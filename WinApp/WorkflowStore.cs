using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace JSAI.WinApp
{
    public static class WorkflowStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        private static string DataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JSAI");

        private static string LastWorkflowPath => Path.Combine(DataDirectory, "last-workflow.json");
        private static string ProjectCacheDirectory => Path.Combine(DataDirectory, "project-cache");
        public const string ProjectFileExtension = ".myai";

        public static WorkflowDocument? LoadLastWorkflow()
        {
            return LoadFromFile(LastWorkflowPath, throwOnFailure: false);
        }

        public static void SaveLastWorkflow(WorkflowDocument document)
        {
            SaveToFile(document, LastWorkflowPath);
        }

        public static WorkflowDocument? LoadProjectFile(string path, bool throwOnFailure = true)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                Directory.CreateDirectory(ProjectCacheDirectory);
                var extractRoot = Path.Combine(
                    ProjectCacheDirectory,
                    $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}");

                if (Directory.Exists(extractRoot))
                {
                    Directory.Delete(extractRoot, recursive: true);
                }

                Directory.CreateDirectory(extractRoot);
                ZipFile.ExtractToDirectory(path, extractRoot, overwriteFiles: true);

                var workflowPath = Path.Combine(extractRoot, "workflow.json");
                if (!File.Exists(workflowPath))
                {
                    workflowPath = Directory
                        .EnumerateFiles(extractRoot, "workflow.json", SearchOption.AllDirectories)
                        .FirstOrDefault() ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(workflowPath) || !File.Exists(workflowPath))
                {
                    throw new InvalidDataException("项目文件中未找到 workflow.json。");
                }

                return LoadFromFile(workflowPath, throwOnFailure: true);
            }
            catch when (!throwOnFailure)
            {
                return null;
            }
        }

        public static ProjectArchiveResult SaveProjectFile(WorkflowDocument document, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Project file path cannot be empty.", nameof(path));
            }

            var fullPath = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(fullPath), ProjectFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                fullPath += ProjectFileExtension;
            }

            var tempRoot = Path.Combine(Path.GetTempPath(), $"JSAI_Project_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            try
            {
                var packageRoot = Path.Combine(tempRoot, BuildProjectDirectoryName(document.ProjectName));
                var packageResult = SaveProjectPackage(document, packageRoot);

                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }

                ZipFile.CreateFromDirectory(
                    packageRoot,
                    fullPath,
                    CompressionLevel.Fastest,
                    includeBaseDirectory: false,
                    entryNameEncoding: Encoding.UTF8);

                return new ProjectArchiveResult(
                    fullPath,
                    packageResult.RootDirectory,
                    packageResult.WorkflowPath,
                    packageResult.OutputFileCount,
                    packageResult.CopiedFileCount);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
        }

        public static WorkflowDocument? LoadFromFile(string path, bool throwOnFailure = true)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var json = File.ReadAllText(path, Encoding.UTF8);
                var document = JsonSerializer.Deserialize<WorkflowDocument>(json, JsonOptions);
                if (document == null)
                {
                    return null;
                }

                ResolveRelativePaths(document, Path.GetDirectoryName(path));
                return Normalize(document);
            }
            catch when (!throwOnFailure)
            {
                return null;
            }
        }

        public static void SaveToFile(WorkflowDocument document, string path)
        {
            var normalized = Normalize(document);
            normalized.ExportedAt = DateTime.UtcNow;

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(normalized, JsonOptions);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        public static ProjectPackageResult SaveProjectPackage(WorkflowDocument document, string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("Project root directory cannot be empty.", nameof(rootDirectory));
            }

            var projectRoot = Path.GetFullPath(rootDirectory);
            var workingDocument = Normalize(CloneDocument(document));
            workingDocument.ExportedAt = DateTime.UtcNow;

            Directory.CreateDirectory(projectRoot);

            var nodesDirectory = ResetDirectory(projectRoot, "nodes");
            var assetsDirectory = ResetDirectory(projectRoot, "assets");
            var artifactsDirectory = ResetDirectory(projectRoot, "artifacts");
            DeleteIfExists(Path.Combine(projectRoot, "workflow.json"));
            DeleteIfExists(Path.Combine(projectRoot, "project-manifest.json"));

            var copiedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var nodeOutputs = new List<ProjectNodeOutputRecord>();
            var copiedArtifacts = new List<ProjectCopiedFileRecord>();

            foreach (var asset in workingDocument.Assets)
            {
                if (!TryCopyProjectFile(
                        asset.FilePath,
                        assetsDirectory,
                        projectRoot,
                        BuildAssetFileName(asset),
                        copiedFiles,
                        out var relativePath))
                {
                    continue;
                }

                asset.FilePath = relativePath;
                copiedArtifacts.Add(new ProjectCopiedFileRecord
                {
                    OwnerId = asset.Id,
                    OwnerType = "asset",
                    Kind = asset.Kind,
                    RelativePath = relativePath,
                });
            }

            foreach (var node in workingDocument.Nodes)
            {
                if (!string.IsNullOrWhiteSpace(node.Output))
                {
                    var outputPath = SaveNodeOutputFile(nodesDirectory, projectRoot, node);
                    nodeOutputs.Add(new ProjectNodeOutputRecord
                    {
                        NodeId = node.Id,
                        NodeType = node.Type,
                        RelativePath = outputPath,
                    });
                }

                if (TryCopyProjectFile(
                        node.ArtifactPath,
                        Path.Combine(artifactsDirectory, SanitizeFileName(node.ArtifactKind, "other")),
                        projectRoot,
                        BuildArtifactFileName(node),
                        copiedFiles,
                        out var artifactRelativePath))
                {
                    node.ArtifactPath = artifactRelativePath;
                    copiedArtifacts.Add(new ProjectCopiedFileRecord
                    {
                        OwnerId = node.Id,
                        OwnerType = "node",
                        Kind = string.IsNullOrWhiteSpace(node.ArtifactKind) ? "file" : node.ArtifactKind,
                        RelativePath = artifactRelativePath,
                    });
                }

                if (TryCopyProjectFile(
                        node.Params?.DirectReferenceImagePath,
                        Path.Combine(artifactsDirectory, "direct_reference"),
                        projectRoot,
                        BuildDirectReferenceFileName(node),
                        copiedFiles,
                        out var directReferenceRelativePath))
                {
                    node.Params!.DirectReferenceImagePath = directReferenceRelativePath;
                    copiedArtifacts.Add(new ProjectCopiedFileRecord
                    {
                        OwnerId = node.Id,
                        OwnerType = "direct_reference",
                        Kind = "image",
                        RelativePath = directReferenceRelativePath,
                    });
                }

                foreach (var entry in node.Params?.CharacterEntries ?? Enumerable.Empty<CharacterDesignEntry>())
                {
                    if (TryCopyProjectFile(
                            entry.ReferencePortraitPath,
                            Path.Combine(artifactsDirectory, "character_reference"),
                            projectRoot,
                            BuildCharacterArtifactFileName(node, entry, "reference"),
                            copiedFiles,
                            out var referenceRelativePath))
                    {
                        entry.ReferencePortraitPath = referenceRelativePath;
                        copiedArtifacts.Add(new ProjectCopiedFileRecord
                        {
                            OwnerId = $"{node.Id}:{entry.Name}",
                            OwnerType = "character_reference",
                            Kind = "image",
                            RelativePath = referenceRelativePath,
                        });
                    }

                    if (TryCopyProjectFile(
                            entry.ExpressionSheetPath,
                            Path.Combine(artifactsDirectory, "character_expression"),
                            projectRoot,
                            BuildCharacterArtifactFileName(node, entry, "expression"),
                            copiedFiles,
                            out var expressionRelativePath))
                    {
                        entry.ExpressionSheetPath = expressionRelativePath;
                        copiedArtifacts.Add(new ProjectCopiedFileRecord
                        {
                            OwnerId = $"{node.Id}:{entry.Name}",
                            OwnerType = "character_expression",
                            Kind = "image",
                            RelativePath = expressionRelativePath,
                        });
                    }

                    if (TryCopyProjectFile(
                            entry.ThreeViewSheetPath,
                            Path.Combine(artifactsDirectory, "character_threeview"),
                            projectRoot,
                            BuildCharacterArtifactFileName(node, entry, "threeview"),
                            copiedFiles,
                            out var threeViewRelativePath))
                    {
                        entry.ThreeViewSheetPath = threeViewRelativePath;
                        copiedArtifacts.Add(new ProjectCopiedFileRecord
                        {
                            OwnerId = $"{node.Id}:{entry.Name}",
                            OwnerType = "character_threeview",
                            Kind = "image",
                            RelativePath = threeViewRelativePath,
                        });
                    }
                }

                if (node.Params?.StoryboardGridPagePaths != null && node.Params.StoryboardGridPagePaths.Count > 0)
                {
                    for (var index = 0; index < node.Params.StoryboardGridPagePaths.Count; index++)
                    {
                        var pagePath = node.Params.StoryboardGridPagePaths[index];
                        if (!TryCopyProjectFile(
                                pagePath,
                                Path.Combine(artifactsDirectory, "storyboard_pages"),
                                projectRoot,
                                $"{SanitizeFileName(node.Id, "storyboard")}_page_{index + 1}{Path.GetExtension(pagePath).OrDefault(".png")}",
                                copiedFiles,
                                out var copiedPageRelativePath))
                        {
                            continue;
                        }

                        node.Params.StoryboardGridPagePaths[index] = copiedPageRelativePath;
                        copiedArtifacts.Add(new ProjectCopiedFileRecord
                        {
                            OwnerId = $"{node.Id}:storyboard:{index + 1}",
                            OwnerType = "storyboard_page",
                            Kind = "image",
                            RelativePath = copiedPageRelativePath,
                        });
                    }

                    if (node.Params.StoryboardGridPagePaths.Count > 0)
                    {
                        node.ArtifactPath = node.Params.StoryboardGridPagePaths[
                            Math.Clamp(node.Params.StoryboardCurrentPage, 0, node.Params.StoryboardGridPagePaths.Count - 1)];
                    }
                }

                if (node.Params?.StoryboardShots != null && node.Params.StoryboardShots.Count > 0)
                {
                    for (var index = 0; index < node.Params.StoryboardShots.Count; index++)
                    {
                        var shot = node.Params.StoryboardShots[index];
                        if (!TryCopyProjectFile(
                                shot.SplitImagePath,
                                Path.Combine(artifactsDirectory, "storyboard_shots"),
                                projectRoot,
                                BuildStoryboardShotFileName(node, shot, index),
                                copiedFiles,
                                out var splitImageRelativePath))
                        {
                            continue;
                        }

                        shot.SplitImagePath = splitImageRelativePath;
                        copiedArtifacts.Add(new ProjectCopiedFileRecord
                        {
                            OwnerId = $"{node.Id}:shot:{shot.Id}",
                            OwnerType = "storyboard_shot",
                            Kind = "image",
                            RelativePath = splitImageRelativePath,
                        });
                    }
                }

                if (TryCopyProjectFile(
                        node.Params?.StoryboardVideoFusedImagePath,
                        Path.Combine(artifactsDirectory, "storyboard_video_reference"),
                        projectRoot,
                        BuildStoryboardVideoReferenceFileName(node),
                        copiedFiles,
                        out var fusedReferenceRelativePath))
                {
                    node.Params!.StoryboardVideoFusedImagePath = fusedReferenceRelativePath;
                    copiedArtifacts.Add(new ProjectCopiedFileRecord
                    {
                        OwnerId = node.Id,
                        OwnerType = "storyboard_video_reference",
                        Kind = "image",
                        RelativePath = fusedReferenceRelativePath,
                    });
                }

                if (node.Params?.VideoCollectionSelectedArtifactPaths != null &&
                    node.Params.VideoCollectionSelectedArtifactPaths.Count > 0)
                {
                    for (var index = 0; index < node.Params.VideoCollectionSelectedArtifactPaths.Count; index++)
                    {
                        var sourcePath = node.Params.VideoCollectionSelectedArtifactPaths[index];
                        if (!TryCopyProjectFile(
                                sourcePath,
                                Path.Combine(artifactsDirectory, "video_collection"),
                                projectRoot,
                                BuildVideoCollectionFileName(node, sourcePath, $"selected_{index + 1}"),
                                copiedFiles,
                                out var selectedVideoRelativePath))
                        {
                            continue;
                        }

                        node.Params.VideoCollectionSelectedArtifactPaths[index] = selectedVideoRelativePath;
                        copiedArtifacts.Add(new ProjectCopiedFileRecord
                        {
                            OwnerId = $"{node.Id}:video:selected:{index + 1}",
                            OwnerType = "video_collection_selected",
                            Kind = "video",
                            RelativePath = selectedVideoRelativePath,
                        });
                    }
                }

                if (node.Params?.VideoCollectionImportedAssets != null &&
                    node.Params.VideoCollectionImportedAssets.Count > 0)
                {
                    for (var index = 0; index < node.Params.VideoCollectionImportedAssets.Count; index++)
                    {
                        var importedAsset = node.Params.VideoCollectionImportedAssets[index];
                        if (!TryCopyProjectFile(
                                importedAsset.FilePath,
                                Path.Combine(artifactsDirectory, "video_collection"),
                                projectRoot,
                                BuildVideoCollectionFileName(node, importedAsset.FilePath, $"imported_{index + 1}"),
                                copiedFiles,
                                out var importedRelativePath))
                        {
                            continue;
                        }

                        importedAsset.FilePath = importedRelativePath;
                        copiedArtifacts.Add(new ProjectCopiedFileRecord
                        {
                            OwnerId = $"{node.Id}:video:imported:{index + 1}",
                            OwnerType = "video_collection_imported",
                            Kind = importedAsset.Kind,
                            RelativePath = importedRelativePath,
                        });
                    }
                }

                CopyVideoCollectionPath(
                    node,
                    nameof(node.Params.VideoCollectionCurrentArtifactPath),
                    node.Params?.VideoCollectionCurrentArtifactPath,
                    artifactsDirectory,
                    projectRoot,
                    copiedFiles,
                    copiedArtifacts,
                    relativePath => node.Params!.VideoCollectionCurrentArtifactPath = relativePath);
                CopyVideoCollectionPath(
                    node,
                    nameof(node.Params.VideoCollectionPlaylistPath),
                    node.Params?.VideoCollectionPlaylistPath,
                    artifactsDirectory,
                    projectRoot,
                    copiedFiles,
                    copiedArtifacts,
                    relativePath => node.Params!.VideoCollectionPlaylistPath = relativePath);
                CopyVideoCollectionPath(
                    node,
                    nameof(node.Params.VideoCollectionEditProjectPath),
                    node.Params?.VideoCollectionEditProjectPath,
                    artifactsDirectory,
                    projectRoot,
                    copiedFiles,
                    copiedArtifacts,
                    relativePath => node.Params!.VideoCollectionEditProjectPath = relativePath);
                CopyVideoCollectionPath(
                    node,
                    nameof(node.Params.VideoCollectionAudioPath),
                    node.Params?.VideoCollectionAudioPath,
                    artifactsDirectory,
                    projectRoot,
                    copiedFiles,
                    copiedArtifacts,
                    relativePath => node.Params!.VideoCollectionAudioPath = relativePath);

                if (node.Params?.VideoCollectionTimelineClips != null)
                {
                    for (var index = 0; index < node.Params.VideoCollectionTimelineClips.Count; index++)
                    {
                        var clip = node.Params.VideoCollectionTimelineClips[index];
                        if (!TryCopyProjectFile(
                                clip.ArtifactPath,
                                Path.Combine(artifactsDirectory, "video_collection"),
                                projectRoot,
                                BuildVideoCollectionFileName(node, clip.ArtifactPath, $"timeline_{index + 1}"),
                                copiedFiles,
                                out var timelineVideoRelativePath))
                        {
                            continue;
                        }

                        clip.ArtifactPath = timelineVideoRelativePath;
                        copiedArtifacts.Add(new ProjectCopiedFileRecord
                        {
                            OwnerId = $"{node.Id}:video:timeline:{index + 1}",
                            OwnerType = "video_collection_timeline",
                            Kind = "video",
                            RelativePath = timelineVideoRelativePath,
                        });
                    }
                }

                if (node.Params?.VideoCollectionOverlayItems != null)
                {
                    for (var index = 0; index < node.Params.VideoCollectionOverlayItems.Count; index++)
                    {
                        var overlay = node.Params.VideoCollectionOverlayItems[index];
                        if (!string.Equals(overlay.Kind, "image", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!TryCopyProjectFile(
                                overlay.ImagePath,
                                Path.Combine(artifactsDirectory, "video_collection"),
                                projectRoot,
                                BuildVideoCollectionFileName(node, overlay.ImagePath, $"overlay_{index + 1}"),
                                copiedFiles,
                                out var overlayRelativePath))
                        {
                            continue;
                        }

                        overlay.ImagePath = overlayRelativePath;
                        copiedArtifacts.Add(new ProjectCopiedFileRecord
                        {
                            OwnerId = $"{node.Id}:video:overlay:{index + 1}",
                            OwnerType = "video_collection_overlay",
                            Kind = "image",
                            RelativePath = overlayRelativePath,
                        });
                    }
                }
            }

            var workflowPath = Path.Combine(projectRoot, "workflow.json");
            var manifestPath = Path.Combine(projectRoot, "project-manifest.json");

            SaveToFile(workingDocument, workflowPath);

            var manifest = new ProjectManifest
            {
                ProjectName = workingDocument.ProjectName,
                SavedAt = workingDocument.ExportedAt,
                WorkflowFile = Path.GetFileName(workflowPath),
                NodeOutputs = nodeOutputs,
                CopiedFiles = copiedArtifacts,
            };

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            File.WriteAllText(manifestPath, manifestJson, Encoding.UTF8);

            return new ProjectPackageResult(projectRoot, workflowPath, nodeOutputs.Count, copiedArtifacts.Count);
        }

        public static string BuildProjectDirectoryName(string projectName)
        {
            return SanitizeFileName(projectName, "JSAI_Project");
        }

        private static WorkflowDocument Normalize(WorkflowDocument document)
        {
            document.Version = document.Version <= 0 ? 1 : document.Version;
            document.ExportedAt = document.ExportedAt == default ? DateTime.UtcNow : document.ExportedAt;
            document.ProjectName = string.IsNullOrWhiteSpace(document.ProjectName) ? "新项目" : document.ProjectName.Trim();
            if (!Enum.IsDefined(typeof(ProjectWorkspaceMode), document.ProjectMode))
            {
                document.ProjectMode = ProjectWorkspaceMode.AiAnimeProject;
            }
            document.Nodes ??= new();
            document.Edges ??= new();
            document.Assets ??= new();

            foreach (var node in document.Nodes)
            {
                var originalType = node.Type;
                node.Id = string.IsNullOrWhiteSpace(node.Id) ? $"node_{Guid.NewGuid():N}" : node.Id;
                node.Type = WorkflowNodeCatalog.NormalizeNodeType(node.Type);
                node.Params ??= new WorkflowNodeParameters();
                node.Params.EnsureDefaults(node.Type);
                node.Output ??= string.Empty;
                node.ArtifactPath ??= string.Empty;
                node.ArtifactKind ??= string.Empty;

                if ((string.Equals(originalType, WorkflowNodeCatalog.CharacterDescription, StringComparison.Ordinal) ||
                     node.Type == WorkflowNodeCatalog.CharacterView) &&
                    (node.Params.CharacterEntries == null || node.Params.CharacterEntries.Count == 0) &&
                    !string.IsNullOrWhiteSpace(node.Output))
                {
                    var migratedEntries = WorkflowExecutor.ParseCharacterDescriptionEntries(node.Output);
                    if (migratedEntries.Count > 0)
                    {
                        node.Params.CharacterEntries = migratedEntries;
                        node.Params.SelectedCharacterName = migratedEntries[0].Name;
                    }
                }

                if (node.Type == WorkflowNodeCatalog.StoryboardBreakdown &&
                    (node.Params.StoryboardShots == null || node.Params.StoryboardShots.Count == 0) &&
                    !string.IsNullOrWhiteSpace(node.Output))
                {
                    node.Params.StoryboardShots = WorkflowExecutor.ParseStoryboardShots(node.Output);
                }
            }

            document.Edges = document.Edges
                .Where(edge => !string.IsNullOrWhiteSpace(edge.From) && !string.IsNullOrWhiteSpace(edge.To))
                .ToList();

            foreach (var asset in document.Assets)
            {
                asset.Id = string.IsNullOrWhiteSpace(asset.Id) ? Guid.NewGuid().ToString("N") : asset.Id;
                asset.Name = string.IsNullOrWhiteSpace(asset.Name) ? "未命名资产" : asset.Name;
                asset.Mime = string.IsNullOrWhiteSpace(asset.Mime) ? "application/octet-stream" : asset.Mime;
                asset.Kind = string.IsNullOrWhiteSpace(asset.Kind) ? "other" : asset.Kind;
                asset.FilePath ??= string.Empty;
            }

            return document;
        }

        private static WorkflowDocument CloneDocument(WorkflowDocument document)
        {
            var json = JsonSerializer.Serialize(document, JsonOptions);
            return JsonSerializer.Deserialize<WorkflowDocument>(json, JsonOptions) ?? WorkflowDocument.CreateEmpty(document.ProjectName, document.ProjectMode);
        }

        private static void ResolveRelativePaths(WorkflowDocument document, string? baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return;
            }

            foreach (var node in document.Nodes ?? Enumerable.Empty<WorkflowNode>())
            {
                node.ArtifactPath = ResolvePath(baseDirectory, node.ArtifactPath);
                if (node.Params != null)
                {
                    node.Params.DirectReferenceImagePath = ResolvePath(baseDirectory, node.Params.DirectReferenceImagePath);
                    node.Params.StoryboardVideoFusedImagePath = ResolvePath(baseDirectory, node.Params.StoryboardVideoFusedImagePath);

                    foreach (var shot in node.Params.StoryboardShots ?? Enumerable.Empty<StoryboardShot>())
                    {
                        shot.SplitImagePath = ResolvePath(baseDirectory, shot.SplitImagePath);
                    }

                    if (node.Params.VideoCollectionSelectedArtifactPaths != null)
                    {
                        for (var index = 0; index < node.Params.VideoCollectionSelectedArtifactPaths.Count; index++)
                        {
                            node.Params.VideoCollectionSelectedArtifactPaths[index] =
                                ResolvePath(baseDirectory, node.Params.VideoCollectionSelectedArtifactPaths[index]);
                        }
                    }

                    node.Params.VideoCollectionCurrentArtifactPath = ResolvePath(baseDirectory, node.Params.VideoCollectionCurrentArtifactPath);
                    node.Params.VideoCollectionPlaylistPath = ResolvePath(baseDirectory, node.Params.VideoCollectionPlaylistPath);
                    node.Params.VideoCollectionEditProjectPath = ResolvePath(baseDirectory, node.Params.VideoCollectionEditProjectPath);
                    node.Params.VideoCollectionAudioPath = ResolvePath(baseDirectory, node.Params.VideoCollectionAudioPath);

                    foreach (var clip in node.Params.VideoCollectionTimelineClips ?? Enumerable.Empty<VideoCollectionTimelineClip>())
                    {
                        clip.ArtifactPath = ResolvePath(baseDirectory, clip.ArtifactPath);
                    }

                    foreach (var importedAsset in node.Params.VideoCollectionImportedAssets ?? Enumerable.Empty<VideoCollectionImportedAsset>())
                    {
                        importedAsset.FilePath = ResolvePath(baseDirectory, importedAsset.FilePath);
                    }

                    foreach (var overlay in node.Params.VideoCollectionOverlayItems ?? Enumerable.Empty<VideoCollectionOverlayItem>())
                    {
                        overlay.ImagePath = ResolvePath(baseDirectory, overlay.ImagePath);
                    }
                }

                foreach (var entry in node.Params?.CharacterEntries ?? Enumerable.Empty<CharacterDesignEntry>())
                {
                    entry.ReferencePortraitPath = ResolvePath(baseDirectory, entry.ReferencePortraitPath);
                    entry.ExpressionSheetPath = ResolvePath(baseDirectory, entry.ExpressionSheetPath);
                    entry.ThreeViewSheetPath = ResolvePath(baseDirectory, entry.ThreeViewSheetPath);
                }

                if (node.Params?.StoryboardGridPagePaths != null)
                {
                    for (var index = 0; index < node.Params.StoryboardGridPagePaths.Count; index++)
                    {
                        node.Params.StoryboardGridPagePaths[index] = ResolvePath(baseDirectory, node.Params.StoryboardGridPagePaths[index]);
                    }
                }
            }

            foreach (var asset in document.Assets ?? Enumerable.Empty<WorkflowAsset>())
            {
                asset.FilePath = ResolvePath(baseDirectory, asset.FilePath);
            }
        }

        private static string ResolvePath(string baseDirectory, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            {
                return path ?? string.Empty;
            }

            return Path.GetFullPath(Path.Combine(baseDirectory, path));
        }

        private static string ResetDirectory(string rootDirectory, string relativeDirectory)
        {
            var directory = Path.Combine(rootDirectory, relativeDirectory);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static bool TryCopyProjectFile(
            string? sourcePath,
            string targetDirectory,
            string projectRoot,
            string preferredFileName,
            IDictionary<string, string> copiedFiles,
            out string relativePath)
        {
            relativePath = string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return false;
            }

            var fullSourcePath = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullSourcePath))
            {
                return false;
            }

            if (copiedFiles.TryGetValue(fullSourcePath, out var existingRelativePath))
            {
                relativePath = existingRelativePath;
                return true;
            }

            Directory.CreateDirectory(targetDirectory);

            var extension = Path.GetExtension(fullSourcePath);
            var fileName = SanitizeFileName(preferredFileName, Path.GetFileName(fullSourcePath));
            if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
            {
                fileName += extension;
            }

            var destinationPath = GetUniqueFilePath(targetDirectory, fileName);
            File.Copy(fullSourcePath, destinationPath, overwrite: false);

            relativePath = Path.GetRelativePath(projectRoot, destinationPath);
            copiedFiles[fullSourcePath] = relativePath;
            return true;
        }

        private static string SaveNodeOutputFile(string nodesDirectory, string projectRoot, WorkflowNode node)
        {
            var extension = DetermineNodeOutputExtension(node);
            var fileName = $"{SanitizeFileName(node.Id, "node")}_{SanitizeFileName(node.Type, "output")}{extension}";
            var outputPath = GetUniqueFilePath(nodesDirectory, fileName);
            File.WriteAllText(outputPath, node.Output ?? string.Empty, Encoding.UTF8);
            return Path.GetRelativePath(projectRoot, outputPath);
        }

        private static string DetermineNodeOutputExtension(WorkflowNode node)
        {
            if (LooksLikeJson(node.Output))
            {
                return ".json";
            }

            return node.Type switch
            {
                var type when type == WorkflowNodeCatalog.Outline => ".md",
                var type when type == WorkflowNodeCatalog.CreativeDescription => ".md",
                var type when type == WorkflowNodeCatalog.VideoCollection => ".md",
                var type when type == WorkflowNodeCatalog.StoryboardBreakdown => ".json",
                var type when type == WorkflowNodeCatalog.StoryboardVideo => ".json",
                _ => ".txt",
            };
        }

        private static bool LooksLikeJson(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            return (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal)) ||
                   (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal));
        }

        private static string BuildAssetFileName(WorkflowAsset asset)
        {
            var name = Path.GetFileNameWithoutExtension(asset.Name);
            var extension = Path.GetExtension(asset.FilePath);
            var fileName = $"{SanitizeFileName(asset.Name, asset.Id)}";
            return string.IsNullOrWhiteSpace(Path.GetExtension(fileName)) && !string.IsNullOrWhiteSpace(extension)
                ? $"{SanitizeFileName(name, asset.Id)}{extension}"
                : fileName;
        }

        private static string BuildArtifactFileName(WorkflowNode node)
        {
            var extension = Path.GetExtension(node.ArtifactPath);
            var baseName = $"{SanitizeFileName(node.Id, "node")}_{SanitizeFileName(node.Type, "artifact")}";
            return string.IsNullOrWhiteSpace(extension) ? baseName : $"{baseName}{extension}";
        }

        private static string BuildCharacterArtifactFileName(WorkflowNode node, CharacterDesignEntry entry, string suffix)
        {
            var sourcePath = suffix switch
            {
                "reference" => entry.ReferencePortraitPath,
                "expression" => entry.ExpressionSheetPath,
                _ => entry.ThreeViewSheetPath,
            };
            var extension = Path.GetExtension(sourcePath);
            var baseName =
                $"{SanitizeFileName(node.Id, "node")}_{SanitizeFileName(entry.Name, "character")}_{SanitizeFileName(suffix, "artifact")}";
            return string.IsNullOrWhiteSpace(extension) ? baseName : $"{baseName}{extension}";
        }

        private static string BuildDirectReferenceFileName(WorkflowNode node)
        {
            var extension = Path.GetExtension(node.Params?.DirectReferenceImagePath);
            var baseName = $"{SanitizeFileName(node.Id, "node")}_direct_reference";
            return string.IsNullOrWhiteSpace(extension) ? $"{baseName}.png" : $"{baseName}{extension}";
        }

        private static string BuildStoryboardShotFileName(WorkflowNode node, StoryboardShot shot, int index)
        {
            var extension = Path.GetExtension(shot.SplitImagePath);
            var shotNumber = Math.Max(1, shot.ShotNumber > 0 ? shot.ShotNumber : index + 1);
            var baseName = $"{SanitizeFileName(node.Id, "node")}_shot_{shotNumber}_{SanitizeFileName(shot.Id, "shot")}";
            return string.IsNullOrWhiteSpace(extension) ? $"{baseName}.png" : $"{baseName}{extension}";
        }

        private static string BuildStoryboardVideoReferenceFileName(WorkflowNode node)
        {
            var extension = Path.GetExtension(node.Params?.StoryboardVideoFusedImagePath);
            var baseName = $"{SanitizeFileName(node.Id, "node")}_storyboard_video_reference";
            return string.IsNullOrWhiteSpace(extension) ? $"{baseName}.png" : $"{baseName}{extension}";
        }

        private static string BuildVideoCollectionFileName(WorkflowNode node, string? sourcePath, string suffix)
        {
            var extension = Path.GetExtension(sourcePath);
            var baseName = $"{SanitizeFileName(node.Id, "node")}_{SanitizeFileName(suffix, "video")}";
            return string.IsNullOrWhiteSpace(extension) ? baseName : $"{baseName}{extension}";
        }

        private static void CopyVideoCollectionPath(
            WorkflowNode node,
            string fieldName,
            string? sourcePath,
            string artifactsDirectory,
            string projectRoot,
            IDictionary<string, string> copiedFiles,
            ICollection<ProjectCopiedFileRecord> copiedArtifacts,
            Action<string> assignRelativePath)
        {
            if (!TryCopyProjectFile(
                    sourcePath,
                    Path.Combine(artifactsDirectory, "video_collection"),
                    projectRoot,
                    BuildVideoCollectionFileName(node, sourcePath, fieldName),
                    copiedFiles,
                    out var relativePath))
            {
                return;
            }

            assignRelativePath(relativePath);
            copiedArtifacts.Add(new ProjectCopiedFileRecord
            {
                OwnerId = $"{node.Id}:video:{fieldName}",
                OwnerType = "video_collection",
                Kind = "file",
                RelativePath = relativePath,
            });
        }

        private static string GetUniqueFilePath(string directory, string fileName)
        {
            var candidate = Path.Combine(directory, fileName);
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            var stem = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var index = 2;
            do
            {
                candidate = Path.Combine(directory, $"{stem}_{index}{extension}");
                index++;
            }
            while (File.Exists(candidate));

            return candidate;
        }

        private static string SanitizeFileName(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(invalidChars.Contains(character) ? '_' : character);
            }

            var result = builder.ToString().Trim();
            result = result.Trim('.', ' ');
            return string.IsNullOrWhiteSpace(result) ? fallback : result;
        }

        public sealed record ProjectPackageResult(string RootDirectory, string WorkflowPath, int OutputFileCount, int CopiedFileCount);
        public sealed record ProjectArchiveResult(string FilePath, string PackageRoot, string WorkflowPath, int OutputFileCount, int CopiedFileCount);

        private sealed class ProjectManifest
        {
            public string ProjectName { get; set; } = string.Empty;

            public DateTime SavedAt { get; set; }

            public string WorkflowFile { get; set; } = "workflow.json";

            public List<ProjectNodeOutputRecord> NodeOutputs { get; set; } = new();

            public List<ProjectCopiedFileRecord> CopiedFiles { get; set; } = new();
        }

        private sealed class ProjectNodeOutputRecord
        {
            public string NodeId { get; set; } = string.Empty;

            public string NodeType { get; set; } = string.Empty;

            public string RelativePath { get; set; } = string.Empty;
        }

        private sealed class ProjectCopiedFileRecord
        {
            public string OwnerId { get; set; } = string.Empty;

            public string OwnerType { get; set; } = string.Empty;

            public string Kind { get; set; } = string.Empty;

            public string RelativePath { get; set; } = string.Empty;
        }
    }
}
