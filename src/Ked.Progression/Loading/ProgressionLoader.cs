using System;
using System.Collections.Generic;
using Ked.Progression.Dto;

namespace Ked.Progression
{
    /// <summary>
    /// DTO -> 모델.
    ///
    /// 오류가 하나라도 있으면 로드 실패로 보고
    /// <see cref="ProgressionLoadResult.Chapter"/>를 <c>null</c>로 냄.
    /// (부분 통과 금지)
    ///
    /// 여기서 새로 쓰는 규칙은 데이터의 모양에 관한 것뿐이다
    /// 알 수 없는 enum 이름,
    /// 언제나 비어 있어야 할 칸이 안 비었을 때,
    /// sentinel 쌍의 불일치.
    /// 
    /// 챕터 전체의 불변식은 <see cref="ChapterInvariants"/> 한 곳에 있고
    /// 여기서는 그것을 모으는 방식으로 쓴다.
    /// </summary>
    public static class ProgressionLoader
    {
        public static ProgressionLoadResult Load(ChapterProgressionDto dto)
        {
            var diagnostics = new List<ProgressionDiagnostic>();

            if (dto == null)
            {
                diagnostics.Add(ProgressionDiagnostic.Error(
                    string.Empty, "챕터 DTO가 null이다. 역직렬화가 실패한 것은 아닌지 확인할 것."));

                return new ProgressionLoadResult(null, diagnostics);
            }

            List<StatDefinition> stats = LoadStats(dto.Stats, diagnostics);
            List<EpisodeNode> nodes = LoadNodes(dto.Nodes, diagnostics);
            List<EndingRule> endingRules = LoadEndingRules(dto.EndingRules, diagnostics);

            if (HasError(diagnostics))
            {
                return new ProgressionLoadResult(null, diagnostics);
            }

            // 챕터 전체의 불변식은 ChapterInvariants가 소유. 여기서는 수집만.
            ChapterInvariants.Collect(
                stats, nodes, endingRules, dto.StartEpisodeId, diagnostics, out _, out _);

            if (string.IsNullOrEmpty(dto.ChapterId))
            {
                diagnostics.Add(ProgressionDiagnostic.Error("ChapterId", "챕터 ID가 비어 있다."));
            }

            if (HasError(diagnostics))
            {
                return new ProgressionLoadResult(null, diagnostics);
            }

            var chapter = new ChapterProgression(
                dto.ChapterId, dto.DisplayName, dto.StartEpisodeId, stats, nodes, endingRules);

            return new ProgressionLoadResult(chapter, diagnostics);
        }

        // 챕터 여러 개를 시나리오로 묶어 싣는다.
        //
        // 시나리오는 저작물이 아니다 — 툴은 챕터 JSON만 낸다. 그래서 조립은 호스트의
        // 일이고, 입구가 DTO가 아니라 인자다. 시나리오 JSON 모양을 두면 아무도 안 쓰는
        // 직렬화 껍질이 하나 늘 뿐이다.
        //
        // 진단에는 Chapters[...] 접두가 붙는다 — 어느 챕터의 어느 자리인지가 한 줄에 다
        // 있어야 한다.
        public static ScenarioLoadResult LoadScenario(
            string scenarioId,
            string displayName,
            string startChapterId,
            IReadOnlyList<ChapterProgressionDto> chapterDtos)
        {
            var diagnostics = new List<ProgressionDiagnostic>();

            if (string.IsNullOrEmpty(scenarioId))
            {
                diagnostics.Add(ProgressionDiagnostic.Error("ScenarioId", "시나리오 ID가 비어 있다."));
            }

            var chapters = new List<ChapterProgression>();
            int count = chapterDtos == null ? 0 : chapterDtos.Count;

            for (int i = 0; i < count; i++)
            {
                ChapterProgressionDto chapterDto = chapterDtos[i];

                string prefix = chapterDto != null && !string.IsNullOrEmpty(chapterDto.ChapterId)
                    ? $"Chapters[{chapterDto.ChapterId}]"
                    : $"Chapters[{i}]";

                ProgressionLoadResult result = Load(chapterDto);

                foreach (ProgressionDiagnostic diagnostic in result.Diagnostics)
                {
                    diagnostics.Add(new ProgressionDiagnostic(
                        diagnostic.Severity,
                        diagnostic.Path.Length == 0 ? prefix : prefix + "." + diagnostic.Path,
                        diagnostic.Message));
                }

                if (result.Chapter != null)
                {
                    chapters.Add(result.Chapter);
                }
            }

            if (HasError(diagnostics))
            {
                return new ScenarioLoadResult(null, diagnostics);
            }

            ScenarioInvariants.Collect(chapters, startChapterId, diagnostics, out _);

            if (HasError(diagnostics))
            {
                return new ScenarioLoadResult(null, diagnostics);
            }

            var scenario = new ScenarioProgression(
                scenarioId, displayName, startChapterId, chapters);

            return new ScenarioLoadResult(scenario, diagnostics);
        }

