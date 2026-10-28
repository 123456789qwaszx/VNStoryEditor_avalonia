using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Vn.Authoring.Model;

namespace Vn.App.Services;

/// <summary>
/// 에셋 루트 폴더 선택의 단일 구현. 무대 프리뷰 패널과 에셋 탐색기가 같은 규칙을 쓴다 —
/// 가능하면 프로젝트 기준 상대 경로로 저장(프로젝트 이동 내성), 다른 드라이브면 절대 경로.
/// </summary>
internal static class AssetRootPicker
{
    /// <summary>폴더를 골라 프로젝트 설정에 저장한다. 골랐으면 true.</summary>
    public static async Task<bool> PickAsync(Control anchor, AuthoringSession session, bool backgrounds)
    {
        if (TopLevel.GetTopLevel(anchor)?.StorageProvider is not { } storage)
        {
            return false;
        }

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = backgrounds ? "배경 PNG 폴더" : "초상화 폴더 (portraits.manifest.json 포함)",
                AllowMultiple = false
            });

        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } picked)
        {
            return false;
        }

        string stored = picked;

        if (session.ProjectPath is { } projectPath)
        {
            string projectDirectory = Path.GetDirectoryName(projectPath) ?? projectPath;
            string relative = Path.GetRelativePath(projectDirectory, picked);

            if (!Path.IsPathRooted(relative))
            {
                stored = relative;
            }
        }

        AssetRootSettings roots = session.Project.AssetRoots;
        session.Editor.SetAssetRoots(
            backgrounds ? stored : roots.BackgroundsPath,
            backgrounds ? roots.PortraitsPath : stored);
        return true;
    }
}
