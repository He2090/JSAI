using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JSAI.WinApp
{
    public static class WorkflowStoryboardParser
    {
        public static List<StoryboardShot> ParseStoryboardShots(string text)
        {
            var normalized = WorkflowResultParser.NormalizeTextResult(WorkflowNodeCatalog.StoryboardBreakdown, text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return new List<StoryboardShot>();
            }

            try
            {
                using var document = JsonDocument.Parse(normalized);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("shots", out var shotsElement) &&
                    shotsElement.ValueKind == JsonValueKind.Array)
                {
                    return ParseStoryboardShotArray(shotsElement);
                }

                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return ParseStoryboardShotArray(document.RootElement);
                }
            }
            catch
            {
                // Fall back to text heuristics.
            }

            return ExtractStoryboardShotsFromText(normalized);
        }

        public static List<StoryboardShot> CollectStoryboardShots(IEnumerable<WorkflowNode> upstreamNodes, WorkflowNode node, int limit = 48)
        {
            var shots = new List<StoryboardShot>();
            var upstreamList = upstreamNodes?.ToList() ?? new List<WorkflowNode>();

            foreach (var upstream in upstreamList.Where(candidate => candidate.Type == WorkflowNodeCatalog.StoryboardBreakdown))
            {
                upstream.Params ??= new WorkflowNodeParameters();
                upstream.Params.EnsureDefaults(upstream.Type);
                if (upstream.Params.StoryboardShots.Count > 0)
                {
                    shots.AddRange(upstream.Params.StoryboardShots.Select(shot => shot.Clone()));
                    continue;
                }

                shots.AddRange(ParseStoryboardShots(upstream.Output));
            }

            if (shots.Count == 0)
            {
                foreach (var upstream in upstreamList.Where(candidate => candidate.Type == WorkflowNodeCatalog.CreativeDescription))
                {
                    if (upstream.Params?.StoryboardShots != null && upstream.Params.StoryboardShots.Count > 0)
                    {
                        shots.AddRange(upstream.Params.StoryboardShots.Select(shot => shot.Clone()));
                        continue;
                    }

                    shots.AddRange(ExtractStoryboardShotsFromText(upstream.Output, perCreativeDescription: true));
                }
            }

            if (shots.Count == 0 && !string.IsNullOrWhiteSpace(node.Params?.Input))
            {
                shots.AddRange(ExtractStoryboardShotsFromText(node.Params.Input, perCreativeDescription: true));
            }

            return NormalizeStoryboardShots(shots)
                .Take(Math.Max(1, limit))
                .ToList();
        }

        public static int GetStoryboardShotsPerPage(string? gridLayout)
        {
            return string.Equals(gridLayout, "2x3", StringComparison.Ordinal) ? 6 : 9;
        }

        public static List<StoryboardShot> NormalizeStoryboardShots(IEnumerable<StoryboardShot> shots)
        {
            var normalized = new List<StoryboardShot>();
            var currentTime = 0;
            foreach (var shot in shots ?? Enumerable.Empty<StoryboardShot>())
            {
                if (shot == null)
                {
                    continue;
                }

                var clone = shot.Clone();
                clone.Id = string.IsNullOrWhiteSpace(clone.Id) ? Guid.NewGuid().ToString("N") : clone.Id.Trim();
                clone.DurationSeconds = Math.Max(1, clone.DurationSeconds);
                clone.Scene = string.IsNullOrWhiteSpace(clone.Scene)
                    ? $"分镜 #{normalized.Count + 1}"
                    : clone.Scene.Trim();
                clone.VisualDescription = string.IsNullOrWhiteSpace(clone.VisualDescription)
                    ? clone.Scene
                    : clone.VisualDescription.Trim();
                clone.ImagePrompt = string.IsNullOrWhiteSpace(clone.ImagePrompt) ? string.Empty : clone.ImagePrompt.Trim();
                clone.Dialogue = string.IsNullOrWhiteSpace(clone.Dialogue) ? "无" : clone.Dialogue.Trim();
                clone.VisualEffects = string.IsNullOrWhiteSpace(clone.VisualEffects) ? "无" : clone.VisualEffects.Trim();
                clone.AudioEffects = string.IsNullOrWhiteSpace(clone.AudioEffects) ? "无" : clone.AudioEffects.Trim();
                clone.ShotSize = NormalizeShotSize(clone.ShotSize);
                clone.CameraAngle = NormalizeCameraAngle(clone.CameraAngle);
                clone.CameraMovement = NormalizeCameraMovement(clone.CameraMovement);
                clone.Characters ??= new List<string>();
                clone.Characters = clone.Characters
                    .Select(value => WorkflowParseHelpers.CleanExtractedValue(value))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                clone.SplitImagePath = string.IsNullOrWhiteSpace(clone.SplitImagePath) ? string.Empty : clone.SplitImagePath.Trim();
                clone.SourceNodeId = string.IsNullOrWhiteSpace(clone.SourceNodeId) ? string.Empty : clone.SourceNodeId.Trim();
                clone.SourcePage = Math.Max(0, clone.SourcePage);
                clone.PanelIndex = Math.Max(0, clone.PanelIndex);
                clone.ShotNumber = clone.ShotNumber > 0 ? clone.ShotNumber : normalized.Count + 1;
                if (clone.StartTime < 0)
                {
                    clone.StartTime = 0;
                }

                if (clone.EndTime <= clone.StartTime)
                {
                    clone.StartTime = currentTime;
                    clone.EndTime = currentTime + clone.DurationSeconds;
                }

                currentTime = clone.EndTime;
                normalized.Add(clone);
            }

            for (var index = 0; index < normalized.Count; index++)
            {
                normalized[index].ShotNumber = index + 1;
            }

            return normalized;
        }

        private static List<StoryboardShot> ParseStoryboardShotArray(JsonElement arrayElement)
        {
            var result = new List<StoryboardShot>();
            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var shot = new StoryboardShot
                {
                    Id = WorkflowParseHelpers.FirstNonEmpty(WorkflowParseHelpers.ReadJsonString(item, "id"), $"shot_{result.Count + 1}"),
                    ShotNumber = WorkflowParseHelpers.ReadJsonInt(item, "shotNumber", result.Count + 1),
                    DurationSeconds = Math.Max(1, WorkflowParseHelpers.ReadJsonInt(item, "duration", WorkflowParseHelpers.ReadJsonInt(item, "durationSeconds", 3))),
                    Scene = WorkflowParseHelpers.FirstNonEmpty(
                        WorkflowParseHelpers.ReadJsonString(item, "scene"),
                        WorkflowParseHelpers.ReadJsonString(item, "shotTitle"),
                        WorkflowParseHelpers.ReadJsonString(item, "title")),
                    Characters = WorkflowParseHelpers.ReadJsonStringList(item, "characters", "characterBreakdown", "roles"),
                    ShotSize = WorkflowParseHelpers.FirstNonEmpty(WorkflowParseHelpers.ReadJsonString(item, "shotSize"), WorkflowParseHelpers.ReadJsonString(item, "shotType"), "中景"),
                    CameraAngle = WorkflowParseHelpers.FirstNonEmpty(WorkflowParseHelpers.ReadJsonString(item, "cameraAngle"), "平视"),
                    CameraMovement = WorkflowParseHelpers.FirstNonEmpty(WorkflowParseHelpers.ReadJsonString(item, "cameraMovement"), "固定"),
                    VisualDescription = WorkflowParseHelpers.FirstNonEmpty(
                        WorkflowParseHelpers.ReadJsonString(item, "visualDescription"),
                        WorkflowParseHelpers.ReadJsonString(item, "description"),
                        WorkflowParseHelpers.ReadJsonString(item, "content"),
                        WorkflowParseHelpers.ReadJsonString(item, "action")),
                    ImagePrompt = WorkflowParseHelpers.FirstNonEmpty(
                        WorkflowParseHelpers.ReadJsonString(item, "imagePrompt"),
                        WorkflowParseHelpers.ReadJsonString(item, "englishVisualPrompt"),
                        WorkflowParseHelpers.ReadJsonString(item, "visualPromptForImage"),
                        WorkflowParseHelpers.ReadJsonString(item, "prompt")),
                    Dialogue = WorkflowParseHelpers.FirstNonEmpty(WorkflowParseHelpers.ReadJsonString(item, "dialogue"), "无"),
                    VisualEffects = WorkflowParseHelpers.FirstNonEmpty(WorkflowParseHelpers.ReadJsonString(item, "visualEffects"), "无"),
                    AudioEffects = WorkflowParseHelpers.FirstNonEmpty(WorkflowParseHelpers.ReadJsonString(item, "audioEffects"), "无"),
                    StartTime = WorkflowParseHelpers.ReadJsonInt(item, "startTime", 0),
                    EndTime = WorkflowParseHelpers.ReadJsonInt(item, "endTime", 0),
                    SplitImagePath = WorkflowParseHelpers.ReadJsonString(item, "splitImagePath"),
                    SourceNodeId = WorkflowParseHelpers.ReadJsonString(item, "sourceNodeId"),
                    SourcePage = WorkflowParseHelpers.ReadJsonInt(item, "sourcePage", 0),
                    PanelIndex = WorkflowParseHelpers.ReadJsonInt(item, "panelIndex", 0),
                };
                result.Add(shot);
            }

            return NormalizeStoryboardShots(result);
        }

        private static List<StoryboardShot> ExtractStoryboardShotsFromText(string text, bool perCreativeDescription = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<StoryboardShot>();
            }

            var normalized = WorkflowResultParser.NormalizeTextResult(WorkflowNodeCatalog.StoryboardBreakdown, text);
            var sectionShots = ExtractCreativeDescriptionShots(normalized);
            if (sectionShots.Count > 0)
            {
                return NormalizeStoryboardShots(sectionShots);
            }

            var numberedMatches = Regex.Matches(normalized, @"(?im)^(?<index>\d+)[\.、\)]\s*(?<content>.+)$");
            if (numberedMatches.Count > 0)
            {
                return NormalizeStoryboardShots(numberedMatches
                    .Cast<Match>()
                    .Select(match => new StoryboardShot
                    {
                        ShotNumber = int.TryParse(match.Groups["index"].Value, out var value) ? value : 0,
                        Scene = $"分镜 #{match.Groups["index"].Value}",
                        VisualDescription = WorkflowParseHelpers.CleanExtractedValue(match.Groups["content"].Value),
                        ImagePrompt = WorkflowParseHelpers.CleanExtractedValue(match.Groups["content"].Value),
                    })
                    .ToList());
            }

            var paragraphs = normalized
                .Split(new[] { $"{Environment.NewLine}{Environment.NewLine}", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 12)
                .ToList();

            if (paragraphs.Count == 0)
            {
                return new List<StoryboardShot>();
            }

            return NormalizeStoryboardShots(paragraphs
                .Take(perCreativeDescription ? 6 : 9)
                .Select((paragraph, index) => new StoryboardShot
                {
                    ShotNumber = index + 1,
                    Scene = $"分镜 #{index + 1}",
                    VisualDescription = paragraph,
                    ImagePrompt = paragraph,
                })
                .ToList());
        }

        private static string NormalizeShotSize(string? value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (StoryboardShotCatalog.ShotSizes.Contains(normalized))
            {
                return normalized;
            }

            return normalized.ToLowerInvariant() switch
            {
                "extreme long shot" or "wide establishing shot" => "大远景",
                "long shot" or "wide shot" => "远景",
                "full shot" or "full body shot" => "全景",
                "medium shot" => "中景",
                "medium close-up" or "medium close up" => "中近景",
                "close-up" or "close up" or "close shot" => "近景",
                "extreme close-up" or "extreme close up" or "macro shot" => "大特写",
                _ => "中景",
            };
        }

        private static string NormalizeCameraAngle(string? value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (StoryboardShotCatalog.CameraAngles.Contains(normalized))
            {
                return normalized;
            }

            return normalized.ToLowerInvariant() switch
            {
                "high angle" or "high angle shot" => "高位俯拍",
                "low angle" or "low angle shot" => "低位仰拍",
                "dutch angle" => "斜拍",
                "over-the-shoulder" or "over the shoulder" or "over-the-shoulder shot" => "越肩",
                "bird's eye" or "bird's eye view" or "bird eye view" => "鸟瞰",
                _ => "平视",
            };
        }

        private static string NormalizeCameraMovement(string? value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (StoryboardShotCatalog.CameraMovements.Contains(normalized))
            {
                return normalized;
            }

            return normalized.ToLowerInvariant() switch
            {
                "static" or "locked-off" or "locked off" => "固定",
                "truck" or "lateral" or "lateral tracking" => "横移",
                "tilt" => "俯仰",
                "pan" => "摇移",
                "pedestal" or "crane" => "升降",
                "dolly" or "push" or "pull" or "dolly push pull" => "轨道推拉",
                "zoom" => "变焦推拉",
                "tracking forward" or "follow" => "正跟随",
                "tracking backward" or "tracking back" => "倒跟随",
                "arc" or "orbit" => "环绕",
                "slider" => "滑轨横移",
                _ => "固定",
            };
        }

        private static List<StoryboardShot> ExtractCreativeDescriptionShots(string text)
        {
            var normalized = WorkflowResultParser.NormalizeTextResult(WorkflowNodeCatalog.CreativeDescription, text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return new List<StoryboardShot>();
            }

            var episodeTitleMatch = Regex.Match(normalized, @"(?im)^##\s*(?<title>[^\r\n]+)$");
            var episodeTitle = episodeTitleMatch.Success
                ? WorkflowParseHelpers.CleanExtractedValue(episodeTitleMatch.Groups["title"].Value)
                : "创意分镜";
            var characters = ExtractCreativeDescriptionCharacters(normalized);

            var sceneDescription = ExtractBracketSection(normalized, "场景描述");
            var interaction = ExtractBracketSection(normalized, "角色互动");
            var actionConflict = ExtractBracketSection(normalized, "动作与冲突");
            var dialogue = ExtractBracketSection(normalized, "对白");
            var suspense = ExtractBracketSection(normalized, "悬念");
            var continuity = WorkflowParseHelpers.ExtractMarkdownField(normalized, "连贯性说明");

            var shots = new List<StoryboardShot>();

            void AddShot(string sceneSuffix, string visualDescription, string shotSize, string cameraAngle, string cameraMovement, string dialogueText = "", string visualEffects = "", string audioEffects = "")
            {
                if (string.IsNullOrWhiteSpace(visualDescription))
                {
                    return;
                }

                shots.Add(new StoryboardShot
                {
                    Scene = $"{episodeTitle} - {sceneSuffix}",
                    Characters = new List<string>(characters),
                    DurationSeconds = 3,
                    ShotSize = shotSize,
                    CameraAngle = cameraAngle,
                    CameraMovement = cameraMovement,
                    VisualDescription = visualDescription.Trim(),
                    ImagePrompt = visualDescription.Trim(),
                    Dialogue = string.IsNullOrWhiteSpace(dialogueText) ? "无" : dialogueText.Trim(),
                    VisualEffects = string.IsNullOrWhiteSpace(visualEffects) ? "无" : visualEffects.Trim(),
                    AudioEffects = string.IsNullOrWhiteSpace(audioEffects) ? "无" : audioEffects.Trim(),
                });
            }

            AddShot("俯瞰全景", sceneDescription, "大远景", "鸟瞰", "固定");
            AddShot("角色关系建立", interaction, "中景", "平视", "横移");
            AddShot("冲突推进", actionConflict, "中近景", "低位仰拍", "轨道推拉");
            AddShot("对白重点", string.IsNullOrWhiteSpace(dialogue) ? interaction : dialogue, "近景", "越肩", "固定", dialogue);
            AddShot("情绪悬念", suspense, "特写", "斜拍", "变焦推拉", dialogue, continuity, continuity);

            return NormalizeStoryboardShots(shots);
        }

        private static List<string> ExtractCreativeDescriptionCharacters(string text)
        {
            var roleText = WorkflowParseHelpers.ExtractMarkdownField(text, "角色");
            if (string.IsNullOrWhiteSpace(roleText))
            {
                return new List<string>();
            }

            return Regex.Split(roleText, @"[、,，/｜\| ]+")
                .Select(value => WorkflowParseHelpers.CleanExtractedValue(value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ExtractBracketSection(string text, string sectionName)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(sectionName))
            {
                return string.Empty;
            }

            var match = Regex.Match(
                text,
                $@"(?ims)^[【\[]\s*{Regex.Escape(sectionName)}\s*[】\]]\s*$\n(?<body>.*?)(?=^[【\[]\s*[^\r\n\]\】]+\s*[】\]]\s*$|\z)");
            return match.Success
                ? WorkflowParseHelpers.CleanExtractedValue(match.Groups["body"].Value)
                : string.Empty;
        }
    }
}
