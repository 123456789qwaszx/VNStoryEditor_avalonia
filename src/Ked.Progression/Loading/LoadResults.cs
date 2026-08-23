using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    /// <summary>시나리오 로드 결과. 챕터와 같은 규율이다 — 오류가 있으면 아무것도 내지 않는다.</summary>
    public sealed class ScenarioLoadResult
    {
        public ScenarioProgression Scenario { get; }
        public IReadOnlyList<ProgressionDiagnostic> Diagnostics { get; }

        public ScenarioLoadResult(
            ScenarioProgression scenario, IReadOnlyList<ProgressionDiagnostic> diagnostics)
        {
            Scenario = scenario;
            Diagnostics = diagnostics ?? Array.Empty<ProgressionDiagnostic>();
        }

        public bool IsValid => Scenario != null;

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Diagnostics.Count; i++)
                {
                    if (Diagnostics[i].Severity == ProgressionDiagnosticSeverity.Error)
                        return true;
                }

                return false;
            }
        }
    }

    /// <summary>
    /// 로드 결과. <b>진단이 하나라도 오류면 <see cref="Chapter"/>는 <c>null</c>이다</b> —
    /// 부분 통과를 만들지 않는다. 반쯤 실린 챕터는 재생해 봐야 무엇이 빠졌는지 알 수 있고,
    /// 그건 이 패키지가 막으려는 종류의 침묵이다.
    /// </summary>
    public sealed class ProgressionLoadResult
    {
        public ChapterProgression Chapter { get; }
        public IReadOnlyList<ProgressionDiagnostic> Diagnostics { get; }

        public ProgressionLoadResult(
            ChapterProgression chapter, IReadOnlyList<ProgressionDiagnostic> diagnostics)
        {
            Chapter = chapter;
            Diagnostics = diagnostics ?? Array.Empty<ProgressionDiagnostic>();
        }

        public bool IsValid => Chapter != null;

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Diagnostics.Count; i++)
                {
                    if (Diagnostics[i].Severity == ProgressionDiagnosticSeverity.Error)
                        return true;
                }

                return false;
            }
        }
    }
}
