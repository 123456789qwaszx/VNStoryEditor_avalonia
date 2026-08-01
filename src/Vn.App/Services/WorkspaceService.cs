using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Vn.App.Services;

/// <summary>
/// 편집기 전용 데이터. 게임은 이 파일을 읽지 않는다.
///
/// 그래프 좌표는 <c>.yarn</c>에 넣을 자리가 없다. 넣으면 앱이 원본 형식을 오염시키게 되고,
/// 3절 원칙을 어긴다. 그래서 프로젝트 폴더에 따로 둔다.
///
/// <b>좌표 말고는 아무것도 저장하지 않는다.</b> 분석 결과는 원본에서 다시 계산되는 것이라
/// 여기 담으면 두 개의 진실이 생긴다.
///
/// 파일이 없거나 깨졌으면 조용히 빈 상태로 돌아간다. 편집기 편의를 위한 것이 못 열린다고
/// 작가가 프로젝트를 못 여는 일이 있어서는 안 된다.
/// </summary>
internal static class WorkspaceService
{
    public const string FileName = "vn.workspace.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    public static string PathFor(string projectPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))
            ?? Environment.CurrentDirectory;

        return Path.Combine(directory, FileName);
    }

    /// <summary>
    /// 저장된 좌표. 파일이 없거나 읽을 수 없으면 빈 사전이다. 예외를 밖으로 내보내지 않는다.
    /// </summary>
    public static IReadOnlyDictionary<string, NodePosition> LoadPositions(string projectPath)
    {
        var empty = new Dictionary<string, NodePosition>(StringComparer.Ordinal);

        try
        {
            string path = PathFor(projectPath);

            if (!File.Exists(path))
            {
                return empty;
            }

            WorkspaceFile? file = JsonSerializer.Deserialize<WorkspaceFile>(
                File.ReadAllText(path),
                ReadOptions);

            if (file?.NodePositions is null)
            {
                return empty;
            }

            var positions = new Dictionary<string, NodePosition>(StringComparer.Ordinal);

            foreach ((string title, NodePosition? position) in file.NodePositions)
            {
                // 좌표가 숫자가 아니거나 비어 있으면 그 노드만 자동 배치로 돌아간다.
                if (!string.IsNullOrWhiteSpace(title) &&
                    position is not null &&
                    double.IsFinite(position.X) &&
                    double.IsFinite(position.Y))
                {
                    positions[title] = position;
                }
            }

            return positions;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                NotSupportedException or
                ArgumentException)
        {
            // 깨진 편의 파일 때문에 앱이 죽지 않는다. 자동 배치로 돌아간다.
            return empty;
        }
    }

    /// <summary>좌표를 저장한다. 실패해도 조용히 넘어간다. 원고가 아니다.</summary>
    public static void SavePositions(
        string projectPath,
        IReadOnlyDictionary<string, NodePosition> positions)
    {
        try
        {
            var file = new WorkspaceFile
            {
                NodePositions = new Dictionary<string, NodePosition>(
                    positions,
                    StringComparer.Ordinal)
            };

            File.WriteAllText(
                PathFor(projectPath),
                JsonSerializer.Serialize(file, WriteOptions),
                new UTF8Encoding(false));
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                NotSupportedException or
                ArgumentException)
        {
            // 좌표를 못 남겨도 다음에 자동 배치되면 그만이다.
        }
    }

    private sealed class WorkspaceFile
    {
        public Dictionary<string, NodePosition>? NodePositions { get; set; }
    }
}

/// <summary>그래프에서의 노드 위치. 이 파일에 담기는 것은 이것뿐이다.</summary>
internal sealed class NodePosition
{
    public double X { get; set; }

    public double Y { get; set; }
}