        // 챕터 하나만 떼어 돌리는 길 — LoadScenario의 N=1이다.
        //
        // 챕터가 여럿이 된 뒤에도 사라지지 않는다. 작가가 ch03만 돌려 보고 싶은 때가
        // 영원히 있고, 호스트 둘이 각자 감싸는 코드를 만들지 않게 여기 한 번 둔다.
        //
        // 챕터가 든 EndingRules는 그대로 실린다. 그중 다음 챕터로 가는 규칙이 있으면
        // 갈 곳이 없어 ScenarioInvariants가 허공 간선으로 잡는다 — 그게 맞는 동작이다.
        // 챕터 하나뿐인데 다음 챕터를 적었다는 뜻이니까.
        public static ScenarioLoadResult LoadAsSingleChapterScenario(ChapterProgressionDto dto)
        {
            var diagnostics = new List<ProgressionDiagnostic>();

            if (dto == null)
            {
                diagnostics.Add(ProgressionDiagnostic.Error(
                    string.Empty, "챕터 DTO가 null이다. 역직렬화가 실패한 것은 아닌지 확인할 것."));

                return new ScenarioLoadResult(null, diagnostics);
            }

            if (string.IsNullOrEmpty(dto.ChapterId))
            {
                // 시나리오 ID와 시작 챕터 ID를 둘 다 여기서 가져온다 — 비면 감쌀 이름이 없다.
                diagnostics.Add(ProgressionDiagnostic.Error(
                    "ChapterId",
                    "챕터 ID가 비어 있다. 챕터 하나짜리 시나리오는 이 ID를 시나리오 ID로도 쓴다."));

                return new ScenarioLoadResult(null, diagnostics);
            }

            return LoadScenario(
                dto.ChapterId, dto.DisplayName, dto.ChapterId,
                new List<ChapterProgressionDto> { dto });
        }

        // ── 엔딩 규칙 ───────────────────────────────────────────────────────

        private static List<EndingRule> LoadEndingRules(
            List<EndingRuleDto> dtos, List<ProgressionDiagnostic> into)
        {
            var rules = new List<EndingRule>();

            if (dtos == null)
            {
                return rules;
            }

            for (int i = 0; i < dtos.Count; i++)
            {
                EndingRuleDto dto = dtos[i];
                string at = $"EndingRules[{i}]";

                if (dto == null)
                {
                    into.Add(ProgressionDiagnostic.Error(at, "엔딩 규칙이 null이다."));
                    continue;
                }

                List<ProgressionCondition> conditions =
                    LoadConditions(dto.Conditions, at + ".Conditions", into);

                bool endsScenario;

                switch (dto.Outcome)
                {
                    case "NextChapter": endsScenario = false; break;
                    case "ScenarioEnd": endsScenario = true; break;
                    default:
                        into.Add(ProgressionDiagnostic.Error(
                            at,
                            $"알 수 없는 결과 '{dto.Outcome}'. 가능한 값: NextChapter, ScenarioEnd. " +
                            "다음 챕터 칸이 비었는지로 판별하지 않는다 — " +
                            "\"끝난다\"와 \"안 적었다\"가 같은 모양이 되기 때문이다."));
                        continue;
                }

                bool hasNext = !string.IsNullOrEmpty(dto.NextChapterId);

                if (endsScenario && hasNext)
                {
                    into.Add(ProgressionDiagnostic.Error(
                        at,
                        $"ScenarioEnd인데 다음 챕터 '{dto.NextChapterId}'가 적혀 있다. " +
                        "둘 중 어느 쪽이 뜻인지 추측하지 않는다."));
                    continue;
                }

                try
                {
                    rules.Add(endsScenario
                        ? EndingRule.Ends(dto.EndingKey, conditions, dto.DisplayName, dto.DesignerNote)
                        : EndingRule.To(
                            dto.EndingKey, dto.NextChapterId, conditions,
                            dto.DisplayName, dto.DesignerNote));
                }
                catch (ArgumentException error)
                {
                    into.Add(ProgressionDiagnostic.Error(at, error.Message));
                }
            }

            return rules;
        }

