using System.Collections.Generic;
using Ked.Progression.Dto;

namespace Ked.Progression
{
    /// <summary>
    /// 지금 호스트가 해야 하는 일.
    ///
    /// <b>다섯이 곧 이 패키지의 외부 연결점 전부다.</b> 늘어나는 변경은 경계면이 넓어진다는
    /// 뜻이므로 한 번 더 본다.
    /// </summary>
    public enum FlowRequestKind
    {
        /// <summary>통과 자리라 부탁할 것이 없다.</summary>
        None = 0,

        /// <summary>대사를 재생하라. 이름은 <see cref="EpisodeNode.DialogueEntryId"/>다.</summary>
        PlayDialogue = 10,

        /// <summary>선택지를 그리고 하나 고르게 하라.</summary>
        PresentOptions = 20,

        /// <summary>지나며 거쳐 갈 연출을 재생하라. 이름은 <c>ViaNodeId</c>다.</summary>
        PlayVia = 30,

        /// <summary>세이브 블록을 파일로 써라. <b>어디에 어떻게 쓰는지는 호스트의 일이다.</b></summary>
        PersistSave = 40,

        /// <summary>더 부탁할 것이 없다. 끝났거나 막혔다.</summary>
        Finished = 900,
    }

    /// <summary>
    /// 경계를 건너가는 부탁 하나. <b>해석 결과이지 상태가 아니다</b> — 저장하지 않는다(P3).
    ///
    /// <b>이 패키지는 아무것도 부르지 않는다.</b> 인터페이스도 콜백도 이벤트도 없고, 대신
    /// 무엇이 필요한지를 값으로 내놓고 호스트가 마쳤다고 알려 준다. 그래서 유니티와
    /// Avalonia가 같은 흐름을 쓰면서도 서로의 어휘를 모른다.
    ///
    /// <see cref="ChapterAdvance"/>와 같은 모양이다 — <see cref="Kind"/>가 정하고, 그 갈래에
    /// 쓰이는 칸만 채워진다.
    /// </summary>
    public readonly struct FlowRequest
    {
        public FlowRequestKind Kind { get; }

        /// <summary>
        /// <see cref="FlowRequestKind.PlayDialogue"/>면 대사 노드,
        /// <see cref="FlowRequestKind.PlayVia"/>면 연출 노드. 그 외에는 빈 문자열이다.
        ///
        /// <b>둘 다 이름 하나뿐이다.</b> 지속시간·이징·화자가 여기 붙기 시작하면 그때가
        /// 경계면이 진짜로 넓어지는 순간이다.
        /// </summary>
        public string NodeName { get; }

        /// <summary>
        /// <see cref="FlowRequestKind.PresentOptions"/>일 때만 채워진다.
        /// <b>배열 순서가 곧 화면 순서다</b> — 정렬하지 않는다.
        /// </summary>
        public IReadOnlyList<ResolvedOption> Options { get; }

        /// <summary>
        /// 표시조건 미달로 목록에서 빠진 개수. 그리는 데는 안 쓰이고 <b>로그가 본다</b> —
        /// 몇 개가 숨겨졌는지조차 모르면 "왜 선택지가 안 뜨지"를 추적할 방법이 없다.
        /// </summary>
        public int HiddenCount { get; }

        /// <summary>
        /// <see cref="FlowRequestKind.PersistSave"/>일 때만 채워진다.
        /// <b>이 패키지는 파일을 만들지 않는다</b> — 굽기만 하고 쓰는 것은 호스트다.
        /// </summary>
        public ProgressionSaveDto Save { get; }

        /// <summary>
        /// <see cref="FlowRequestKind.Finished"/>일 때 어떻게 끝났는가.
        /// 의도한 종착과 막다른 곳이 여기서 구별된다.
        /// </summary>
        public ScenarioAdvance Outcome { get; }

        private FlowRequest(
            FlowRequestKind kind,
            string nodeName,
            IReadOnlyList<ResolvedOption> options,
            int hiddenCount,
            ProgressionSaveDto save,
            in ScenarioAdvance outcome)
        {
            Kind = kind;
            NodeName = nodeName ?? string.Empty;
            Options = options;
            HiddenCount = hiddenCount;
            Save = save;
            Outcome = outcome;
        }

        internal static FlowRequest None() =>
            new FlowRequest(FlowRequestKind.None, string.Empty, null, 0, null, default);

        internal static FlowRequest PlayDialogue(string nodeName) =>
            new FlowRequest(FlowRequestKind.PlayDialogue, nodeName, null, 0, null, default);

        internal static FlowRequest PresentOptions(
            IReadOnlyList<ResolvedOption> options, int hiddenCount) =>
            new FlowRequest(
                FlowRequestKind.PresentOptions, string.Empty, options, hiddenCount, null, default);

        internal static FlowRequest PlayVia(string nodeName) =>
            new FlowRequest(FlowRequestKind.PlayVia, nodeName, null, 0, null, default);

        internal static FlowRequest PersistSave(ProgressionSaveDto save) =>
            new FlowRequest(FlowRequestKind.PersistSave, string.Empty, null, 0, save, default);

        internal static FlowRequest Finished(in ScenarioAdvance outcome) =>
            new FlowRequest(FlowRequestKind.Finished, string.Empty, null, 0, null, outcome);

        public override string ToString()
        {
            switch (Kind)
            {
                case FlowRequestKind.PlayDialogue: return $"대사 재생 \"{NodeName}\"";
                case FlowRequestKind.PlayVia: return $"연출 재생 \"{NodeName}\"";
                case FlowRequestKind.PresentOptions:
                    return $"선택지 {Options.Count}개 (숨김 {HiddenCount})";
                case FlowRequestKind.PersistSave: return "세이브 기록";
                case FlowRequestKind.Finished: return $"종료 — {Outcome.Kind}";
                default: return "없음";
            }
        }
    }
}
