using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace JSAI.WinApp
{
    public sealed class ProjectLibraryExportResult
    {
        public ProjectLibraryExportResult(string rootPath, int characterFolderCount, int storyboardFolderCount)
        {
            RootPath = rootPath;
            CharacterFolderCount = characterFolderCount;
            StoryboardFolderCount = storyboardFolderCount;
        }

        public string RootPath { get; }

        public int CharacterFolderCount { get; }

        public int StoryboardFolderCount { get; }
    }

    public static class ProjectLibraryExportService
    {
        public static ProjectLibraryExportResult Export(WorkflowDocument document)
        {
            var rootPath = CharacterAssetExportService.GetProjectRootPath(document.ProjectName);
            var characterCount = 0;
            var storyboardCount = 0;

            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(Path.Combine(rootPath, "jiaose"));
            Directory.CreateDirectory(Path.Combine(rootPath, "fenjing"));

            foreach (var entry in document.Nodes
                         .Where(node => node.Type == WorkflowNodeCatalog.CharacterView)
                         .SelectMany(node => node.Params?.CharacterEntries ?? Enumerable.Empty<CharacterDesignEntry>())
                         .Where(entry => entry.HasProfileData || entry.HasExpressionSheet || entry.HasThreeViewSheet))
            {
                CharacterAssetExportService.Export(entry, rootPath);
                characterCount++;
            }

            foreach (var node in document.Nodes.Where(node =>
                         node.Type == WorkflowNodeCatalog.StoryboardImage ||
                         node.Type == WorkflowNodeCatalog.StoryboardBreakdown ||
                         node.Type == WorkflowNodeCatalog.StoryboardVideo ||
                         node.Type == WorkflowNodeCatalog.VideoCollection))
            {
                if (!ExportStoryboardNode(rootPath, node))
                {
                    continue;
                }

                storyboardCount++;
            }

            return new ProjectLibraryExportResult(rootPath, characterCount, storyboardCount);
        }

        public static string ExportStoryboardNodeToProjectFolder(string? projectName, WorkflowNode node)
        {
            var rootPath = CharacterAssetExportService.GetProjectRootPath(projectName);
            Directory.CreateDirectory(rootPath);
            Directory.CreateDirectory(Path.Combine(rootPath, "fenjing"));
            ExportStoryboardNode(rootPath, node);
            return GetStoryboardNodeDirectory(rootPath, node);
        }

        public static string ExportStoryboardVideoFusionAssets(string? projectName, WorkflowNode node, string saveName)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var rootPath = CharacterAssetExportService.GetProjectRootPath(projectName);
            var baseDirectory = Path.Combine(rootPath, "fenjingRH", SanitizeFileSegment(saveName));
            Directory.CreateDirectory(baseDirectory);

            if (!string.IsNullOrWhiteSpace(node.Params.StoryboardVideoFusedImagePath) &&
                File.Exists(node.Params.StoryboardVideoFusedImagePath))
            {
                var extension = Path.GetExtension(node.Params.StoryboardVideoFusedImagePath);
                var targetPath = Path.Combine(
                    baseDirectory,
                    $"融合参考图{(string.IsNullOrWhiteSpace(extension) ? ".png" : extension)}");
                File.Copy(node.Params.StoryboardVideoFusedImagePath, targetPath, true);
            }

            if (!string.IsNullOrWhiteSpace(node.Params.StoryboardVideoPrompt))
            {
                File.WriteAllText(
                    Path.Combine(baseDirectory, "视频生成提示词.txt"),
                    node.Params.StoryboardVideoPrompt.Trim(),
                    Encoding.UTF8);
            }

            if (!string.IsNullOrWhiteSpace(node.Params.StoryboardVideoModelPrompt))
            {
                File.WriteAllText(
                    Path.Combine(baseDirectory, "分镜视频英文执行提示词.txt"),
                    node.Params.StoryboardVideoModelPrompt.Trim(),
                    Encoding.UTF8);
            }

            if (node.Params.StoryboardShots.Count > 0)
            {
                var selectedIds = new HashSet<string>(
                    node.Params.StoryboardVideoSelectedShotIds ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
                var selectedShots = node.Params.StoryboardShots
                    .Where(shot => selectedIds.Count == 0 || selectedIds.Contains(shot.Id))
                    .ToList();
                if (selectedShots.Count > 0)
                {
                    File.WriteAllText(
                        Path.Combine(baseDirectory, "分镜描述.txt"),
                        BuildStoryboardShotText(selectedShots),
                        Encoding.UTF8);
                }
            }

            return baseDirectory;
        }

        public static bool ExportStoryboardNode(string rootPath, WorkflowNode node)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var hasPages = node.Params.StoryboardGridPagePaths != null && node.Params.StoryboardGridPagePaths.Count > 0;
            var hasShots = node.Params.StoryboardShots != null && node.Params.StoryboardShots.Count > 0;
            var hasVideoArtifact = !string.IsNullOrWhiteSpace(node.ArtifactPath) && File.Exists(node.ArtifactPath);
            var hasFusedReference = node.Type == WorkflowNodeCatalog.StoryboardVideo &&
                                    !string.IsNullOrWhiteSpace(node.Params.StoryboardVideoFusedImagePath) &&
                                    File.Exists(node.Params.StoryboardVideoFusedImagePath);
            var hasOutput = !string.IsNullOrWhiteSpace(node.Output) || !string.IsNullOrWhiteSpace(node.Params.Input);

            if (!hasPages && !hasShots && !hasOutput && !hasVideoArtifact && !hasFusedReference)
            {
                return false;
            }

            var baseDirectory = GetStoryboardNodeDirectory(rootPath, node);
            Directory.CreateDirectory(baseDirectory);

            if (!string.IsNullOrWhiteSpace(node.Params.Input))
            {
                File.WriteAllText(
                    Path.Combine(baseDirectory, "分镜输入描述.txt"),
                    node.Params.Input.Trim(),
                    Encoding.UTF8);
            }

            if (!string.IsNullOrWhiteSpace(node.Output))
            {
                File.WriteAllText(
                    Path.Combine(baseDirectory, "分镜节点输出.txt"),
                    node.Output.Trim(),
                    Encoding.UTF8);
            }

            if (node.Type == WorkflowNodeCatalog.StoryboardVideo &&
                !string.IsNullOrWhiteSpace(node.Params.StoryboardVideoPrompt))
            {
                File.WriteAllText(
                    Path.Combine(baseDirectory, "分镜视频提示词.txt"),
                    node.Params.StoryboardVideoPrompt.Trim(),
                    Encoding.UTF8);
            }

            if (node.Type == WorkflowNodeCatalog.StoryboardVideo &&
                !string.IsNullOrWhiteSpace(node.Params.StoryboardVideoModelPrompt))
            {
                File.WriteAllText(
                    Path.Combine(baseDirectory, "分镜视频英文执行提示词.txt"),
                    node.Params.StoryboardVideoModelPrompt.Trim(),
                    Encoding.UTF8);
            }

            if (hasShots)
            {
                File.WriteAllText(
                    Path.Combine(baseDirectory, "分镜描述.txt"),
                    BuildStoryboardShotText(node.Params.StoryboardShots ?? Enumerable.Empty<StoryboardShot>()),
                    Encoding.UTF8);
            }

            if (node.Type == WorkflowNodeCatalog.StoryboardVideo && hasShots)
            {
                var selectedIds = new HashSet<string>(
                    node.Params.StoryboardVideoSelectedShotIds ?? Enumerable.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase);
                var selectedShots = (node.Params.StoryboardShots ?? Enumerable.Empty<StoryboardShot>())
                    .Where(shot => selectedIds.Contains(shot.Id))
                    .ToList();
                if (selectedShots.Count > 0)
                {
                    File.WriteAllText(
                        Path.Combine(baseDirectory, "分镜视频镜头.txt"),
                        BuildStoryboardShotText(selectedShots),
                        Encoding.UTF8);
                }
            }

            var pagePaths = node.Params.StoryboardGridPagePaths?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList() ?? new List<string>();

            if (pagePaths.Count == 0 && !string.IsNullOrWhiteSpace(node.ArtifactPath) &&
                (node.Type == WorkflowNodeCatalog.StoryboardImage || node.Type == WorkflowNodeCatalog.StoryboardBreakdown))
            {
                pagePaths.Add(node.ArtifactPath);
            }

            for (var index = 0; index < pagePaths.Count; index++)
            {
                var pagePath = pagePaths[index];
                if (string.IsNullOrWhiteSpace(pagePath) || !File.Exists(pagePath))
                {
                    continue;
                }

                var extension = Path.GetExtension(pagePath);
                var targetPath = Path.Combine(
                    baseDirectory,
                    $"分镜图设计_{index + 1:D2}{(string.IsNullOrWhiteSpace(extension) ? ".png" : extension)}");
                File.Copy(pagePath, targetPath, true);
            }

            var splitImagePaths = (node.Params.StoryboardShots ?? Enumerable.Empty<StoryboardShot>())
                .Select(shot => new
                {
                    shot.ShotNumber,
                    shot.SplitImagePath,
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.SplitImagePath) && File.Exists(item.SplitImagePath))
                .ToList();

            foreach (var item in splitImagePaths)
            {
                var extension = Path.GetExtension(item.SplitImagePath);
                var targetPath = Path.Combine(
                    baseDirectory,
                    $"分镜镜头_{item.ShotNumber:D2}{(string.IsNullOrWhiteSpace(extension) ? ".png" : extension)}");
                File.Copy(item.SplitImagePath, targetPath, true);
            }

            if (node.Type == WorkflowNodeCatalog.StoryboardVideo && hasFusedReference)
            {
                var extension = Path.GetExtension(node.Params.StoryboardVideoFusedImagePath);
                var targetPath = Path.Combine(
                    baseDirectory,
                    $"分镜视频融合参考图{(string.IsNullOrWhiteSpace(extension) ? ".png" : extension)}");
                File.Copy(node.Params.StoryboardVideoFusedImagePath, targetPath, true);
            }

            if (node.Type == WorkflowNodeCatalog.VideoCollection &&
                !string.IsNullOrWhiteSpace(node.Params.VideoCollectionPlaylistPath) &&
                File.Exists(node.Params.VideoCollectionPlaylistPath))
            {
                File.Copy(
                    node.Params.VideoCollectionPlaylistPath,
                    Path.Combine(baseDirectory, "视频合集清单.txt"),
                    true);
            }

            if (hasVideoArtifact)
            {
                var extension = Path.GetExtension(node.ArtifactPath);
                var fileName = node.Type == WorkflowNodeCatalog.VideoCollection
                    ? $"视频合集{(string.IsNullOrWhiteSpace(extension) ? ".mp4" : extension)}"
                    : $"分镜视频{(string.IsNullOrWhiteSpace(extension) ? ".mp4" : extension)}";
                File.Copy(node.ArtifactPath, Path.Combine(baseDirectory, fileName), true);
            }

            return true;
        }

        private static string GetStoryboardNodeDirectory(string rootPath, WorkflowNode node)
        {
            var folderName = SanitizeFileSegment($"{node.Id}_{node.Type}");
            return Path.Combine(rootPath, "fenjing", folderName);
        }

        private static string BuildStoryboardShotText(IEnumerable<StoryboardShot> shots)
        {
            var builder = new StringBuilder();
            foreach (var shot in shots ?? Enumerable.Empty<StoryboardShot>())
            {
                builder.AppendLine($"分镜 #{Math.Max(1, shot.ShotNumber)}");
                AppendSection(builder, "场景", shot.Scene);
                AppendSection(builder, "角色", shot.CharactersDisplay);
                AppendSection(builder, "时长", $"{Math.Max(1, shot.DurationSeconds)} 秒");
                AppendSection(builder, "景别", shot.ShotSize);
                AppendSection(builder, "拍摄角度", shot.CameraAngle);
                AppendSection(builder, "运镜方式", shot.CameraMovement);
                AppendSection(builder, "画面描述", shot.VisualDescription);
                AppendSection(builder, "对白", shot.Dialogue);
                AppendSection(builder, "视觉效果", shot.VisualEffects);
                AppendSection(builder, "音效", shot.AudioEffects);
                builder.AppendLine();
                builder.AppendLine(new string('-', 48));
                builder.AppendLine();
            }

            return builder.ToString().Trim();
        }

        private static void AppendSection(StringBuilder builder, string title, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            builder.AppendLine(title);
            builder.AppendLine(content.Trim());
            builder.AppendLine();
        }

        private static string SanitizeFileSegment(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = new string((value ?? string.Empty)
                .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
                .ToArray())
                .Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "storyboard" : cleaned;
        }
    }
}