        // ── 스탯 ────────────────────────────────────────────────────────────

        private static List<StatDefinition> LoadStats(
            List<StatDto> dtos, List<ProgressionDiagnostic> into)
        {
            var stats = new List<StatDefinition>();

            if (dtos == null)
            {
                return stats;
            }

            for (int i = 0; i < dtos.Count; i++)
            {
                StatDto dto = dtos[i];
                string at = $"Stats[{i}]";

                if (dto == null)
                {
                    into.Add(ProgressionDiagnostic.Error(at, "스탯 정의가 null이다."));
                    continue;
                }

                if (!TryParseStatType(dto.Type, out StatType type))
                {
                    into.Add(ProgressionDiagnostic.Error(
                        at,
                        $"알 수 없는 스탯 타입 '{dto.Type}'. 가능한 값: Number, Bool. " +
                        "저작 쪽 Int가 여기서는 Number다 — 내보내기가 이름을 번역한다."));
                    continue;
                }

                try
                {
                    stats.Add(new StatDefinition(
                        dto.Key, dto.DisplayName, type, dto.Initial, dto.Minimum, dto.Maximum));
                }
                catch (ArgumentException error)
                {
                    into.Add(ProgressionDiagnostic.Error(at, error.Message));
                }
            }

            return stats;
        }

        // ── 노드 ────────────────────────────────────────────────────────────

        private static List<EpisodeNode> LoadNodes(
            List<EpisodeNodeDto> dtos, List<ProgressionDiagnostic> into)
        {
            var nodes = new List<EpisodeNode>();

            if (dtos == null)
            {
                return nodes;
            }

            for (int i = 0; i < dtos.Count; i++)
            {
                EpisodeNodeDto dto = dtos[i];
                string at = $"Nodes[{i}]";

                if (dto == null)
                {
                    into.Add(ProgressionDiagnostic.Error(at, "에피소드가 null이다."));
                    continue;
                }

                string where = string.IsNullOrEmpty(dto.EpisodeId)
                    ? at
                    : $"Nodes[{dto.EpisodeId}]";

                VerifyNodeIsLeanedOut(dto, where, into);

                List<EpisodeOption> options = LoadOptions(dto.NextOptions, where, into);

                try
                {
                    nodes.Add(new EpisodeNode(
                        dto.EpisodeId,
                        dto.Title,
                        dto.DialogueEntryId,
                        options,
                        dto.EndingKey,
                        dto.DesignerNote));
                }
                catch (ArgumentException error)
                {
                    into.Add(ProgressionDiagnostic.Error(where, error.Message));
                }
            }

            return nodes;
        }

        /// <summary>
        /// 모델이 안 받는 칸에 값이 들어 있는지 본다. <b>이 검사가 없으면 그 데이터가
        /// 조용히 사라진다.</b>
        /// </summary>
        private static void VerifyNodeIsLeanedOut(
            EpisodeNodeDto dto, string where, List<ProgressionDiagnostic> into)
        {
            // 분기 관문이 노드에서 간선으로 내려감. 마이그레이션 검사용.
            if (Count(dto.VisibleConditions) > 0 || Count(dto.UnlockConditions) > 0)
            {
                into.Add(ProgressionDiagnostic.Error(
                    where,
                    "노드에 표시조건·해금조건이 실려 있다. v8에서 관문은 간선(NextOptions)의 " +
                    "것이 됐고 노드 쪽은 언제나 비어 나온다. 이대로 실으면 그 관문이 " +
                    "조용히 사라져 전부 열린 채로 돈다 — 구판 내보내기가 만든 데이터인지 확인할 것."));
            }

            // sentinel 쌍 — 4조합 중 둘이 무효다.
            bool hasKey = !string.IsNullOrEmpty(dto.EndingKey);

            if (dto.IsChapterEndingCandidate != hasKey)
            {
                into.Add(ProgressionDiagnostic.Error(
                    where,
                    dto.IsChapterEndingCandidate
                        ? "엔딩 후보로 표시됐는데 EndingKey가 비어 있다. 어느 엔딩인지 알 수 없다."
                        : $"EndingKey('{dto.EndingKey}')가 있는데 엔딩 후보로 표시되지 않았다. " +
                          "둘 중 어느 쪽이 뜻인지 추측하지 않는다."));
            }
        }

