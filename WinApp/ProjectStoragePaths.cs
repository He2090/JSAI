using System;
using System.IO;
using System.Linq;

namespace JSAI.WinApp
{
    public static class ProjectStoragePaths
    {
        public static string BaseRootPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XMwenjian");

        public static string GetProjectRootPath(
            ProjectWorkspaceMode mode,
            string? projectName,
            string? directNodeType = null)
        {
            var modeDirectory = GetModeDirectoryName(mode, directNodeType);
            var safeProjectName = SanitizeFileSegment(string.IsNullOrWhiteSpace(projectName)
                ? "新项目"
                : projectName);

            return Path.Combine(BaseRootPath, modeDirectory, safeProjectName);
        }

        public static string EnsureProjectRootPath(
            ProjectWorkspaceMode mode,
            string? projectName,
            string? directNodeType = null)
        {
            var path = GetProjectRootPath(mode, projectName, directNodeType);
            Directory.CreateDirectory(path);
            return path;
        }

        public static string EnsureProjectDateDirectory(
            ProjectWorkspaceMode mode,
            string? projectName,
            string? directNodeType = null)
        {
            var projectRoot = EnsureProjectRootPath(mode, projectName, directNodeType);
            var dayDirectory = Path.Combine(projectRoot, DateTime.Now.ToString("yyyyMMdd"));
            Directory.CreateDirectory(dayDirectory);
            return dayDirectory;
        }

        public static string GetModeDirectoryName(ProjectWorkspaceMode mode, string? directNodeType = null)
        {
            if (mode != ProjectWorkspaceMode.DirectStudio)
            {
                return "ai漫剧";
            }

            return WorkflowNodeCatalog.NormalizeNodeType(directNodeType ?? string.Empty) switch
            {
                var type when string.Equals(type, WorkflowNodeCatalog.TextToImage, StringComparison.Ordinal) => "文生图",
                var type when string.Equals(type, WorkflowNodeCatalog.ImageToImage, StringComparison.Ordinal) => "图生图",
                var type when string.Equals(type, WorkflowNodeCatalog.TextToVideo, StringComparison.Ordinal) => "文生视频",
                var type when string.Equals(type, WorkflowNodeCatalog.TextImageToVideo, StringComparison.Ordinal) => "文图生视频",
                _ => "直出项目",
            };
        }

        public static string SanitizeFileSegment(string? value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = new string((value ?? string.Empty)
                .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
                .ToArray())
                .Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "untitled" : cleaned;
        }
    }
}
