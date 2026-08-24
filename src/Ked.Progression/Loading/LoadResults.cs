using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 시나리오 로드 결과.
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