        // ── 간선 ────────────────────────────────────────────────────────────

        private static List<EpisodeOption> LoadOptions(
            List<EpisodeOptionDto> dtos, string nodePath, List<ProgressionDiagnostic> into)
        {
            var options = new List<EpisodeOption>();

            if (dtos == null)
            {
                return options;
            }

            for (int i = 0; i < dtos.Count; i++)
            {
                EpisodeOptionDto dto = dtos[i];
                string at = $"{nodePath}.NextOptions[{i}]";

                if (dto == null)
                {
                    into.Add(ProgressionDiagnostic.Error(at, "간선이 null이다."));
                    continue;
                }

                List<ProgressionCondition> visible =
                    LoadConditions(dto.VisibleConditions, at + ".VisibleConditions", into);
                List<ProgressionCondition> conditions =
                    LoadConditions(dto.Conditions, at + ".Conditions", into);
                List<StatChange> changes =
                    LoadStatChanges(dto.StatChanges, at + ".StatChanges", into);

                bool isAuto = string.IsNullOrEmpty(dto.ChoiceLabel);

                try
                {
                    if (!isAuto)
                    {
                        options.Add(EpisodeOption.Choice(
                            dto.ChoiceLabel,
                            dto.TargetEpisodeId,
                            visible,
                            conditions,
                            dto.LockedReasonText,
                            changes,
                            dto.ViaNodeId));

                        continue;
                    }

                    // 자동 진행이 받지 않는 칸에 값이 있으면 그대로 실을 때 조용히 사라진다.
                    if (visible.Count > 0 || conditions.Count > 0)
                    {
                        into.Add(ProgressionDiagnostic.Error(
                            at,
                            "문구 없는 간선(자동 진행)에 관문이 달렸다(§G6-2). 그 관문마저 막히면 " +
                            "챕터가 조용히 끝난다 — 문구를 주어 보통 선택지로 만들거나 조건을 뗄 것."));

                        continue;
                    }

                    if (!string.IsNullOrEmpty(dto.LockedReasonText))
                    {
                        into.Add(ProgressionDiagnostic.Error(
                            at,
                            "문구 없는 간선(자동 진행)에 잠금 설정이 달렸다. 자동 진행은 잠기지 " +
                            "않으므로 이 값은 실리지 않는다 — 조용히 버리지 않는다."));

                        continue;
                    }

                    options.Add(EpisodeOption.Auto(dto.TargetEpisodeId, changes, dto.ViaNodeId));
                }
                catch (ArgumentException error)
                {
                    into.Add(ProgressionDiagnostic.Error(at, error.Message));
                }
            }

            return options;
        }

        private static List<ProgressionCondition> LoadConditions(
            List<ConditionDto> dtos, string where, List<ProgressionDiagnostic> into)
        {
            var conditions = new List<ProgressionCondition>();

            if (dtos == null)
            {
                return conditions;
            }

            for (int i = 0; i < dtos.Count; i++)
            {
                ConditionDto dto = dtos[i];
                string at = $"{where}[{i}]";

                if (dto == null)
                {
                    into.Add(ProgressionDiagnostic.Error(at, "조건이 null이다."));
                    continue;
                }

                if (!TryParseConditionKind(dto.Kind, out _))
                {
                    into.Add(ProgressionDiagnostic.Error(
                        at, $"알 수 없는 조건 종류 '{dto.Kind}'. 가능한 값: Stat."));
                    continue;
                }

                if (!TryParseComparisonOp(dto.Op, out ComparisonOp op))
                {
                    into.Add(ProgressionDiagnostic.Error(
                        at,
                        $"알 수 없는 비교 연산 '{dto.Op}'. 가능한 값: " +
                        "GreaterOrEqual, LessOrEqual, Equal, Exists, GreaterThan, LessThan. " +
                        "(NotEqual은 일부러 없다 — 저작 파서가 닫아 두어 데이터로 나오지 않는다.)"));
                    continue;
                }

                try
                {
                    conditions.Add(ProgressionCondition.Stat(dto.Key, op, dto.IntValue));
                }
                catch (ArgumentException error)
                {
                    into.Add(ProgressionDiagnostic.Error(at, error.Message));
                }
            }

            return conditions;
        }

