using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace JSAI.WinApp
{
    public sealed class CharacterAssetExportResult
    {
        public CharacterAssetExportResult(string folderPath, List<string> savedFiles)
        {
            FolderPath = folderPath;
            SavedFiles = savedFiles;
        }

        public string FolderPath { get; }

        public List<string> SavedFiles { get; }
    }

    public static class CharacterAssetExportService
    {
        public static string DefaultExportRootPath =>
            ProjectStoragePaths.BaseRootPath;

        public static string GetProjectRootPath(string? projectName)
        {
            return ProjectStoragePaths.GetProjectRootPath(ProjectWorkspaceMode.AiAnimeProject, projectName);
        }

        public static CharacterAssetExportResult Export(CharacterDesignEntry entry, string? rootDirectory = null, WorkflowNode? node = null)
        {
            var roleName = SanitizeFileSegment(string.IsNullOrWhiteSpace(entry.Name)
                ? "\u672a\u547d\u540d\u89d2\u8272"
                : entry.Name);
            var exportRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(rootDirectory)
                ? DefaultExportRootPath
                : rootDirectory);
            var baseDirectory = Path.Combine(exportRoot, "jiaose", roleName);
            Directory.CreateDirectory(baseDirectory);

            var savedFiles = new List<string>();
            CopyIfExists(
                entry.ExpressionSheetPath,
                Path.Combine(baseDirectory, BuildImageFileName("\u4e5d\u5bab\u683c\u8868\u60c5\u677f", entry.ExpressionSheetPath)),
                savedFiles);
            CopyIfExists(
                entry.ThreeViewSheetPath,
                Path.Combine(baseDirectory, BuildImageFileName("\u4e09\u89c6\u56fe", entry.ThreeViewSheetPath)),
                savedFiles);

            SaveTextFile(
                Path.Combine(baseDirectory, "\u89d2\u8272\u8bf4\u660e.txt"),
                BuildDescriptionText(entry),
                savedFiles);
            SaveTextFile(
                Path.Combine(baseDirectory, "\u4e2d\u6587\u63d0\u793a\u8bcd.txt"),
                CharacterPromptTextBuilder.BuildChinesePromptBundle(node, entry),
                savedFiles);
            SaveTextFile(
                Path.Combine(baseDirectory, "\u82f1\u6587\u63d0\u793a\u8bcd.txt"),
                BuildPromptText(entry),
                savedFiles);

            return new CharacterAssetExportResult(baseDirectory, savedFiles);
        }

        public static string BuildEnglishPromptBundle(CharacterDesignEntry entry)
        {
            return BuildPromptText(entry);
        }

        private static string BuildImageFileName(string stem, string sourcePath)
        {
            var extension = Path.GetExtension(sourcePath);
            return string.IsNullOrWhiteSpace(extension) ? $"{stem}.png" : $"{stem}{extension}";
        }

        private static void CopyIfExists(string sourcePath, string targetPath, ICollection<string> savedFiles)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(sourcePath, targetPath, true);
            savedFiles.Add(targetPath);
        }

        private static void SaveTextFile(string filePath, string content, ICollection<string> savedFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, (content ?? string.Empty).Trim(), Encoding.UTF8);
            savedFiles.Add(filePath);
        }

        private static string BuildDescriptionText(CharacterDesignEntry entry)
        {
            var builder = new StringBuilder();
            AppendSection(builder, "\u89d2\u8272\u540d", entry.Name);
            AppendSection(builder, "\u522b\u540d", entry.Alias);
            AppendSection(builder, "\u89d2\u8272\u7c7b\u578b", entry.RoleType);
            AppendSection(builder, "\u89d2\u8272\u6458\u8981", entry.Summary);
            AppendSection(builder, "\u57fa\u7840\u5916\u5f62", entry.BasicStats);
            AppendSection(builder, "\u804c\u4e1a\u8eab\u4efd", entry.Profession);
            AppendSection(builder, "\u6210\u957f\u80cc\u666f", entry.Background);
            AppendSection(builder, "\u6027\u683c\u7279\u5f81", entry.Personality);
            AppendSection(builder, "\u6838\u5fc3\u52a8\u673a", entry.Motivation);
            AppendSection(builder, "\u4ef7\u503c\u89c2", entry.Values);
            AppendSection(builder, "\u5f31\u70b9\u4e0e\u6050\u60e7", entry.Weakness);
            AppendSection(builder, "\u6838\u5fc3\u5173\u7cfb", entry.Relationships);
            AppendSection(builder, "\u4e60\u60ef\u4e0e\u5174\u8da3", entry.Habits);
            AppendSection(builder, "\u89c6\u89c9\u6807\u7b7e", entry.VisualTags);
            AppendSection(builder, "\u670d\u88c5\u8bf4\u660e", entry.CostumeNotes);
            AppendSection(builder, "\u8868\u6f14\u8bf4\u660e", entry.ActingNotes);
            return builder.ToString().Trim();
        }

        private static string BuildPromptText(CharacterDesignEntry entry)
        {
            var builder = new StringBuilder();
            AppendSection(builder, "\u89d2\u8272\u5916\u89c2\u63d0\u793a\u8bcd", entry.AppearancePrompt);
            AppendSection(builder, "\u4e5d\u5bab\u683c\u63d0\u793a\u8bcd", entry.ExpressionPrompt);
            AppendSection(builder, "\u4e09\u89c6\u56fe\u63d0\u793a\u8bcd", entry.ThreeViewPrompt);
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
            return string.IsNullOrWhiteSpace(cleaned) ? "untitled" : cleaned;
        }
    }
}
