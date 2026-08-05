using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 리듀서가 접는 커맨드 하나. 이름 + 문자열 인자 + 출처.
    /// 런타임은 Yarn 커맨드에서, VnTool은 발행된 출력 커맨드에서 만든다 — 같은 모양이다.
    /// </summary>
    public readonly struct StageCommand
    {
        public readonly string Name;
        public readonly IReadOnlyList<string> Args;

        /// <summary>어디서 온 커맨드인가 (파일/노드/라인). Unhandled 보고에 그대로 실린다.</summary>
        public readonly string Source;

        public StageCommand(string name, IReadOnlyList<string> args, string source = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Args = args ?? Array.Empty<string>();
            Source = source;
        }

        public string Arg(int index, string fallback = null)
            => index < Args.Count && !string.IsNullOrEmpty(Args[index]) ? Args[index] : fallback;

        public override string ToString()
            => $"<<{Name} {string.Join(" ", Args)}>>{(Source != null ? $" @ {Source}" : "")}";
    }

    /// <summary>접지 못한 커맨드의 기록. 조용히 버리지 않는 것이 이 프로젝트의 규율이다.</summary>
    public readonly struct UnhandledCommand
    {
        public readonly StageCommand Command;
        public readonly string Reason;

        public UnhandledCommand(in StageCommand command, string reason)
        {
            Command = command;
            Reason = reason;
        }

        public override string ToString() => $"{Command} — {Reason}";
    }

    /// <summary>
    /// 슬롯 리그의 구조 부착 상태 (char_to / sibling 계열의 축).
    /// 스테이지·레이어 어휘는 게임 데이터라 문자열로 담는다 — 코어는 뜻을 모른다.
    /// </summary>
    public readonly struct SlotAttachment
    {
        public readonly string StageKey;
        public readonly string LayerKey;

        public SlotAttachment(string stageKey, string layerKey)
        {
            StageKey = stageKey;
            LayerKey = layerKey;
        }
    }

    /// <summary>
    /// 무대 확정 상태 — 커맨드 열을 접은 결과이자 정지 프레임의 원료 (U14).
    ///
    /// 축: 노드 트리(좌표) · 가시성(alpha) · 샷 의도 · 슬롯 부착(구조) ·
    /// 배역/초상 선택 상태 · Unhandled. 배경·박스·이펙트 축은 아직 없다 —
    /// 커맨드가 오면 Unhandled에 남지, 조용히 사라지지 않는다.
    ///
    /// 리듀서 계약(§6.2): Apply는 이 객체를 바꾸지 않고 새 상태를 돌려준다.
    /// </summary>
    public sealed class StageState
    {
        public RectNodeTree Nodes { get; private set; }
        public ShotIntentState Shot { get; set; }

        private readonly Dictionary<string, float> _alphas;
        private readonly Dictionary<string, SlotAttachment> _attachments;
        private readonly List<UnhandledCommand> _unhandled;

        /// <summary>슬롯 키 → 리그 노드 키 prefix ("c1" → "c1/"). 스폰된 슬롯의 존재 기록.</summary>
        private readonly HashSet<string> _slots;

        // 배역 축: cast가 캐릭터를 슬롯에 앉힌다. (초상 스프라이트 축은 아직 없다 —
        // 여기 담는 것은 커맨드의 대상 해석에 필요한 "누가 어느 슬롯인가"뿐이다.)
        private readonly Dictionary<string, string> _slotByCharacter;
        private readonly Dictionary<string, string> _characterBySlot;

        // 별칭 축: actor가 "@3" 같은 기호를 캐릭터/슬롯 키에 잇는다.
        private readonly Dictionary<string, string> _aliases;

        // 초상 resolver 입력 축: 슬롯 → (변형 접미사, 정규화된 표정 코드).
        // pose가 변형을, show/face/face_swap이 표정을 갱신한다.
        private readonly Dictionary<string, (char variantSuffix, string emotion)> _portraits;

        public IReadOnlyList<UnhandledCommand> Unhandled => _unhandled;
        public IReadOnlyCollection<string> Slots => _slots;

        public StageState(RectSpace rootSpace)
        {
            Nodes = new RectNodeTree(rootSpace);
            Shot = ShotIntentState.Default;
            _alphas = new Dictionary<string, float>(StringComparer.Ordinal);
            _attachments = new Dictionary<string, SlotAttachment>(StringComparer.Ordinal);
            _unhandled = new List<UnhandledCommand>();
            _slots = new HashSet<string>(StringComparer.Ordinal);
            _slotByCharacter = new Dictionary<string, string>(StringComparer.Ordinal);
            _characterBySlot = new Dictionary<string, string>(StringComparer.Ordinal);
            _aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            _portraits = new Dictionary<string, (char, string)>(StringComparer.Ordinal);
        }

        private StageState(StageState source)
        {
            Nodes = source.Nodes.Clone();
            Shot = source.Shot;
            _alphas = new Dictionary<string, float>(source._alphas, StringComparer.Ordinal);
            _attachments = new Dictionary<string, SlotAttachment>(source._attachments, StringComparer.Ordinal);
            _unhandled = new List<UnhandledCommand>(source._unhandled);
            _slots = new HashSet<string>(source._slots, StringComparer.Ordinal);
            _slotByCharacter = new Dictionary<string, string>(source._slotByCharacter, StringComparer.Ordinal);
            _characterBySlot = new Dictionary<string, string>(source._characterBySlot, StringComparer.Ordinal);
            _aliases = new Dictionary<string, string>(source._aliases, StringComparer.Ordinal);
            _portraits = new Dictionary<string, (char, string)>(source._portraits, StringComparer.Ordinal);
        }

        public StageState Clone() => new StageState(this);

        // ── 슬롯 ─────────────────────────────────────────────────────

        public bool HasSlot(string slotKey) => _slots.Contains(slotKey);

        public void RegisterSlot(string slotKey) => _slots.Add(slotKey);

        /// <summary>슬롯 리그의 노드 키. 예: NodeKeyOf("c1", "CharSlot_Track") → "c1/CharSlot_Track".</summary>
        public static string NodeKeyOf(string slotKey, string nodeId) => $"{slotKey}/{nodeId}";

        /// <summary>cast — 캐릭터를 슬롯에 앉힌다. 같은 캐릭터의 재배역은 갈아탄다.</summary>
        public void SetCast(string slotKey, string characterKey)
        {
            // 이전 배역 관계를 정리한다 (슬롯 재사용·캐릭터 이동 양쪽).
            if (_characterBySlot.TryGetValue(slotKey, out string previousCharacter))
                _slotByCharacter.Remove(previousCharacter);

            if (_slotByCharacter.TryGetValue(characterKey, out string previousSlot))
                _characterBySlot.Remove(previousSlot);

            _characterBySlot[slotKey] = characterKey;
            _slotByCharacter[characterKey] = slotKey;
        }

        /// <summary>actor — 별칭 기호("@3")를 캐릭터/슬롯 키에 잇는다.</summary>
        public void SetAlias(string aliasSymbol, string targetKey) => _aliases[aliasSymbol] = targetKey;

        /// <summary>슬롯에 앉은 캐릭터 키. 배역이 없으면 false — focus 튜닝은 base만 적용된다.</summary>
        public bool TryGetCharacter(string slotKey, out string characterKey)
            => _characterBySlot.TryGetValue(slotKey, out characterKey);

        public void SetPortrait(string slotKey, char variantSuffix, string emotion)
            => _portraits[slotKey] = (variantSuffix, emotion);

        public bool TryGetPortrait(string slotKey, out char variantSuffix, out string emotion)
        {
            if (_portraits.TryGetValue(slotKey, out (char v, string e) p))
            {
                variantSuffix = p.v;
                emotion = p.e;
                return true;
            }

            variantSuffix = 'a';
            emotion = null;
            return false;
        }

        /// <summary>
        /// 커맨드 대상 키 → 슬롯 키. 별칭이면 풀고, 캐릭터 키면 배역 맵으로 슬롯을 찾는다.
        /// (런타임 CharacterRigTargetResolver의 해석 순서를 폴드 쪽에서 재현한 것)
        /// </summary>
        public bool TryResolveSlot(string targetKey, out string slotKey)
        {
            slotKey = null;

            if (string.IsNullOrEmpty(targetKey))
                return false;

            if (_aliases.TryGetValue(targetKey, out string aliased))
                targetKey = aliased;

            if (_slots.Contains(targetKey))
            {
                slotKey = targetKey;
                return true;
            }

            if (_slotByCharacter.TryGetValue(targetKey, out string bySlot) && _slots.Contains(bySlot))
            {
                slotKey = bySlot;
                return true;
            }

            return false;
        }

        // ── 가시성 축 ────────────────────────────────────────────────

        /// <summary>기록이 없으면 1(보임)이다 — 스폰 직후 기본값.</summary>
        public float GetAlpha(string nodeKey)
            => _alphas.TryGetValue(nodeKey, out float alpha) ? alpha : 1f;

        public void SetAlpha(string nodeKey, float alpha) => _alphas[nodeKey] = alpha;

        // ── 구조 축 ──────────────────────────────────────────────────

        public bool TryGetAttachment(string slotKey, out SlotAttachment attachment)
            => _attachments.TryGetValue(slotKey, out attachment);

        public void SetAttachment(string slotKey, in SlotAttachment attachment)
            => _attachments[slotKey] = attachment;

        // ── 클레임 적용 ──────────────────────────────────────────────

        /// <summary>리덕션 출력을 상태에 접는다. 트랜스폼은 트리로, alpha는 가시성 축으로.</summary>
        public void Apply(in StageNodeClaim claim)
        {
            if (claim.Kind == StageNodeClaimKind.CanvasAlpha)
            {
                SetAlpha(claim.NodeKey, claim.Value.X);
                return;
            }

            claim.ApplyTo(Nodes);
        }

        // ── Unhandled ────────────────────────────────────────────────

        public void AddUnhandled(in StageCommand command, string reason)
            => _unhandled.Add(new UnhandledCommand(command, reason));
    }
}