        private static List<StatChange> LoadStatChanges(
            List<StatChangeDto> dtos, string where, List<ProgressionDiagnostic> into)
        {
            var changes = new List<StatChange>();

            if (dtos == null)
            {
                return changes;
            }

            for (int i = 0; i < dtos.Count; i++)
            {
                StatChangeDto dto = dtos[i];

                if (dto == null)
                {
                    continue;
                }

                string at = $"{where}[{i}]";

                // 빈 칸은 Add다 — 이 칸이 서기 전에 나간 JSON이 그대로 실려야 한다.
                if (string.IsNullOrEmpty(dto.Op))
                {
                    changes.Add(StatChange.Add(dto.Key, dto.Amount));
                    continue;
                }

                if (!TryParseStatChangeOp(dto.Op, out StatChangeKind kind))
                {
                    // 규율 1 — 모르는 이름을 Add로 읽으면 깃발을 켜려던 간선이 아무것도
                    // 안 하는 간선이 되고, 그 버그는 재생해 봐도 안 보인다.
                    into.Add(ProgressionDiagnostic.Error(
                        at,
                        $"알 수 없는 스탯 변화 종류 '{dto.Op}'. 가능한 값: Add, Set " +
                        "(비어 있으면 Add)."));

                    continue;
                }

                // 키의 실재와 bool 어휘는 ChapterInvariants가 본다 — 여기서 또 보지 않는다.
                changes.Add(kind == StatChangeKind.Set
                    ? StatChange.Set(dto.Key, dto.Amount)
                    : StatChange.Add(dto.Key, dto.Amount));
            }

            return changes;
        }

        // ── enum 이름 ───────────────────────────────────────────────────────
        //
        // §G1 — enum은 이름 문자열로 온다. Enum.TryParse를 쓰지 않는 이유가 둘 있다:
        // 숫자 문자열("0")도 통과시키고, 없는 이름을 캐스팅해 넣는 오버로드가 섞이기 쉽다.
        // 명시 목록이면 "가능한 값"을 그대로 진단에 실을 수 있다.

        private static bool TryParseStatType(string name, out StatType value)
        {
            switch (name)
            {
                case "Number": value = StatType.Number; return true;
                case "Bool": value = StatType.Bool; return true;
                default: value = default; return false;
            }
        }

        private static bool TryParseStatChangeOp(string name, out StatChangeKind value)
        {
            switch (name)
            {
                case "Add": value = StatChangeKind.Add; return true;
                case "Set": value = StatChangeKind.Set; return true;
                default: value = default; return false;
            }
        }

        private static bool TryParseConditionKind(string name, out ConditionKind value)
        {
            switch (name)
            {
                case "Stat": value = ConditionKind.Stat; return true;
                default: value = default; return false;
            }
        }

        private static bool TryParseComparisonOp(string name, out ComparisonOp value)
        {
            switch (name)
            {
                case "GreaterOrEqual": value = ComparisonOp.GreaterOrEqual; return true;
                case "LessOrEqual": value = ComparisonOp.LessOrEqual; return true;
                case "Equal": value = ComparisonOp.Equal; return true;
                case "Exists": value = ComparisonOp.Exists; return true;
                case "GreaterThan": value = ComparisonOp.GreaterThan; return true;
                case "LessThan": value = ComparisonOp.LessThan; return true;
                default: value = default; return false;
            }
        }

        private static int Count<T>(List<T> list) => list == null ? 0 : list.Count;

        private static bool HasError(List<ProgressionDiagnostic> diagnostics)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == ProgressionDiagnosticSeverity.Error)
                    return true;
            }

            return false;
        }
    }
}
