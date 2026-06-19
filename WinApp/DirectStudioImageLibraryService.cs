using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JSAI.WinApp
{
    public sealed class DirectImageHistoryItem
    {
        public DirectImageHistoryItem(string fileName, string fullPath, DateTime createdAt)
        {
            FileName = fileName;
            FullPath = fullPath;
            CreatedAt = createdAt;
        }

        public string FileName { get; }

        public string FullPath { get; }

        public DateTime CreatedAt { get; }
    }

    public static class DirectStudioImageLibraryService
    {
        public static string GetWorkspaceRoot(string? projectName, string? nodeType = null)
        {
            return ProjectStoragePaths.GetProjectRootPath(
                ProjectWorkspaceMode.DirectStudio,
                projectName,
                nodeType ?? WorkflowNodeCatalog.TextToImage);
        }

        public static string EnsureTodayDirectory(string? projectName, string? nodeType = null)
        {
            return ProjectStoragePaths.EnsureProjectDateDirectory(
                ProjectWorkspaceMode.DirectStudio,
                projectName,
                nodeType ?? WorkflowNodeCatalog.TextToImage);
        }

        public static string ArchiveGeneratedArtifact(string sourcePath, string? projectName, string? nodeType)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return string.Empty;
            }

            var normalizedNodeType = WorkflowNodeCatalog.NormalizeNodeType(nodeType ?? WorkflowNodeCatalog.TextToImage);
            var fullSourcePath = Path.GetFullPath(sourcePath);
            var root = Path.GetFullPath(GetWorkspaceRoot(projectName, normalizedNodeType));
            if (fullSourcePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return fullSourcePath;
            }

            var targetDirectory = EnsureTodayDirectory(projectName, normalizedNodeType);
            var extension = Path.GetExtension(fullSourcePath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = normalizedNodeType == WorkflowNodeCatalog.TextToImage ? ".png" : ".mp4";
            }

            var baseName = DateTime.Now.ToString("yyyyMMddHHmmss");
            var targetPath = Path.Combine(targetDirectory, baseName + extension);
            var sequence = 1;
            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(targetDirectory, $"{baseName}_{sequence:D2}{extension}");
                sequence++;
            }

            File.Copy(fullSourcePath, targetPath, true);
            return targetPath;
        }

        public static string ArchiveGeneratedImage(string sourcePath, string? projectName)
        {
            return ArchiveGeneratedArtifact(sourcePath, projectName, WorkflowNodeCatalog.TextToImage);
        }

        public static IReadOnlyList<DirectImageHistoryItem> LoadHistory(string? projectName)
        {
            var rootPath = GetWorkspaceRoot(projectName, WorkflowNodeCatalog.TextToImage);
            if (!Directory.Exists(rootPath))
            {
                return Array.Empty<DirectImageHistoryItem>();
            }

            var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png",
                ".jpg",
                ".jpeg",
                ".webp",
                ".bmp",
            };

            return Directory
                .EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
                .Where(path => supportedExtensions.Contains(Path.GetExtension(path)))
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Select(info => new DirectImageHistoryItem(info.Name, info.FullName, info.LastWriteTime))
                .ToList();
        }

        public static void DeleteImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            File.Delete(path);
        }
    }
}
