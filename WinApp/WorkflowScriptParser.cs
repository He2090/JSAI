using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JSAI.WinApp
{
    public static class WorkflowScriptParser
    {
        public static List<GeneratedScriptEpisode> ParseGeneratedScriptEpisodes(string text)
        {
            var normalized = WorkflowResultParser.NormalizeTextResult(WorkflowNodeCatalog.Script, text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return new List<GeneratedScriptEpisode>();
            }

            try
            {
                using var document = JsonDocument.Parse(normalized);
                var episodeArray = ResolveScriptEpisodeArray(document.RootElement);
                if (episodeArray == null)
                {
                    return ParseMarkdownGeneratedScriptEpisodes(normalized);
                }

                return episodeArray.Value
                    .EnumerateArray()
                    .Where(element => element.ValueKind == JsonValueKind.Object)
                    .Select(element => new GeneratedScriptEpisode
                    {
                        Title = WorkflowParseHelpers.ReadJsonString(element, "title"),
                        Content = NormalizeEpisodeContent(
                            WorkflowParseHelpers.ReadJsonString(element, "content"),
                            WorkflowParseHelpers.ReadJsonString(element, "title"),
                            WorkflowParseHelpers.ReadJsonString(element, "characters"),
                            WorkflowParseHelpers.ReadJsonString(element, "keyItems"),
                            WorkflowParseHelpers.ReadJsonString(element, "continuityNote")),
                        Characters = WorkflowParseHelpers.ReadJsonString(element, "characters"),
                        KeyItems = WorkflowParseHelpers.ReadJsonString(element, "keyItems"),
                        VisualStyleNote = WorkflowParseHelpers.ReadJsonString(element, "visualStyleNote"),
                        ContinuityNote = WorkflowParseHelpers.ReadJsonString(element, "continuityNote"),
                    })
                    .Where(episode => !string.IsNullOrWhiteSpace(episode.Title) || !string.IsNullOrWhiteSpace(episode.Content))
                    .ToList();
            }
            catch
            {
                return ParseMarkdownGeneratedScriptEpisodes(normalized);
            }
        }

        public static List<GeneratedScriptEpisode> NormalizeGeneratedScriptEpisodeCount(
            WorkflowNode node,
            string outlineInput,
            IReadOnlyList<GeneratedScriptEpisode> episodes)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var chapters = ExtractOutlineChapters(outlineInput, 24);
            var selectedChapter = ResolveScriptChapterSelection(node, chapters);
            var targetCount = Math.Max(1, ResolveScriptEpisodeCount(node, selectedChapter));
            var normalized = (episodes ?? Array.Empty<GeneratedScriptEpisode>())
                .Where(episode => !string.IsNullOrWhiteSpace(episode.Title) || !string.IsNullOrWhiteSpace(episode.Content))
                .Take(targetCount)
                .ToList();

            for (var index = normalized.Count; index < targetCount; index++)
            {
                normalized.Add(BuildFallbackGeneratedScriptEpisode(outlineInput, selectedChapter, index));
            }

            return normalized;
        }

        public static string BuildScriptEpisodesOutput(IEnumerable<GeneratedScriptEpisode> episodes)
        {
            var builder = new StringBuilder();
            foreach (var episode in episodes ?? Enumerable.Empty<GeneratedScriptEpisode>())
            {
                if (!string.IsNullOrWhiteSpace(episode.Content))
                {
                    builder.AppendLine(episode.Content.Trim());
                }
                else
                {
                    builder.AppendLine($"## {episode.DisplayTitle}");
                    if (!string.IsNullOrWhiteSpace(episode.Characters))
                    {
                        builder.AppendLine();
                        builder.AppendLine($"**角色**: {episode.Characters}");
                    }

                    if (!string.IsNullOrWhiteSpace(episode.KeyItems))
                    {
                        builder.AppendLine($"**关键物品**: {episode.KeyItems}");
                    }

                    if (!string.IsNullOrWhiteSpace(episode.ContinuityNote))
                    {
                        builder.AppendLine();
                        builder.AppendLine($"**连贯性说明**: {episode.ContinuityNote}");
                    }
                }

                builder.AppendLine();
                builder.AppendLine("---");
                builder.AppendLine();
            }

            return WorkflowResultParser.NormalizeTextResult(WorkflowNodeCatalog.Script, builder.ToString());
        }

        public static List<OutlineChapterPlan> ExtractOutlineChapters(string text, int limit = 24, int totalEpisodes = 0)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<OutlineChapterPlan>();
            }

            var matches = Regex.Matches(
                text,
                @"(?im)^#{2,4}\s*(?<label>第[一二三四五六七八九十百零\d]+章|最终章)\s*[：:]\s*(?<name>[^\r\n（(]+?)\s*[（(]第\s*(?<start>\d+)\s*-\s*(?<end>\d+)\s*集[）)]");

            var chapters = matches
                .Cast<Match>()
                .Select(match => new OutlineChapterPlan
                {
                    ChapterLabel = WorkflowParseHelpers.CleanExtractedValue(match.Groups["label"].Value),
                    ChapterName = WorkflowParseHelpers.CleanExtractedValue(match.Groups["name"].Value),
                    StartEpisode = int.TryParse(match.Groups["start"].Value, out var startEpisode) ? startEpisode : 0,
                    EndEpisode = int.TryParse(match.Groups["end"].Value, out var endEpisode) ? endEpisode : 0,
                })
                .Where(plan => !string.IsNullOrWhiteSpace(plan.Title))
                .DistinctBy(plan => plan.DisplayText)
                .Take(Math.Max(1, limit))
                .ToList();

            if (chapters.Count > 0)
            {
                return CompleteOutlineChapterRanges(text, chapters, limit, totalEpisodes);
            }

            chapters = ExtractStructuredChapterPlans(text, Math.Max(1, limit));
            if (chapters.Count > 0)
            {
                return CompleteOutlineChapterRanges(text, chapters, limit, totalEpisodes);
            }

            chapters = ExtractChapterTitles(text, Math.Max(1, limit))
                .Select(title => new OutlineChapterPlan
                {
                    ChapterLabel = title,
                    ChapterName = string.Empty,
                    StartEpisode = 0,
                    EndEpisode = 0,
                })
                .ToList();

            return CompleteOutlineChapterRanges(text, chapters, limit, totalEpisodes);
        }

        public static OutlineChapterPlan? ResolveScriptChapterSelection(WorkflowNode node, IReadOnlyList<OutlineChapterPlan> chapters)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            if (!string.IsNullOrWhiteSpace(node.Params.ScriptSourceChapter))
            {
                var selected = chapters.FirstOrDefault(plan =>
                    string.Equals(plan.DisplayText, node.Params.ScriptSourceChapter, StringComparison.Ordinal) ||
                    string.Equals(plan.Title, node.Params.ScriptSourceChapter, StringComparison.Ordinal) ||
                    string.Equals(plan.ChapterLabel, node.Params.ScriptSourceChapter, StringComparison.Ordinal));

                if (selected != null)
                {
                    return selected;
                }

                if (TryParseChapterPlan(node.Params.ScriptSourceChapter, out var parsedSelection))
                {
                    return parsedSelection;
                }
            }

            if (chapters.Count == 0)
            {
                return null;
            }

            return chapters[0];
        }

        public static int ResolveScriptEpisodeCount(WorkflowNode node, OutlineChapterPlan? selectedChapter)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var requestedCount = node.Params.ScriptEpisodesToGenerate;
            if (selectedChapter == null || selectedChapter.EpisodeCount <= 1)
            {
                return Math.Max(1, requestedCount);
            }

            if (requestedCount <= 0)
            {
                return selectedChapter.EpisodeCount;
            }

            if (requestedCount == 1 && string.IsNullOrWhiteSpace(node.Params.ScriptSourceChapter))
            {
                return selectedChapter.EpisodeCount;
            }

            return Math.Max(1, Math.Min(selectedChapter.EpisodeCount, requestedCount));
        }

        public static string BuildScriptTargetEpisodeRange(OutlineChapterPlan? selectedChapter, int episodeCount)
        {
            if (selectedChapter == null || selectedChapter.StartEpisode <= 0 || selectedChapter.EndEpisode < selectedChapter.StartEpisode)
            {
                return $"共 {Math.Max(1, episodeCount)} 集";
            }

            var safeCount = Math.Max(1, Math.Min(selectedChapter.EpisodeCount, episodeCount));
            var rangeEnd = selectedChapter.StartEpisode + safeCount - 1;
            return $"第{selectedChapter.StartEpisode}-{rangeEnd}集";
        }

        private static JsonElement? ResolveScriptEpisodeArray(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                return root;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var propertyName in new[] { "episodes", "items", "scripts", "data", "result" })
            {
                if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array)
                {
                    return value;
                }
            }

            return null;
        }

        private static List<GeneratedScriptEpisode> ParseMarkdownGeneratedScriptEpisodes(string normalized)
        {
            var matches = Regex.Matches(
                normalized,
                @"(?ims)^##\s*(?<title>第\s*\d+\s*集\s*[：:][^\r\n]+)\s*(?<body>.*?)(?=^##\s*第\s*\d+\s*集\s*[：:]|\z)");

            if (matches.Count == 0)
            {
                return new List<GeneratedScriptEpisode>();
            }

            return matches
                .Cast<Match>()
                .Select(match =>
                {
                    var title = WorkflowParseHelpers.CleanExtractedValue(match.Groups["title"].Value);
                    var body = match.Groups["body"].Value.Trim();
                    var content = WorkflowResultParser.NormalizeTextResult(
                        WorkflowNodeCatalog.Script,
                        $"## {title}{Environment.NewLine}{Environment.NewLine}{body}");
                    return new GeneratedScriptEpisode
                    {
                        Title = title,
                        Content = content,
                        Characters = WorkflowParseHelpers.ExtractMarkdownField(content, "角色"),
                        KeyItems = WorkflowParseHelpers.ExtractMarkdownField(content, "关键物品"),
                        ContinuityNote = WorkflowParseHelpers.ExtractMarkdownField(content, "连贯性说明"),
                    };
                })
                .Where(episode => !string.IsNullOrWhiteSpace(episode.Content))
                .ToList();
        }

        private static GeneratedScriptEpisode BuildFallbackGeneratedScriptEpisode(
            string outlineInput,
            OutlineChapterPlan? selectedChapter,
            int zeroBasedIndex)
        {
            var episodeNumber = selectedChapter?.StartEpisode > 0
                ? selectedChapter.StartEpisode + zeroBasedIndex
                : zeroBasedIndex + 1;
            var chapterTitle = selectedChapter?.Title.OrDefault("当前章节") ?? "当前章节";
            var characters = WorkflowParseHelpers.ExtractMarkdownField(outlineInput, "涉及角色", "角色");
            var keyItems = WorkflowParseHelpers.ExtractMarkdownField(outlineInput, "关键物品");
            var title = $"第{episodeNumber}集：{chapterTitle}推进";

            var builder = new StringBuilder();
            builder.AppendLine($"## {title}");
            builder.AppendLine();
            builder.AppendLine($"**角色**: {characters.OrDefault("按故事大纲角色清单延续")}");
            builder.AppendLine($"**关键物品**: {keyItems.OrDefault("按故事大纲关键物品延续")}");
            builder.AppendLine();
            builder.AppendLine("【场景描述】");
            builder.AppendLine($"围绕“{chapterTitle}”继续推进，场景承接本章主冲突，保持大纲中的类型、背景和视觉风格。");
            builder.AppendLine("【角色互动】");
            builder.AppendLine("核心角色围绕当前目标产生新的信息差，关系进一步紧张。");
            builder.AppendLine("【动作与冲突】");
            builder.AppendLine("本集安排一次明确的行动阻碍，让主角必须做出选择。");
            builder.AppendLine("【对白】");
            builder.AppendLine("角色用简短对白揭示压力、目标和隐藏线索。");
            builder.AppendLine("【悬念】");
            builder.AppendLine("结尾抛出新的线索或危机，为下一集留下钩子。");
            builder.AppendLine();
            builder.AppendLine("**连贯性说明**: 这是根据当前章节和拆分集数自动补齐的分集骨架，可继续重新生成或手动细化。");

            return new GeneratedScriptEpisode
            {
                Title = title,
                Content = WorkflowResultParser.NormalizeTextResult(WorkflowNodeCatalog.Script, builder.ToString()),
                Characters = characters,
                KeyItems = keyItems,
                ContinuityNote = "根据当前章节和拆分集数自动补齐，可继续细化。",
            };
        }

        private static List<string> ExtractChapterTitles(string text, int limit)
        {
            var titles = Regex.Matches(text, @"(?im)^#{2,4}\s*(第[一二三四五六七八九十百零\d]+章[：:][^\r\n]+|最终章[：:][^\r\n]+)$")
                .Cast<Match>()
                .Select(match => WorkflowParseHelpers.CleanExtractedValue(match.Groups[1].Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Take(limit)
                .ToList();

            if (titles.Count >= limit)
            {
                return titles;
            }

            foreach (var chapter in ExtractStructuredChapterPlans(text, limit))
            {
                if (titles.Count >= limit)
                {
                    break;
                }

                if (!titles.Contains(chapter.Title, StringComparer.Ordinal))
                {
                    titles.Add(chapter.Title);
                }
            }

            return titles;
        }

        private static List<OutlineChapterPlan> ExtractStructuredChapterPlans(string text, int limit)
        {
            var section = WorkflowParseHelpers.ExtractMarkdownSection(
                text,
                new[] { "## 章节规划", "## 剧集结构规划" },
                "## 分集规划");
            if (string.IsNullOrWhiteSpace(section))
            {
                return new List<OutlineChapterPlan>();
            }

            var matches = Regex.Matches(
                section,
                @"(?ims)^###\s*(?<label>章节\s*\d+|第[一二三四五六七八九十百零\d]+章|最终章)\s*$\n(?<body>.*?)(?=^###\s*(?:章节\s*\d+|第[一二三四五六七八九十百零\d]+章|最终章)\s*$|\z)");

            return matches
                .Cast<Match>()
                .Select(match =>
                {
                    var body = match.Groups["body"].Value;
                    var label = NormalizeChapterLabel(match.Groups["label"].Value);
                    var title = WorkflowParseHelpers.ExtractMarkdownField(body, "标题", "章节标题");
                    var rangeText = WorkflowParseHelpers.ExtractMarkdownField(body, "集数范围", "章节范围");
                    if (!TryExtractEpisodeRange(rangeText, out var startEpisode, out var endEpisode))
                    {
                        TryExtractEpisodeRange(body, out startEpisode, out endEpisode);
                    }

                    return new OutlineChapterPlan
                    {
                        ChapterLabel = label,
                        ChapterName = string.IsNullOrWhiteSpace(title) ? string.Empty : title,
                        StartEpisode = startEpisode,
                        EndEpisode = endEpisode,
                    };
                })
                .Where(plan => !string.IsNullOrWhiteSpace(plan.Title))
                .DistinctBy(plan => plan.DisplayText)
                .Take(Math.Max(1, limit))
                .ToList();
        }

        private static List<OutlineChapterPlan> CompleteOutlineChapterRanges(
            string text,
            List<OutlineChapterPlan> chapters,
            int limit,
            int totalEpisodesFallback)
        {
            var safeLimit = Math.Max(1, limit);
            var totalEpisodes = ResolveTotalEpisodes(text, totalEpisodesFallback);
            if (chapters.Count == 0 || totalEpisodes <= 0)
            {
                return chapters.Take(safeLimit).ToList();
            }

            var rangedChapters = chapters
                .Where(plan => plan.StartEpisode > 0 && plan.EndEpisode >= plan.StartEpisode)
                .OrderBy(plan => plan.StartEpisode)
                .ThenBy(plan => plan.EndEpisode)
                .ToList();

            if (rangedChapters.Count == 0)
            {
                return AssignEvenEpisodeRanges(chapters, totalEpisodes, safeLimit);
            }

            var completed = new List<OutlineChapterPlan>();
            var nextEpisode = 1;
            var typicalSpan = ResolveTypicalChapterSpan(rangedChapters);

            foreach (var chapter in rangedChapters)
            {
                if (completed.Count >= safeLimit)
                {
                    break;
                }

                if (chapter.StartEpisode > nextEpisode)
                {
                    AddFallbackChapterChunks(completed, nextEpisode, chapter.StartEpisode - 1, typicalSpan, totalEpisodes, safeLimit);
                }

                if (completed.Count >= safeLimit)
                {
                    break;
                }

                if (chapter.EndEpisode >= nextEpisode)
                {
                    completed.Add(chapter);
                    nextEpisode = Math.Max(nextEpisode, chapter.EndEpisode + 1);
                }
            }

            if (nextEpisode <= totalEpisodes && completed.Count < safeLimit)
            {
                AddFallbackChapterChunks(completed, nextEpisode, totalEpisodes, typicalSpan, totalEpisodes, safeLimit);
            }

            return completed.Count == 0
                ? chapters.Take(safeLimit).ToList()
                : completed.Take(safeLimit).ToList();
        }

        private static List<OutlineChapterPlan> AssignEvenEpisodeRanges(List<OutlineChapterPlan> chapters, int totalEpisodes, int limit)
        {
            var selected = chapters.Take(Math.Max(1, limit)).ToList();
            if (selected.Count == 0 || totalEpisodes <= 0)
            {
                return selected;
            }

            var span = Math.Max(1, (int)Math.Ceiling(totalEpisodes / (double)selected.Count));
            for (var index = 0; index < selected.Count; index++)
            {
                var start = index * span + 1;
                var end = index == selected.Count - 1 ? totalEpisodes : Math.Min(totalEpisodes, start + span - 1);
                if (start > totalEpisodes)
                {
                    break;
                }

                selected[index].StartEpisode = start;
                selected[index].EndEpisode = end;
            }

            return selected;
        }

        private static void AddFallbackChapterChunks(
            List<OutlineChapterPlan> chapters,
            int startEpisode,
            int endEpisode,
            int span,
            int totalEpisodes,
            int limit)
        {
            var safeSpan = Math.Max(1, span);
            var currentStart = Math.Max(1, startEpisode);
            var safeEnd = Math.Max(currentStart, endEpisode);
            while (currentStart <= safeEnd && chapters.Count < limit)
            {
                var currentEnd = Math.Min(safeEnd, currentStart + safeSpan - 1);
                chapters.Add(BuildFallbackChapterPlan(chapters.Count + 1, currentStart, currentEnd, totalEpisodes));
                currentStart = currentEnd + 1;
            }
        }

        private static OutlineChapterPlan BuildFallbackChapterPlan(int chapterNumber, int startEpisode, int endEpisode, int totalEpisodes)
        {
            return new OutlineChapterPlan
            {
                ChapterLabel = $"第{ToChineseNumber(chapterNumber)}章",
                ChapterName = endEpisode >= totalEpisodes ? "终局收束" : "剧情推进",
                StartEpisode = startEpisode,
                EndEpisode = endEpisode,
            };
        }

        private static int ResolveTotalEpisodes(string text, int fallback)
        {
            var total = Math.Max(0, fallback);
            foreach (var pattern in new[]
            {
                @"(?:总集数|Total\s+Episodes?)\s*[:：]?\s*(?<count>\d{1,3})\s*(?:集|episodes?)?",
                @"(?:剧集结构规划|章节结构规划|分集规划)[^\r\n]*?共\s*(?<count>\d{1,3})\s*集",
                @"共\s*(?<count>\d{1,3})\s*集",
            })
            {
                foreach (Match match in Regex.Matches(text ?? string.Empty, pattern, RegexOptions.IgnoreCase))
                {
                    if (int.TryParse(match.Groups["count"].Value, out var parsed) && parsed > 0)
                    {
                        total = Math.Max(total, parsed);
                    }
                }
            }

            return total;
        }

        private static int ResolveTypicalChapterSpan(IReadOnlyList<OutlineChapterPlan> chapters)
        {
            return chapters
                .Where(chapter => chapter.EpisodeCount > 1)
                .GroupBy(chapter => chapter.EpisodeCount)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => Math.Abs(group.Key - 4))
                .ThenBy(group => group.Key)
                .Select(group => group.Key)
                .FirstOrDefault(4);
        }

        private static bool TryParseChapterPlan(string value, out OutlineChapterPlan plan)
        {
            plan = new OutlineChapterPlan();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var match = Regex.Match(
                value.Trim(),
                @"^(?<title>.+?)\s*[（(]\s*第\s*(?<start>\d+)\s*[-~—–至到]+\s*(?<end>\d+)\s*集\s*[）)]\s*$");
            if (!match.Success ||
                !int.TryParse(match.Groups["start"].Value, out var startEpisode) ||
                !int.TryParse(match.Groups["end"].Value, out var endEpisode) ||
                endEpisode < startEpisode)
            {
                return false;
            }

            var title = WorkflowParseHelpers.CleanExtractedValue(match.Groups["title"].Value);
            var parts = title.Split(new[] { '：', ':' }, 2, StringSplitOptions.TrimEntries);
            plan = new OutlineChapterPlan
            {
                ChapterLabel = parts.Length > 0 ? parts[0] : title,
                ChapterName = parts.Length > 1 ? parts[1] : string.Empty,
                StartEpisode = startEpisode,
                EndEpisode = endEpisode,
            };
            return true;
        }

        private static string ToChineseNumber(int value)
        {
            string[] digits = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            if (value <= 0)
            {
                return value.ToString();
            }

            if (value < 10)
            {
                return digits[value];
            }

            if (value == 10)
            {
                return "十";
            }

            if (value < 20)
            {
                return "十" + digits[value % 10];
            }

            if (value < 100)
            {
                var ones = value % 10;
                return digits[value / 10] + "十" + (ones == 0 ? string.Empty : digits[ones]);
            }

            return value.ToString();
        }

        private static string NormalizeChapterLabel(string value)
        {
            var cleaned = WorkflowParseHelpers.CleanExtractedValue(value);
            var numberedMatch = Regex.Match(cleaned, @"^章节\s*(\d+)$", RegexOptions.IgnoreCase);
            if (numberedMatch.Success)
            {
                return $"第{numberedMatch.Groups[1].Value}章";
            }

            return cleaned;
        }

        private static bool TryExtractEpisodeRange(string value, out int startEpisode, out int endEpisode)
        {
            startEpisode = 0;
            endEpisode = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var rangeMatch = Regex.Match(
                value,
                @"第?\s*(?<start>\d+)\s*[-~—–至到]+\s*(?<end>\d+)\s*集?",
                RegexOptions.IgnoreCase);
            if (rangeMatch.Success &&
                int.TryParse(rangeMatch.Groups["start"].Value, out startEpisode) &&
                int.TryParse(rangeMatch.Groups["end"].Value, out endEpisode) &&
                endEpisode >= startEpisode)
            {
                return true;
            }

            var singleEpisodeMatch = Regex.Match(value, @"第?\s*(?<episode>\d+)\s*集?", RegexOptions.IgnoreCase);
            if (singleEpisodeMatch.Success &&
                int.TryParse(singleEpisodeMatch.Groups["episode"].Value, out startEpisode))
            {
                endEpisode = startEpisode;
                return true;
            }

            startEpisode = 0;
            endEpisode = 0;
            return false;
        }

        private static string NormalizeEpisodeContent(string content, string title, string characters, string keyItems, string continuityNote)
        {
            var normalized = WorkflowResultParser.NormalizeTextResult(WorkflowNodeCatalog.Script, content);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                var builder = new StringBuilder();
                builder.AppendLine($"## {title}");
                if (!string.IsNullOrWhiteSpace(characters))
                {
                    builder.AppendLine();
                    builder.AppendLine($"**角色**: {characters}");
                }

                if (!string.IsNullOrWhiteSpace(keyItems))
                {
                    builder.AppendLine($"**关键物品**: {keyItems}");
                }

                if (!string.IsNullOrWhiteSpace(continuityNote))
                {
                    builder.AppendLine();
                    builder.AppendLine($"**连贯性说明**: {continuityNote}");
                }

                return builder.ToString().Trim();
            }

            if (!normalized.Contains("##", StringComparison.Ordinal))
            {
                normalized = $"## {title}{Environment.NewLine}{Environment.NewLine}{normalized}";
            }

            if (!string.IsNullOrWhiteSpace(characters) && !normalized.Contains("**角色**", StringComparison.Ordinal))
            {
                normalized = normalized.Replace(
                    $"## {title}",
                    $"## {title}{Environment.NewLine}{Environment.NewLine}**角色**: {characters}",
                    StringComparison.Ordinal);
            }

            if (!string.IsNullOrWhiteSpace(keyItems) && !normalized.Contains("**关键物品**", StringComparison.Ordinal))
            {
                normalized += $"{Environment.NewLine}{Environment.NewLine}**关键物品**: {keyItems}";
            }

            if (!string.IsNullOrWhiteSpace(continuityNote) && !normalized.Contains("**连贯性说明**", StringComparison.Ordinal))
            {
                normalized += $"{Environment.NewLine}{Environment.NewLine}**连贯性说明**: {continuityNote}";
            }

            return normalized.Trim();
        }
    }
}
