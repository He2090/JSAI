using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace JSAI.WinApp
{
    public static class WorkflowNodeCatalog
    {
        public const string Outline = "故事大纲";
        public const string Script = "故事剧本";
        public const string CreativeDescription = "创意描述";
        public const string TextToImage = "文生图";
        public const string ImageToImage = "图生图";
        public const string TextToVideo = "文生视频";
        public const string TextImageToVideo = "文图生视频";
        public const string CharacterDescription = "人物描述";
        public const string LegacySceneDescription = "场景描述";
        public const string CharacterView = "角色设计";
        public const string LegacyCharacterView = "角色视图";
        public const string StoryboardImage = "分镜图片";
        public const string StoryboardBreakdown = "分镜图拆解";
        public const string LegacySceneView = "场景视图";
        public const string StoryboardVideo = "分镜视频";
        public const string VideoPreview = "视频预览";
        public const string VideoCollection = "视频合集";
        public const string LocalAsset = "本地资产";

        public static readonly IReadOnlyList<string> DefaultNodeTypes = new[]
        {
            Outline,
            Script,
            CharacterView,
            StoryboardImage,
            StoryboardBreakdown,
            StoryboardVideo,
            VideoCollection,
        };

        public static readonly IReadOnlyList<string> DirectStudioNodeTypes = new[]
        {
            TextToImage,
            ImageToImage,
            TextToVideo,
            TextImageToVideo,
        };

        public static readonly IReadOnlyList<string> ConfigurableNodeTypes =
            DefaultNodeTypes.Concat(DirectStudioNodeTypes).Distinct(StringComparer.Ordinal).ToArray();

        public static IReadOnlyList<string> GetNodeTypesForMode(ProjectWorkspaceMode mode)
        {
            return mode == ProjectWorkspaceMode.DirectStudio
                ? DirectStudioNodeTypes
                : DefaultNodeTypes;
        }

        public static readonly IReadOnlyList<string> OutlineGenres = new[]
        {
            "霸总 (CEO)",
            "古装 (Historical)",
            "悬疑 (Suspense)",
            "甜宠 (Romance)",
            "复仇 (Revenge)",
            "穿越 (Time Travel)",
            "都市 (Urban)",
            "奇幻 (Fantasy)",
            "萌宝 (Cute Baby)",
            "战神 (God of War)",
        };

        public static readonly IReadOnlyList<string> OutlineSettings = new[]
        {
            "现代都市 (Modern City)",
            "古代宫廷 (Ancient Palace)",
            "豪门别墅 (Luxury Villa)",
            "校园 (School)",
            "医院 (Hospital)",
            "办公室 (Office)",
            "民国 (Republic Era)",
            "仙侠世界 (Xianxia)",
            "赛博朋克 (Cyberpunk)",
        };

        public static readonly IReadOnlyList<string> OutlineVisualStyles = new[]
        {
            "REAL",
            "ANIME",
            "3D",
        };

        public static string NormalizeNodeType(string nodeType)
        {
            if (string.IsNullOrWhiteSpace(nodeType))
            {
                return Outline;
            }

            return nodeType.Trim() switch
            {
                CharacterDescription => CharacterView,
                LegacyCharacterView => CharacterView,
                LegacySceneDescription => StoryboardBreakdown,
                LegacySceneView => StoryboardImage,
                _ => nodeType.Trim(),
            };
        }

        public static bool IsDirectStudioNodeType(string nodeType)
        {
            var normalized = NormalizeNodeType(nodeType);
            return DirectStudioNodeTypes.Contains(normalized, StringComparer.Ordinal);
        }

        public static bool IsAllowedConnection(string sourceNodeType, string targetNodeType)
        {
            var source = NormalizeNodeType(sourceNodeType);
            var target = NormalizeNodeType(targetNodeType);
            return source switch
            {
                Outline => target == Script || target == CharacterView,
                Script => target == CreativeDescription,
                CharacterView => target == StoryboardImage,
                CreativeDescription => target == StoryboardImage,
                StoryboardImage => target == StoryboardBreakdown,
                StoryboardBreakdown => target == StoryboardVideo,
                StoryboardVideo => target == VideoCollection,
                _ => false,
            };
        }

        public static string DescribeAllowedTargets(string sourceNodeType)
        {
            var source = NormalizeNodeType(sourceNodeType);
            var targets = source switch
            {
                Outline => new[] { Script, CharacterView },
                Script => new[] { CreativeDescription },
                CharacterView => new[] { StoryboardImage },
                CreativeDescription => new[] { StoryboardImage },
                StoryboardImage => new[] { StoryboardBreakdown },
                StoryboardBreakdown => new[] { StoryboardVideo },
                StoryboardVideo => new[] { VideoCollection },
                _ => Array.Empty<string>(),
            };

            return targets.Length == 0
                ? "当前节点不允许创建下游连线。"
                : $"“{source}” 只能连接到：{string.Join("、", targets.Select(target => $"“{target}”"))}。";
        }
    }

    public sealed class WorkflowNodeParameters
    {
        public string Input { get; set; } = string.Empty;
        public string CoreIdea { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string Setting { get; set; } = string.Empty;
        public string VisualStyle { get; set; } = "ANIME";
        public int Episodes { get; set; } = 10;
        public decimal DurationMinutes { get; set; } = 1M;
        public string ScriptSourceChapter { get; set; } = string.Empty;
        public int ScriptEpisodesToGenerate { get; set; }
        public string ScriptRevisionNotes { get; set; } = string.Empty;
        public List<GeneratedScriptEpisode> GeneratedScriptEpisodes { get; set; } = new();
        public int SelectedScriptEpisodeIndex { get; set; }
        public string AutoGeneratedSourceNodeId { get; set; } = string.Empty;
        public string AutoGeneratedGroupKey { get; set; } = string.Empty;
        public int AutoGeneratedSequence { get; set; }
        public List<CharacterDesignEntry> CharacterEntries { get; set; } = new();
        public string SelectedCharacterName { get; set; } = string.Empty;
        public string StoryboardGridLayout { get; set; } = "3x3";
        public string StoryboardPanelOrientation { get; set; } = "16:9";
        public List<string> SelectedStoryboardSourceNodeIds { get; set; } = new();
        public List<StoryboardShot> StoryboardShots { get; set; } = new();
        public List<string> StoryboardGridPagePaths { get; set; } = new();
        public int StoryboardCurrentPage { get; set; }
        public int StoryboardTotalPages { get; set; }
        public string StoryboardVideoStage { get; set; } = "idle";
        public List<string> StoryboardVideoSelectedShotIds { get; set; } = new();
        public string StoryboardVideoPrompt { get; set; } = string.Empty;
        public string StoryboardVideoModelPrompt { get; set; } = string.Empty;
        public string StoryboardVideoNegativePrompt { get; set; } = string.Empty;
        public string StoryboardVideoPromptLanguage { get; set; } = "zh";
        public bool StoryboardVideoNeedSound { get; set; }
        public string StoryboardVideoFusedImagePath { get; set; } = string.Empty;
        public string StoryboardVideoTaskId { get; set; } = string.Empty;
        public string StoryboardVideoTaskQueryUrl { get; set; } = string.Empty;
        public string StoryboardVideoLastError { get; set; } = string.Empty;
        public List<StoryboardVideoGeneratedClip> StoryboardVideoGeneratedClips { get; set; } = new();
        public string StoryboardVideoPlatform { get; set; } = "local";
        public string StoryboardVideoModelFamily { get; set; } = "comfyui";
        public string StoryboardVideoSubModel { get; set; } = "workflow";
        public string StoryboardVideoAspectRatio { get; set; } = "16:9";
        public int StoryboardVideoDurationSeconds { get; set; } = 5;
        public int StoryboardVideoFrameRate { get; set; } = 25;
        public string StoryboardVideoQuality { get; set; } = "高清";
        public List<string> VideoCollectionSelectedArtifactPaths { get; set; } = new();
        public string VideoCollectionCurrentArtifactPath { get; set; } = string.Empty;
        public string VideoCollectionPlaylistPath { get; set; } = string.Empty;
        public string VideoCollectionEditProjectPath { get; set; } = string.Empty;
        public List<VideoCollectionTimelineClip> VideoCollectionTimelineClips { get; set; } = new();
        public List<VideoCollectionImportedAsset> VideoCollectionImportedAssets { get; set; } = new();
        public List<VideoCollectionOverlayItem> VideoCollectionOverlayItems { get; set; } = new();
        public bool VideoCollectionSelectionInitialized { get; set; }
        public string VideoCollectionAudioPath { get; set; } = string.Empty;
        public decimal VideoCollectionAudioVolume { get; set; } = 1M;
        public string VideoCollectionSubtitleText { get; set; } = string.Empty;
        public string VideoCollectionTransitionType { get; set; } = "none";
        public decimal VideoCollectionTransitionSeconds { get; set; } = 0.4M;
        public string PreferredModelId { get; set; } = string.Empty;
        public string PreferredTextModelId { get; set; } = string.Empty;
        public string PreferredImageModelId { get; set; } = string.Empty;
        public string CharacterTextToImageModelId { get; set; } = string.Empty;
        public string CharacterImageToImageModelId { get; set; } = string.Empty;
        public string StoryboardTextToImageModelId { get; set; } = string.Empty;
        public string StoryboardImageToImageModelId { get; set; } = string.Empty;
        public string PreferredVideoModelId { get; set; } = string.Empty;
        public string DirectReferenceImagePath { get; set; } = string.Empty;
        public string DirectPositivePrompt { get; set; } = string.Empty;
        public string DirectNegativePrompt { get; set; } = string.Empty;
        public string DirectPromptModelName { get; set; } = string.Empty;
        public string DirectExecutionModelName { get; set; } = string.Empty;
        public string DirectImageMode { get; set; } = "single";
        public string DirectAspectRatio { get; set; } = "1:1";
        public string DirectResolutionPreset { get; set; } = "2K";
        public int DirectWidth { get; set; } = 2048;
        public int DirectHeight { get; set; } = 2048;
        public int DirectDurationSeconds { get; set; } = 5;
        public int DirectVideoFrameRate { get; set; } = 25;
        public string DirectQuality { get; set; } = "高清";
        public string DirectVideoFilenamePrefix { get; set; } = "video/LTX_2.3_i2v";

        [JsonIgnore]
        public bool HasOutlineConfiguration =>
            !string.IsNullOrWhiteSpace(CoreIdea) ||
            !string.IsNullOrWhiteSpace(Genre) ||
            !string.IsNullOrWhiteSpace(Setting) ||
            !string.IsNullOrWhiteSpace(VisualStyle) ||
            Episodes > 0 ||
            DurationMinutes > 0;

        public void EnsureDefaults(string nodeType)
        {
            MigrateLegacyPreferredModel(nodeType);

            if (nodeType == WorkflowNodeCatalog.TextToImage || nodeType == WorkflowNodeCatalog.ImageToImage)
            {
                DirectImageMode = NormalizeDirectImageMode(DirectImageMode);
                DirectAspectRatio = NormalizeDirectAspectRatio(DirectAspectRatio, "1:1");
                DirectResolutionPreset = NormalizeDirectResolutionPreset(DirectResolutionPreset);
                if (DirectWidth <= 0 || DirectHeight <= 0)
                {
                    ApplyDirectCanvasDefaults(DirectAspectRatio, DirectResolutionPreset);
                }

                return;
            }

            if (nodeType == WorkflowNodeCatalog.TextToVideo || nodeType == WorkflowNodeCatalog.TextImageToVideo)
            {
                DirectAspectRatio = NormalizeDirectAspectRatio(DirectAspectRatio, "16:9");
                DirectResolutionPreset = NormalizeDirectResolutionPreset(DirectResolutionPreset);
                DirectDurationSeconds = Math.Max(5, DirectDurationSeconds);
                DirectVideoFrameRate = Math.Clamp(DirectVideoFrameRate <= 0 ? 25 : DirectVideoFrameRate, 8, 60);
                if (string.IsNullOrWhiteSpace(DirectQuality))
                {
                    DirectQuality = "高清";
                }
                if (string.IsNullOrWhiteSpace(DirectVideoFilenamePrefix))
                {
                    DirectVideoFilenamePrefix = "video/LTX_2.3_i2v";
                }
                if (string.IsNullOrWhiteSpace(StoryboardVideoPlatform))
                {
                    StoryboardVideoPlatform = "local";
                }
                if (string.IsNullOrWhiteSpace(StoryboardVideoModelFamily))
                {
                    StoryboardVideoModelFamily = "comfyui";
                }
                if (string.IsNullOrWhiteSpace(StoryboardVideoSubModel))
                {
                    StoryboardVideoSubModel = GetDefaultStoryboardVideoSubModel(StoryboardVideoModelFamily);
                }

                if (DirectWidth <= 0 || DirectHeight <= 0)
                {
                    ApplyDirectCanvasDefaults(DirectAspectRatio, DirectResolutionPreset);
                }

                ClampDirectVideoCanvasSize();
                DirectReferenceImagePath ??= string.Empty;
                return;
            }

            if (nodeType == WorkflowNodeCatalog.StoryboardVideo)
            {
                StoryboardVideoAspectRatio = NormalizeDirectAspectRatio(StoryboardVideoAspectRatio, "16:9");
                StoryboardVideoDurationSeconds = Math.Max(5, StoryboardVideoDurationSeconds);
                StoryboardVideoFrameRate = Math.Clamp(StoryboardVideoFrameRate <= 0 ? 25 : StoryboardVideoFrameRate, 8, 30);
                if (string.IsNullOrWhiteSpace(StoryboardVideoQuality))
                {
                    StoryboardVideoQuality = "高清";
                }
            }

            if (nodeType == WorkflowNodeCatalog.Script)
            {
                GeneratedScriptEpisodes ??= new List<GeneratedScriptEpisode>();
                if (ScriptEpisodesToGenerate <= 0)
                {
                    ScriptEpisodesToGenerate = 1;
                }

                return;
            }

            if (nodeType == WorkflowNodeCatalog.CharacterView || nodeType == WorkflowNodeCatalog.CharacterDescription)
            {
                CharacterEntries ??= new List<CharacterDesignEntry>();
                return;
            }

            if (nodeType == WorkflowNodeCatalog.StoryboardImage)
            {
                SelectedStoryboardSourceNodeIds ??= new List<string>();
                StoryboardShots ??= new List<StoryboardShot>();
                StoryboardGridPagePaths ??= new List<string>();
                StoryboardTextToImageModelId ??= string.Empty;
                StoryboardImageToImageModelId ??= string.Empty;
                if (!string.Equals(StoryboardGridLayout, "2x3", StringComparison.Ordinal))
                {
                    StoryboardGridLayout = "3x3";
                }

                if (!string.Equals(StoryboardPanelOrientation, "9:16", StringComparison.Ordinal))
                {
                    StoryboardPanelOrientation = "16:9";
                }

                return;
            }

            if (nodeType == WorkflowNodeCatalog.StoryboardBreakdown)
            {
                SelectedStoryboardSourceNodeIds ??= new List<string>();
                StoryboardShots ??= new List<StoryboardShot>();
                StoryboardGridPagePaths ??= new List<string>();
                StoryboardCurrentPage = Math.Max(0, StoryboardCurrentPage);
                StoryboardTotalPages = Math.Max(0, StoryboardTotalPages);
                return;
            }

            if (nodeType == WorkflowNodeCatalog.StoryboardVideo)
            {
                StoryboardVideoSelectedShotIds ??= new List<string>();
                StoryboardVideoPrompt ??= string.Empty;
                StoryboardVideoModelPrompt ??= string.Empty;
                if (string.IsNullOrWhiteSpace(StoryboardVideoPromptLanguage))
                {
                    StoryboardVideoPromptLanguage = "zh";
                }
                StoryboardVideoFusedImagePath ??= string.Empty;
                StoryboardVideoTaskId ??= string.Empty;
                StoryboardVideoTaskQueryUrl ??= string.Empty;
                StoryboardVideoLastError ??= string.Empty;
                StoryboardVideoGeneratedClips ??= new List<StoryboardVideoGeneratedClip>();
                if (string.IsNullOrWhiteSpace(StoryboardVideoStage))
                {
                    StoryboardVideoStage = "idle";
                }

                if (!string.Equals(StoryboardVideoAspectRatio, "9:16", StringComparison.Ordinal))
                {
                    StoryboardVideoAspectRatio = "16:9";
                }

                if (string.IsNullOrWhiteSpace(StoryboardVideoPlatform))
                {
                    StoryboardVideoPlatform = "local";
                }

                if (string.IsNullOrWhiteSpace(StoryboardVideoModelFamily))
                {
                    StoryboardVideoModelFamily = "comfyui";
                }

                if (string.IsNullOrWhiteSpace(StoryboardVideoSubModel))
                {
                    StoryboardVideoSubModel = GetDefaultStoryboardVideoSubModel(StoryboardVideoModelFamily);
                }

                if (StoryboardVideoDurationSeconds <= 0)
                {
                    StoryboardVideoDurationSeconds = 5;
                }

                if (string.IsNullOrWhiteSpace(StoryboardVideoQuality))
                {
                    StoryboardVideoQuality = "高清";
                }

                if (string.Equals(StoryboardVideoQuality, "标准", StringComparison.Ordinal))
                {
                    StoryboardVideoQuality = "标清";
                }

                return;
            }

            if (nodeType == WorkflowNodeCatalog.VideoCollection)
            {
                VideoCollectionSelectedArtifactPaths ??= new List<string>();
                VideoCollectionCurrentArtifactPath ??= string.Empty;
                VideoCollectionPlaylistPath ??= string.Empty;
                VideoCollectionEditProjectPath ??= string.Empty;
                VideoCollectionTimelineClips ??= new List<VideoCollectionTimelineClip>();
                VideoCollectionImportedAssets ??= new List<VideoCollectionImportedAsset>();
                VideoCollectionOverlayItems ??= new List<VideoCollectionOverlayItem>();
                VideoCollectionAudioPath ??= string.Empty;
                VideoCollectionSubtitleText ??= string.Empty;
                VideoCollectionTransitionType = NormalizeVideoCollectionTransitionType(VideoCollectionTransitionType);
                VideoCollectionTransitionSeconds = Math.Clamp(VideoCollectionTransitionSeconds <= 0M ? 0.4M : VideoCollectionTransitionSeconds, 0.2M, 2M);
                VideoCollectionAudioVolume = Math.Clamp(VideoCollectionAudioVolume <= 0M ? 1M : VideoCollectionAudioVolume, 0.1M, 2M);
                foreach (var asset in VideoCollectionImportedAssets)
                {
                    asset.Id = string.IsNullOrWhiteSpace(asset.Id) ? Guid.NewGuid().ToString("N") : asset.Id;
                    asset.Kind = string.IsNullOrWhiteSpace(asset.Kind) ? "video" : asset.Kind.Trim().ToLowerInvariant();
                    asset.FilePath ??= string.Empty;
                    asset.DisplayName ??= string.Empty;
                    asset.DurationSeconds = Math.Clamp(asset.DurationSeconds <= 0 ? 5 : asset.DurationSeconds, 1, 3600);
                }

                foreach (var overlay in VideoCollectionOverlayItems)
                {
                    overlay.Id = string.IsNullOrWhiteSpace(overlay.Id) ? Guid.NewGuid().ToString("N") : overlay.Id;
                    overlay.Kind = NormalizeVideoCollectionOverlayKind(overlay.Kind);
                    overlay.Text ??= string.Empty;
                    overlay.ImagePath ??= string.Empty;
                    overlay.StartSeconds = Math.Clamp(overlay.StartSeconds < 0M ? 0M : overlay.StartSeconds, 0M, 86400M);
                    overlay.DurationSeconds = Math.Clamp(overlay.DurationSeconds <= 0M ? 3M : overlay.DurationSeconds, 0.2M, 86400M);
                    overlay.X = Math.Clamp(overlay.X, 0M, 1M);
                    overlay.Y = Math.Clamp(overlay.Y, 0M, 1M);
                    overlay.WidthRatio = Math.Clamp(overlay.WidthRatio <= 0M ? 0.28M : overlay.WidthRatio, 0.05M, 0.9M);
                    overlay.FontSize = Math.Clamp(overlay.FontSize <= 0 ? 44 : overlay.FontSize, 14, 160);
                    overlay.ForeColor = string.IsNullOrWhiteSpace(overlay.ForeColor) ? "#FFFFFF" : overlay.ForeColor;
                }
                return;
            }

            if (nodeType != WorkflowNodeCatalog.Outline)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(CoreIdea) && !string.IsNullOrWhiteSpace(Input))
            {
                CoreIdea = Input.Trim();
            }

            if (!WorkflowNodeCatalog.OutlineVisualStyles.Contains(VisualStyle))
            {
                VisualStyle = "ANIME";
            }

            if (Episodes <= 0)
            {
                Episodes = 10;
            }

            if (DurationMinutes <= 0)
            {
                DurationMinutes = 1M;
            }
        }

        public string GetPreferredModelId(ModelCategory category)
        {
            return category switch
            {
                ModelCategory.Text => PreferredTextModelId ?? string.Empty,
                ModelCategory.Image => PreferredImageModelId ?? string.Empty,
                ModelCategory.Video => PreferredVideoModelId ?? string.Empty,
                _ => string.Empty,
            };
        }

        public void SetPreferredModelId(ModelCategory category, string modelId)
        {
            var normalized = modelId?.Trim() ?? string.Empty;
            switch (category)
            {
                case ModelCategory.Text:
                    PreferredTextModelId = normalized;
                    break;
                case ModelCategory.Image:
                    PreferredImageModelId = normalized;
                    break;
                case ModelCategory.Video:
                    PreferredVideoModelId = normalized;
                    break;
            }
        }

        private void MigrateLegacyPreferredModel(string nodeType)
        {
            if (string.IsNullOrWhiteSpace(PreferredModelId))
            {
                return;
            }

            var categories = WorkflowExecutor.GetRequiredModelCategories(nodeType);
            if (categories.Count == 1)
            {
                var category = categories[0];
                if (string.IsNullOrWhiteSpace(GetPreferredModelId(category)))
                {
                    SetPreferredModelId(category, PreferredModelId);
                }
            }
            else
            {
                var legacyCategory = WorkflowExecutor.GetModelCategory(nodeType);
                if (legacyCategory != null && string.IsNullOrWhiteSpace(GetPreferredModelId(legacyCategory.Value)))
                {
                    SetPreferredModelId(legacyCategory.Value, PreferredModelId);
                }
            }

            PreferredModelId = string.Empty;
        }

        public string BuildOutlineSummary()
        {
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(CoreIdea))
            {
                lines.Add($"核心创意：{CoreIdea.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(Genre))
            {
                lines.Add($"类型：{Genre}");
            }

            if (!string.IsNullOrWhiteSpace(Setting))
            {
                lines.Add($"背景：{Setting}");
            }

            if (!string.IsNullOrWhiteSpace(VisualStyle))
            {
                lines.Add($"视觉风格：{GetVisualStyleDisplayName(VisualStyle)}");
            }

            lines.Add($"总集数：{Math.Max(1, Episodes)}");
            lines.Add($"单集时长：{(DurationMinutes <= 0 ? 1M : DurationMinutes):0.##} 分钟");
            return string.Join(Environment.NewLine, lines);
        }

        public string BuildScriptSummary()
        {
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(ScriptSourceChapter))
            {
                lines.Add($"来源章节：{ScriptSourceChapter.Trim()}");
            }

            lines.Add($"本次生成集数：{Math.Max(1, ScriptEpisodesToGenerate)}");

            if (!string.IsNullOrWhiteSpace(ScriptRevisionNotes))
            {
                lines.Add($"修改建议：{ScriptRevisionNotes.Trim()}");
            }

            if (GeneratedScriptEpisodes != null && GeneratedScriptEpisodes.Count > 0)
            {
                lines.Add($"已生成分集：{GeneratedScriptEpisodes.Count}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        public string BuildCharacterDesignSummary()
        {
            CharacterEntries ??= new List<CharacterDesignEntry>();
            var lines = new List<string>
            {
                $"角色数量：{CharacterEntries.Count}",
            };

            if (!string.IsNullOrWhiteSpace(SelectedCharacterName))
            {
                lines.Add($"当前角色：{SelectedCharacterName}");
            }

            var completedProfiles = CharacterEntries.Count(entry => entry.HasProfileData);
            var completedExpressions = CharacterEntries.Count(entry => entry.HasExpressionSheet);
            var completedTurnarounds = CharacterEntries.Count(entry => entry.HasThreeViewSheet);
            lines.Add($"角色档案：{completedProfiles}/{CharacterEntries.Count}");
            lines.Add($"九宫格：{completedExpressions}/{CharacterEntries.Count}");
            lines.Add($"三视图：{completedTurnarounds}/{CharacterEntries.Count}");
            return string.Join(Environment.NewLine, lines);
        }

        public string BuildStoryboardImageSummary()
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(Input))
            {
                lines.Add($"分镜描述：{Input.Trim()}");
            }

            lines.Add($"网格布局：{(StoryboardGridLayout == "2x3" ? "六宫格 (2x3)" : "九宫格 (3x3)")}");
            lines.Add($"画板方向：{(StoryboardPanelOrientation == "9:16" ? "竖屏 (9:16)" : "横屏 (16:9)")}");
            if (StoryboardShots != null && StoryboardShots.Count > 0)
            {
                lines.Add($"分镜条目：{StoryboardShots.Count}");
            }

            if (StoryboardGridPagePaths != null && StoryboardGridPagePaths.Count > 0)
            {
                lines.Add($"生成页数：{StoryboardGridPagePaths.Count}");
            }
            return string.Join(Environment.NewLine, lines);
        }

        public string BuildStoryboardBreakdownSummary()
        {
            StoryboardShots ??= new List<StoryboardShot>();
            var lines = new List<string>
            {
                $"分镜条目：{StoryboardShots.Count}",
            };

            var totalDuration = StoryboardShots.Sum(shot => Math.Max(1, shot.DurationSeconds));
            if (totalDuration > 0)
            {
                lines.Add($"总时长：{totalDuration} 秒");
            }

            var sceneCount = StoryboardShots
                .Select(shot => shot.Scene.Trim())
                .Where(scene => !string.IsNullOrWhiteSpace(scene))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (sceneCount > 0)
            {
                lines.Add($"场景数量：{sceneCount}");
            }

            var preview = StoryboardShots.FirstOrDefault();
            if (preview != null)
            {
                lines.Add($"首条分镜：{preview.DisplayTitle}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        public string BuildStoryboardVideoSummary()
        {
            StoryboardVideoSelectedShotIds ??= new List<string>();
            StoryboardShots ??= new List<StoryboardShot>();

            var lines = new List<string>
            {
                $"阶段：{GetStoryboardVideoStageDisplayName(StoryboardVideoStage)}",
                $"已选分镜：{StoryboardVideoSelectedShotIds.Count}",
                $"提示词：中文显示 / 英文执行",
                $"声音：{(StoryboardVideoNeedSound ? "需要" : "关闭")}",
                $"总分镜：{StoryboardShots.Count}",
                $"平台：{GetStoryboardVideoPlatformDisplayName(StoryboardVideoPlatform)}",
                $"主模型：{GetStoryboardVideoModelFamilyDisplayName(StoryboardVideoModelFamily)}",
                $"子模型：{GetStoryboardVideoSubModelDisplayName(StoryboardVideoSubModel)}",
                $"画幅：{StoryboardVideoAspectRatio}",
                $"时长：{Math.Max(1, StoryboardVideoDurationSeconds)} 秒",
                $"质量：{StoryboardVideoQuality}",
            };

            if (!string.IsNullOrWhiteSpace(StoryboardVideoPrompt))
            {
                lines.Add("提示词：已生成");
            }

            if (!string.IsNullOrWhiteSpace(StoryboardVideoFusedImagePath) && File.Exists(StoryboardVideoFusedImagePath))
            {
                lines.Add("参考图：已生成");
            }

            return string.Join(Environment.NewLine, lines);
        }

        public string BuildVideoCollectionSummary()
        {
            VideoCollectionSelectedArtifactPaths ??= new List<string>();
            VideoCollectionImportedAssets ??= new List<VideoCollectionImportedAsset>();
            VideoCollectionOverlayItems ??= new List<VideoCollectionOverlayItem>();
            var lines = new List<string>
            {
                $"已选视频：{VideoCollectionSelectedArtifactPaths.Count}",
            };

            if (VideoCollectionImportedAssets.Count > 0)
            {
                lines.Add($"导入素材：{VideoCollectionImportedAssets.Count}");
            }

            if (!string.IsNullOrWhiteSpace(VideoCollectionCurrentArtifactPath))
            {
                lines.Add($"当前预览：{Path.GetFileName(VideoCollectionCurrentArtifactPath)}");
            }

            if (!string.IsNullOrWhiteSpace(VideoCollectionPlaylistPath))
            {
                lines.Add($"合集清单：{Path.GetFileName(VideoCollectionPlaylistPath)}");
            }

            if (VideoCollectionTimelineClips?.Count > 0)
            {
                lines.Add($"时间线片段：{VideoCollectionTimelineClips.Count}");
            }

            if (!string.IsNullOrWhiteSpace(VideoCollectionAudioPath))
            {
                lines.Add($"音轨：{Path.GetFileName(VideoCollectionAudioPath)}");
            }

            if (!string.IsNullOrWhiteSpace(VideoCollectionSubtitleText))
            {
                lines.Add("字幕：已填写");
            }

            if (VideoCollectionOverlayItems.Count > 0)
            {
                lines.Add($"叠加轨道：{VideoCollectionOverlayItems.Count}");
            }

            if (!string.Equals(VideoCollectionTransitionType, "none", StringComparison.Ordinal))
            {
                lines.Add($"转场：{GetVideoCollectionTransitionDisplayName(VideoCollectionTransitionType)}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        public static string GetVisualStyleDisplayName(string value)
        {
            return value switch
            {
                "REAL" => "真人",
                "ANIME" => "动漫",
                "3D" => "3D",
                _ => value,
            };
        }

        public static string GetStoryboardVideoStageDisplayName(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "selecting" => "选择分镜",
                "prompting" => "编辑提示词",
                "generating" => "生成视频",
                "completed" => "已完成",
                _ => "待获取分镜",
            };
        }

        public static string NormalizeVideoCollectionTransitionType(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "fade" => "fade",
                "black" => "black",
                "flash" => "flash",
                _ => "none",
            };
        }

        public static string GetVideoCollectionTransitionDisplayName(string? value)
        {
            return NormalizeVideoCollectionTransitionType(value) switch
            {
                "fade" => "淡入淡出",
                "black" => "黑场切换",
                "flash" => "闪白切换",
                _ => "无",
            };
        }

        public static string NormalizeVideoCollectionOverlayKind(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "image" => "image",
                _ => "text",
            };
        }

        public static string GetStoryboardVideoPlatformDisplayName(string? value)
        {
            return string.Equals((value ?? string.Empty).Trim(), "local", StringComparison.OrdinalIgnoreCase)
                ? "本地视频工作流"
                : (value ?? string.Empty).Trim();
        }

        public static string GetStoryboardVideoPromptLanguageDisplayName(string? value)
        {
            return string.Equals((value ?? string.Empty).Trim(), "en", StringComparison.OrdinalIgnoreCase)
                ? "英文"
                : "中文";
        }

        public static string GetStoryboardVideoModelFamilyDisplayName(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "veo" => "Veo",
                "luma" => "Luma Dream Machine",
                "runway" => "Runway Gen-3",
                "minimax" => "海螺",
                "volcengine" => "豆包",
                "grok" => "Grok",
                "qwen" => "通义万相",
                "sora" => "Sora",
                "comfyui" => "ComfyUI",
                _ => string.IsNullOrWhiteSpace(value) ? "ComfyUI" : value.Trim(),
            };
        }

        public static string GetStoryboardVideoSubModelDisplayName(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "ray-v2" => "Ray V2",
                "photon" => "Photon",
                "photon-flash" => "Photon Flash",
                "veo3.1-4k" => "Veo 3.1 4K",
                "veo3.1-pro-4k" => "Veo 3.1 Pro 4K",
                "veo3.1-fast" => "Veo 3.1 Fast",
                "grok-video-3-10s" => "Grok Video 3 (10s)",
                "sora-2" => "Sora 2",
                "gen3-alpha-turbo" => "Gen-3 Alpha Turbo",
                "video-01" => "Video-01",
                "doubao-video-1" => "Doubao Video 1",
                "grok-2-video" => "Grok 2 Video",
                "qwen-video" => "Qwen Video",
                "workflow" => "工作流",
                _ => string.IsNullOrWhiteSpace(value) ? "工作流" : value.Trim(),
            };
        }

        public static string GetDefaultStoryboardVideoSubModel(string? modelFamily)
        {
            return (modelFamily ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "veo" => "veo3.1-fast",
                "runway" => "gen3-alpha-turbo",
                "minimax" => "video-01",
                "volcengine" => "doubao-video-1",
                "grok" => "grok-video-3-10s",
                "qwen" => "qwen-video",
                "sora" => "sora-2",
                "comfyui" => "workflow",
                _ => "workflow",
            };
        }

        public string BuildDirectStudioSummary(string nodeType)
        {
            var lines = new List<string>();
            if (nodeType == WorkflowNodeCatalog.TextToImage)
            {
                lines.Add($"模式：{GetDirectImageModeDisplayName(DirectImageMode)}");
            }

            lines.Add($"画幅：{DirectAspectRatio}");
            lines.Add($"尺寸：{Math.Max(1, DirectWidth)} × {Math.Max(1, DirectHeight)}");

            if (nodeType == WorkflowNodeCatalog.TextToVideo || nodeType == WorkflowNodeCatalog.TextImageToVideo)
            {
                lines.Add($"时长：{Math.Max(5, DirectDurationSeconds)} 秒");
                lines.Add($"画质：{DirectQuality}");
            }

            if (nodeType == WorkflowNodeCatalog.TextImageToVideo && !string.IsNullOrWhiteSpace(DirectReferenceImagePath))
            {
                lines.Add($"参考图：{Path.GetFileName(DirectReferenceImagePath)}");
            }

            if (!string.IsNullOrWhiteSpace(DirectPromptModelName))
            {
                lines.Add($"提示词模型：{DirectPromptModelName}");
            }

            if (!string.IsNullOrWhiteSpace(DirectExecutionModelName))
            {
                lines.Add($"执行模型：{DirectExecutionModelName}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private void ApplyDirectCanvasDefaults(string aspectRatio, string resolutionPreset)
        {
            var normalizedPreset = NormalizeDirectResolutionPreset(resolutionPreset);
            var longSide = normalizedPreset switch
            {
                "768" => 768,
                "1024" => 1024,
                "4K" => 3840,
                _ => 2048,
            };
            var shortSide = normalizedPreset switch
            {
                "768" => 432,
                "1024" => 576,
                "4K" => 2160,
                _ => 1152,
            };

            (DirectWidth, DirectHeight) = aspectRatio switch
            {
                "21:9" => (longSide, (int)Math.Round(longSide / 2.3333)),
                "16:9" => (longSide, shortSide),
                "3:2" => (longSide, (int)Math.Round(longSide / 1.5)),
                "4:3" => (longSide, (int)Math.Round(longSide / 1.3333)),
                "3:4" => ((int)Math.Round(longSide / 1.3333), longSide),
                "2:3" => ((int)Math.Round(longSide / 1.5), longSide),
                "9:16" => (shortSide, longSide),
                _ => (longSide, longSide),
            };
        }

        private void ClampDirectVideoCanvasSize()
        {
            var portrait = string.Equals(DirectAspectRatio, "9:16", StringComparison.OrdinalIgnoreCase);
            var maxWidth = portrait ? 720 : 1280;
            var maxHeight = portrait ? 1280 : 720;

            DirectWidth = Math.Clamp(DirectWidth <= 0 ? maxWidth : DirectWidth, 256, maxWidth);
            DirectHeight = Math.Clamp(DirectHeight <= 0 ? maxHeight : DirectHeight, 256, maxHeight);
        }

        private static string NormalizeDirectAspectRatio(string? value, string fallback)
        {
            var normalized = (value ?? string.Empty).Trim();
            return normalized switch
            {
                "智能" => "智能",
                "21:9" => "21:9",
                "16:9" => "16:9",
                "3:2" => "3:2",
                "4:3" => "4:3",
                "1:1" => "1:1",
                "3:4" => "3:4",
                "2:3" => "2:3",
                "9:16" => "9:16",
                _ => fallback,
            };
        }

        private static string NormalizeDirectResolutionPreset(string? value)
        {
            return (value ?? string.Empty).Trim() switch
            {
                "768" => "768",
                "1024" => "1024",
                "4K" => "4K",
                _ => "2K",
            };
        }

        private static string NormalizeDirectImageMode(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "expression" => "expression",
                "threeview" => "threeview",
                _ => "single",
            };
        }

        public static string GetDirectImageModeDisplayName(string? value)
        {
            return NormalizeDirectImageMode(value) switch
            {
                "expression" => "九宫格",
                "threeview" => "三视图",
                _ => "普通",
            };
        }
    }

    public sealed class OutlineChapterPlan
    {
        public string ChapterLabel { get; set; } = string.Empty;

        public string ChapterName { get; set; } = string.Empty;

        public int StartEpisode { get; set; }

        public int EndEpisode { get; set; }

        [JsonIgnore]
        public int EpisodeCount => StartEpisode > 0 && EndEpisode >= StartEpisode
            ? EndEpisode - StartEpisode + 1
            : 1;

        [JsonIgnore]
        public string Title => string.IsNullOrWhiteSpace(ChapterName)
            ? ChapterLabel
            : $"{ChapterLabel}：{ChapterName}";

        [JsonIgnore]
        public string DisplayText => StartEpisode > 0 && EndEpisode >= StartEpisode
            ? $"{Title}（第{StartEpisode}-{EndEpisode}集）"
            : Title;

        public override string ToString()
        {
            return DisplayText;
        }
    }

    public sealed class GeneratedScriptEpisode
    {
        public string Title { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public string Characters { get; set; } = string.Empty;

        public string KeyItems { get; set; } = string.Empty;

        public string VisualStyleNote { get; set; } = string.Empty;

        public string ContinuityNote { get; set; } = string.Empty;

        [JsonIgnore]
        public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "未命名分集" : Title;
    }

    public sealed class VideoCollectionTimelineClip
    {
        public string ArtifactPath { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        public string Caption { get; set; } = string.Empty;
    }

    public sealed class VideoCollectionImportedAsset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Kind { get; set; } = "video";

        public string FilePath { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public int DurationSeconds { get; set; } = 5;
    }

    public sealed class VideoCollectionOverlayItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Kind { get; set; } = "text";

        public string Text { get; set; } = string.Empty;

        public string ImagePath { get; set; } = string.Empty;

        public decimal StartSeconds { get; set; }

        public decimal DurationSeconds { get; set; } = 3M;

        public decimal X { get; set; } = 0.5M;

        public decimal Y { get; set; } = 0.82M;

        public int FontSize { get; set; } = 44;

        public string ForeColor { get; set; } = "#FFFFFF";

        public decimal WidthRatio { get; set; } = 0.28M;
    }

    public sealed class StoryboardVideoGeneratedClip
    {
        public string ArtifactPath { get; set; } = string.Empty;

        public string ReferenceImagePath { get; set; } = string.Empty;

        public string ShotId { get; set; } = string.Empty;

        public int ShotNumber { get; set; }

        public string Scene { get; set; } = string.Empty;

        public int DurationSeconds { get; set; }

        public string AspectRatio { get; set; } = "16:9";

        public string Prompt { get; set; } = string.Empty;

        public string ModelPrompt { get; set; } = string.Empty;
    }

    public sealed class StoryboardShot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public int ShotNumber { get; set; }

        public string Scene { get; set; } = string.Empty;

        public List<string> Characters { get; set; } = new();

        public int DurationSeconds { get; set; } = 3;

        public string ShotSize { get; set; } = "中景";

        public string CameraAngle { get; set; } = "平视";

        public string CameraMovement { get; set; } = "固定";

        public string VisualDescription { get; set; } = string.Empty;

        public string ImagePrompt { get; set; } = string.Empty;

        public string Dialogue { get; set; } = string.Empty;

        public string VisualEffects { get; set; } = string.Empty;

        public string AudioEffects { get; set; } = string.Empty;

        public int StartTime { get; set; }

        public int EndTime { get; set; }

        public string SplitImagePath { get; set; } = string.Empty;

        public string SourceNodeId { get; set; } = string.Empty;

        public int SourcePage { get; set; }

        public int PanelIndex { get; set; }

        [JsonIgnore]
        public string CharactersDisplay => Characters == null || Characters.Count == 0
            ? "无角色出镜"
            : string.Join("、", Characters.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));

        [JsonIgnore]
        public string DisplayTitle => string.IsNullOrWhiteSpace(Scene)
            ? $"分镜 #{Math.Max(1, ShotNumber)}"
            : Scene.Trim();

        [JsonIgnore]
        public string DurationLabel => $"{Math.Max(1, DurationSeconds)}秒";

        [JsonIgnore]
        public string VisualPreview
        {
            get
            {
                var text = string.IsNullOrWhiteSpace(VisualDescription) ? "待补充分镜画面描述" : VisualDescription.Trim();
                return text.Length <= 88 ? text : $"{text[..88]}...";
            }
        }

        public StoryboardShot Clone()
        {
            return new StoryboardShot
            {
                Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
                ShotNumber = ShotNumber,
                Scene = Scene,
                Characters = new List<string>(Characters ?? new List<string>()),
                DurationSeconds = DurationSeconds,
                ShotSize = ShotSize,
                CameraAngle = CameraAngle,
                CameraMovement = CameraMovement,
                VisualDescription = VisualDescription,
                ImagePrompt = ImagePrompt,
                Dialogue = Dialogue,
                VisualEffects = VisualEffects,
                AudioEffects = AudioEffects,
                StartTime = StartTime,
                EndTime = EndTime,
                SplitImagePath = SplitImagePath,
                SourceNodeId = SourceNodeId,
                SourcePage = SourcePage,
                PanelIndex = PanelIndex,
            };
        }
    }

    public sealed class CharacterDesignEntry
    {
        public string Name { get; set; } = string.Empty;

        public string Alias { get; set; } = string.Empty;

        public string RoleType { get; set; } = CharacterDesignRoleType.Main.ToLabel();

        public string Summary { get; set; } = string.Empty;

        public string BasicStats { get; set; } = string.Empty;

        public string Profession { get; set; } = string.Empty;

        public string Background { get; set; } = string.Empty;

        public string Personality { get; set; } = string.Empty;

        public string Motivation { get; set; } = string.Empty;

        public string Values { get; set; } = string.Empty;

        public string Weakness { get; set; } = string.Empty;

        public string Relationships { get; set; } = string.Empty;

        public string Habits { get; set; } = string.Empty;

        public string VisualTags { get; set; } = string.Empty;

        public string AppearancePrompt { get; set; } = string.Empty;

        public string CostumeNotes { get; set; } = string.Empty;

        public string ActingNotes { get; set; } = string.Empty;

        public string ExpressionPrompt { get; set; } = string.Empty;

        public string ThreeViewPrompt { get; set; } = string.Empty;

        public string ExpressionSheetPath { get; set; } = string.Empty;

        public string ThreeViewSheetPath { get; set; } = string.Empty;

        public string ReferencePortraitPath { get; set; } = string.Empty;

        public CharacterAssetStatus ProfileStatus { get; set; } = CharacterAssetStatus.Pending;

        public CharacterAssetStatus ExpressionStatus { get; set; } = CharacterAssetStatus.Pending;

        public CharacterAssetStatus ThreeViewStatus { get; set; } = CharacterAssetStatus.Pending;

        public bool AssetsSaved { get; set; }

        public string SavedAssetFolderPath { get; set; } = string.Empty;

        public string LastError { get; set; } = string.Empty;

        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Name : $"{Name} / {Alias}";

        [JsonIgnore]
        public bool HasExpressionSheet => !string.IsNullOrWhiteSpace(ExpressionSheetPath) && File.Exists(ExpressionSheetPath);

        [JsonIgnore]
        public bool HasThreeViewSheet => !string.IsNullOrWhiteSpace(ThreeViewSheetPath) && File.Exists(ThreeViewSheetPath);

        [JsonIgnore]
        public bool HasReferencePortrait => !string.IsNullOrWhiteSpace(ReferencePortraitPath) && File.Exists(ReferencePortraitPath);

        [JsonIgnore]
        public bool HasProfileData =>
            ProfileStatus == CharacterAssetStatus.Success ||
            !string.IsNullOrWhiteSpace(BasicStats) ||
            !string.IsNullOrWhiteSpace(Personality) ||
            !string.IsNullOrWhiteSpace(Motivation) ||
            !string.IsNullOrWhiteSpace(AppearancePrompt) ||
            !string.IsNullOrWhiteSpace(Summary);

        [JsonIgnore]
        public string LatestArtifactPath => HasThreeViewSheet ? ThreeViewSheetPath : (HasExpressionSheet ? ExpressionSheetPath : ReferencePortraitPath);

        [JsonIgnore]
        public CharacterDesignRoleType NormalizedRoleType => CharacterDesignRoleTypeExtensions.Parse(RoleType);

        [JsonIgnore]
        public string CompactSummary
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Summary) && !LooksLikeRawStructuredText(Summary))
                {
                    return Summary;
                }

                var parts = new[] { BasicStats, Profession, Personality }
                    .Where(value => !string.IsNullOrWhiteSpace(value) && !LooksLikeRawStructuredText(value))
                    .Select(value => value.Trim());
                return string.Join(" / ", parts);
            }
        }

        public static bool LooksLikeRawStructuredText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.TrimStart();
            return text.StartsWith("{", StringComparison.Ordinal) ||
                   text.StartsWith("[", StringComparison.Ordinal) ||
                   text.StartsWith("```json", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("json {", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("json [", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class WorkflowNode
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public bool IsMinimized { get; set; }
        public WorkflowNodeParameters Params { get; set; } = new();
        public string Output { get; set; } = string.Empty;
        public string ArtifactPath { get; set; } = string.Empty;
        public string ArtifactKind { get; set; } = string.Empty;
    }

    public sealed class WorkflowEdge
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
    }

    public sealed class WorkflowAsset
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Mime { get; set; } = "application/octet-stream";
        public string Kind { get; set; } = "other";
        public long LastModified { get; set; }
        public string FilePath { get; set; } = string.Empty;

        [JsonIgnore]
        public bool HasFile => !string.IsNullOrWhiteSpace(FilePath);

        [JsonIgnore]
        public bool FileExists => HasFile && File.Exists(FilePath);
    }

    public sealed class WorkflowDocument
    {
        public int Version { get; set; } = 1;
        public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
        public string ProjectName { get; set; } = "新项目";
        public ProjectWorkspaceMode ProjectMode { get; set; } = ProjectWorkspaceMode.AiAnimeProject;
        public List<WorkflowNode> Nodes { get; set; } = new();
        public List<WorkflowEdge> Edges { get; set; } = new();
        public List<WorkflowAsset> Assets { get; set; } = new();

        public static WorkflowDocument CreateEmpty(string? projectName = null, ProjectWorkspaceMode mode = ProjectWorkspaceMode.AiAnimeProject)
        {
            return new WorkflowDocument
            {
                ProjectName = string.IsNullOrWhiteSpace(projectName) ? "新项目" : projectName.Trim(),
                ProjectMode = mode,
                ExportedAt = DateTime.UtcNow,
            };
        }

        public string CreateNextNodeId()
        {
            var max = Nodes
                .Select(node => new string((node.Id ?? string.Empty).Where(char.IsDigit).ToArray()))
                .Select(text => int.TryParse(text, out var value) ? value : 0)
                .DefaultIfEmpty(0)
                .Max();

            return $"node_{max + 1}";
        }
    }

    public sealed class WorkflowNodeEventArgs : EventArgs
    {
        public WorkflowNodeEventArgs(WorkflowNode? node)
        {
            Node = node;
        }

        public WorkflowNode? Node { get; }
    }

    public enum WorkflowPortKind
    {
        Input,
        Output,
    }

    public sealed class WorkflowPortEventArgs : EventArgs
    {
        public WorkflowPortEventArgs(WorkflowNode node, WorkflowPortKind portKind)
        {
            Node = node;
            PortKind = portKind;
        }

        public WorkflowNode Node { get; }

        public WorkflowPortKind PortKind { get; }
    }

    public enum CharacterDesignRoleType
    {
        Main,
        Supporting,
    }

    public static class CharacterDesignRoleTypeExtensions
    {
        public static CharacterDesignRoleType Parse(string? value)
        {
            return value?.Contains("配", StringComparison.Ordinal) == true
                ? CharacterDesignRoleType.Supporting
                : CharacterDesignRoleType.Main;
        }

        public static string ToLabel(this CharacterDesignRoleType value)
        {
            return value switch
            {
                CharacterDesignRoleType.Supporting => "配角（轻量）",
                _ => "主角（完整）",
            };
        }
    }

    public static class StoryboardShotCatalog
    {
        public static readonly IReadOnlyList<string> ShotSizes = new[]
        {
            "大远景",
            "远景",
            "全景",
            "中景",
            "中近景",
            "近景",
            "特写",
            "大特写",
        };

        public static readonly IReadOnlyList<string> CameraAngles = new[]
        {
            "平视",
            "高位俯拍",
            "低位仰拍",
            "斜拍",
            "越肩",
            "鸟瞰",
        };

        public static readonly IReadOnlyList<string> CameraMovements = new[]
        {
            "固定",
            "横移",
            "俯仰",
            "摇移",
            "升降",
            "轨道推拉",
            "变焦推拉",
            "正跟随",
            "倒跟随",
            "环绕",
            "滑轨横移",
        };
    }

    public enum CharacterAssetStatus
    {
        Pending,
        Generating,
        Success,
        Failed,
    }

    public enum CharacterDesignActionType
    {
        GenerateProfile,
        GenerateExpression,
        GenerateThreeView,
    }

    public sealed class WorkflowCharacterActionEventArgs : EventArgs
    {
        public WorkflowCharacterActionEventArgs(WorkflowNode node, string characterName, CharacterDesignActionType action)
        {
            Node = node;
            CharacterName = characterName;
            Action = action;
        }

        public WorkflowNode Node { get; }

        public string CharacterName { get; }

        public CharacterDesignActionType Action { get; }
    }

    public sealed class WorkflowNodeActionEventArgs : EventArgs
    {
        public WorkflowNodeActionEventArgs(WorkflowNode node, string action)
        {
            Node = node;
            Action = action;
        }

        public WorkflowNode Node { get; }

        public string Action { get; }
    }

    public sealed class WorkflowStatusEventArgs : EventArgs
    {
        public WorkflowStatusEventArgs(string message, Color color)
        {
            Message = message;
            Color = color;
        }

        public string Message { get; }

        public Color Color { get; }
    }
}
