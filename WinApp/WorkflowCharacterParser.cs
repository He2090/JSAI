using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JSAI.WinApp
{
    public static class WorkflowCharacterParser
    {
        public static bool SyncCharacterDesignEntries(WorkflowNode node, string outlineText, string characterDescriptionText, int limit = 12)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var current = node.Params.CharacterEntries ?? new List<CharacterDesignEntry>();
            var previousState = JsonSerializer.Serialize(current);
            var seeds = ExtractCharacterDesignEntries(outlineText, characterDescriptionText, limit);
            if (seeds.Count == 0 && current.Count == 0)
            {
                return false;
            }

            var currentByName = current
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .GroupBy(entry => entry.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var merged = new List<CharacterDesignEntry>();

            foreach (var seed in seeds)
            {
                currentByName.TryGetValue(seed.Name.Trim(), out var existing);
                merged.Add(MergeCharacterEntry(existing, seed));
            }

            node.Params.CharacterEntries = merged;
            if (string.IsNullOrWhiteSpace(node.Params.SelectedCharacterName) ||
                node.Params.CharacterEntries.All(entry => !string.Equals(entry.Name, node.Params.SelectedCharacterName, StringComparison.OrdinalIgnoreCase)))
            {
                node.Params.SelectedCharacterName = node.Params.CharacterEntries.FirstOrDefault()?.Name ?? string.Empty;
            }

            var nextState = JsonSerializer.Serialize(node.Params.CharacterEntries);
            return !string.Equals(previousState, nextState, StringComparison.Ordinal);
        }

        public static List<CharacterDesignEntry> ExtractCharacterDesignEntries(string outlineText, string characterDescriptionText, int limit = 12)
        {
            var entries = new Dictionary<string, CharacterDesignEntry>(StringComparer.OrdinalIgnoreCase);

            void Upsert(CharacterDesignEntry entry)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                {
                    return;
                }

                var key = entry.Name.Trim();
                if (!entries.TryGetValue(key, out var current))
                {
                    entries[key] = entry;
                    return;
                }

                entries[key] = MergeCharacterEntry(current, entry);
            }

            var legacySection = WorkflowParseHelpers.ExtractMarkdownSection(
                outlineText,
                new[] { "## 主要人物小传" },
                "## 关键物品设定",
                "## 关键物品",
                "## 章节规划",
                "## 剧集结构规划");
            if (!string.IsNullOrWhiteSpace(legacySection))
            {
                var currentRole = CharacterDesignRoleType.Main;
                var lines = legacySection.Replace("\r\n", "\n").Split('\n');
                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (line.StartsWith("###", StringComparison.Ordinal))
                    {
                        currentRole = line.Contains("核心", StringComparison.Ordinal) || line.Contains("主角", StringComparison.Ordinal)
                            ? CharacterDesignRoleType.Main
                            : CharacterDesignRoleType.Supporting;
                        continue;
                    }

                    var match = Regex.Match(
                        line,
                        @"^[\-\*]\s*\*\*(?<name>[^*：:]+)\*\*\s*[：:]\s*(?<summary>.+)$");
                    if (!match.Success)
                    {
                        match = Regex.Match(line, @"^[\-\*]\s*\*\*(?<name>[^*：:]+)\*\*$");
                    }

                    if (!match.Success)
                    {
                        continue;
                    }

                    Upsert(new CharacterDesignEntry
                    {
                        Name = WorkflowParseHelpers.CleanExtractedValue(match.Groups["name"].Value),
                        Summary = WorkflowParseHelpers.CleanExtractedValue(match.Groups["summary"].Value),
                        RoleType = currentRole.ToLabel(),
                    });
                }
            }

            var structuredSection = WorkflowParseHelpers.ExtractMarkdownSection(
                outlineText,
                new[] { "## 角色清单" },
                "## 关键物品设定",
                "## 关键物品",
                "## 章节规划",
                "## 剧集结构规划");
            if (!string.IsNullOrWhiteSpace(structuredSection))
            {
                var blocks = Regex.Matches(
                    structuredSection,
                    @"(?ims)^###\s*(?<heading>[^\r\n#]+?)\s*$\n(?<body>.*?)(?=^###\s*[^\r\n#]+?\s*$|\z)");

                foreach (var match in blocks.Cast<Match>())
                {
                    var body = match.Groups["body"].Value;
                    var headingName = WorkflowParseHelpers.CleanExtractedValue(match.Groups["heading"].Value);
                    var name = WorkflowParseHelpers.FirstNonEmpty(
                        WorkflowParseHelpers.ExtractMarkdownField(body, "名称", "角色名", "姓名"),
                        headingName);
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var role = WorkflowParseHelpers.ExtractMarkdownField(body, "角色定位", "角色类型", "定位");
                    var age = WorkflowParseHelpers.ExtractMarkdownField(body, "年龄");
                    var appearance = WorkflowParseHelpers.ExtractMarkdownField(body, "外形特征", "外貌特征", "外形");
                    var personality = WorkflowParseHelpers.ExtractMarkdownField(body, "性格标签", "性格");
                    var motivation = WorkflowParseHelpers.ExtractMarkdownField(body, "核心目标", "目标");
                    var conflict = WorkflowParseHelpers.ExtractMarkdownField(body, "核心冲突", "冲突");
                    var relationships = WorkflowParseHelpers.ExtractMarkdownField(body, "角色关系", "关系");
                    var arc = WorkflowParseHelpers.ExtractMarkdownField(body, "角色弧光", "弧光");
                    var values = WorkflowParseHelpers.ExtractMarkdownField(body, "价值观", "信念");
                    var weakness = WorkflowParseHelpers.ExtractMarkdownField(body, "弱点", "恐惧");
                    var habits = WorkflowParseHelpers.ExtractMarkdownField(body, "习惯", "兴趣", "爱好");

                    var basicStats = string.Join("，", new[] { age, appearance }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    var summaryParts = new[] { role, appearance, personality }
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToList();
                    var summary = string.Join(" / ", summaryParts);
                    var backgroundParts = new[] { relationships, arc, conflict }
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToList();

                    Upsert(new CharacterDesignEntry
                    {
                        Name = name,
                        RoleType = role.Contains("配", StringComparison.Ordinal) || role.Contains("反派", StringComparison.Ordinal)
                            ? CharacterDesignRoleType.Supporting.ToLabel()
                            : CharacterDesignRoleType.Main.ToLabel(),
                        Summary = summary,
                        BasicStats = basicStats,
                        Profession = role,
                        Background = string.Join(" / ", backgroundParts),
                        Personality = personality,
                        Motivation = motivation,
                        Values = values,
                        Weakness = weakness,
                        Relationships = relationships,
                        Habits = habits,
                    });
                }
            }

            foreach (var entry in ParseCharacterDescriptionEntries(characterDescriptionText))
            {
                Upsert(entry);
            }

            if (entries.Count == 0 && !string.IsNullOrWhiteSpace(outlineText))
            {
                var fallbackMatches = Regex.Matches(outlineText, @"\*\*(?<name>[^*：:\r\n]{2,12})\*\*");
                foreach (var match in fallbackMatches.Cast<Match>().Take(limit))
                {
                    Upsert(new CharacterDesignEntry
                    {
                        Name = WorkflowParseHelpers.CleanExtractedValue(match.Groups["name"].Value),
                        RoleType = CharacterDesignRoleType.Supporting.ToLabel(),
                    });
                }
            }

            return entries.Values
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Take(Math.Max(1, limit))
                .ToList();
        }

        public static List<CharacterDesignEntry> ParseCharacterDescriptionEntries(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<CharacterDesignEntry>();
            }

            JsonDocument? document = null;
            try
            {
                if (!WorkflowResultParser.TryExtractJsonPayload(text, out var jsonPayload))
                {
                    return new List<CharacterDesignEntry>();
                }

                document = JsonDocument.Parse(jsonPayload);
            }
            catch
            {
                return new List<CharacterDesignEntry>();
            }

            var result = new List<CharacterDesignEntry>();

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return result;
                }

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var name = WorkflowParseHelpers.ReadJsonString(item, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    result.Add(new CharacterDesignEntry
                    {
                        Name = name,
                        Alias = WorkflowParseHelpers.ReadJsonString(item, "alias"),
                        RoleType = WorkflowParseHelpers.ReadJsonString(item, "role").Contains("配", StringComparison.Ordinal)
                            ? CharacterDesignRoleType.Supporting.ToLabel()
                            : CharacterDesignRoleType.Main.ToLabel(),
                        Summary = string.Join(" / ", new[]
                        {
                            WorkflowParseHelpers.ReadJsonString(item, "basicStats"),
                            WorkflowParseHelpers.ReadJsonString(item, "profession"),
                            WorkflowParseHelpers.ReadJsonString(item, "personality"),
                        }.Where(value => !string.IsNullOrWhiteSpace(value))),
                        BasicStats = WorkflowParseHelpers.ReadJsonString(item, "basicStats"),
                        Profession = WorkflowParseHelpers.ReadJsonString(item, "profession"),
                        Background = WorkflowParseHelpers.ReadJsonString(item, "background"),
                        Personality = WorkflowParseHelpers.ReadJsonString(item, "personality"),
                        Motivation = WorkflowParseHelpers.ReadJsonString(item, "motivation"),
                        Values = WorkflowParseHelpers.ReadJsonString(item, "values"),
                        Weakness = WorkflowParseHelpers.ReadJsonString(item, "weakness"),
                        Relationships = WorkflowParseHelpers.ReadJsonString(item, "relationships"),
                        Habits = WorkflowParseHelpers.ReadJsonString(item, "habits"),
                        VisualTags = WorkflowParseHelpers.ReadJsonString(item, "visualTags"),
                        AppearancePrompt = WorkflowParseHelpers.ReadJsonString(item, "appearancePrompt"),
                        CostumeNotes = WorkflowParseHelpers.ReadJsonString(item, "costumeNotes"),
                        ActingNotes = WorkflowParseHelpers.ReadJsonString(item, "actingNotes"),
                        ProfileStatus = CharacterAssetStatus.Success,
                    });
                }
            }

            return result;
        }

        private static CharacterDesignEntry MergeCharacterEntry(CharacterDesignEntry? current, CharacterDesignEntry seed)
        {
            var merged = current ?? new CharacterDesignEntry();
            merged.Name = WorkflowParseHelpers.FirstNonEmpty(seed.Name, merged.Name);
            merged.Alias = WorkflowParseHelpers.FirstNonEmpty(merged.Alias, seed.Alias);
            merged.RoleType = WorkflowParseHelpers.FirstNonEmpty(merged.RoleType, seed.RoleType, CharacterDesignRoleType.Main.ToLabel());
            merged.Summary = WorkflowParseHelpers.ChooseLongerText(merged.Summary, seed.Summary);
            merged.BasicStats = WorkflowParseHelpers.ChooseLongerText(merged.BasicStats, seed.BasicStats);
            merged.Profession = WorkflowParseHelpers.ChooseLongerText(merged.Profession, seed.Profession);
            merged.Background = WorkflowParseHelpers.ChooseLongerText(merged.Background, seed.Background);
            merged.Personality = WorkflowParseHelpers.ChooseLongerText(merged.Personality, seed.Personality);
            merged.Motivation = WorkflowParseHelpers.ChooseLongerText(merged.Motivation, seed.Motivation);
            merged.Values = WorkflowParseHelpers.ChooseLongerText(merged.Values, seed.Values);
            merged.Weakness = WorkflowParseHelpers.ChooseLongerText(merged.Weakness, seed.Weakness);
            merged.Relationships = WorkflowParseHelpers.ChooseLongerText(merged.Relationships, seed.Relationships);
            merged.Habits = WorkflowParseHelpers.ChooseLongerText(merged.Habits, seed.Habits);
            merged.VisualTags = WorkflowParseHelpers.ChooseLongerText(merged.VisualTags, seed.VisualTags);
            merged.AppearancePrompt = WorkflowParseHelpers.ChooseLongerText(merged.AppearancePrompt, seed.AppearancePrompt);
            merged.CostumeNotes = WorkflowParseHelpers.ChooseLongerText(merged.CostumeNotes, seed.CostumeNotes);
            merged.ActingNotes = WorkflowParseHelpers.ChooseLongerText(merged.ActingNotes, seed.ActingNotes);
            merged.ExpressionPrompt = WorkflowParseHelpers.FirstNonEmpty(merged.ExpressionPrompt, seed.ExpressionPrompt);
            merged.ThreeViewPrompt = WorkflowParseHelpers.FirstNonEmpty(merged.ThreeViewPrompt, seed.ThreeViewPrompt);
            merged.ExpressionSheetPath = WorkflowParseHelpers.FirstNonEmpty(merged.ExpressionSheetPath, seed.ExpressionSheetPath);
            merged.ThreeViewSheetPath = WorkflowParseHelpers.FirstNonEmpty(merged.ThreeViewSheetPath, seed.ThreeViewSheetPath);
            merged.ReferencePortraitPath = WorkflowParseHelpers.FirstNonEmpty(merged.ReferencePortraitPath, seed.ReferencePortraitPath);
            if (merged.ProfileStatus == CharacterAssetStatus.Pending && seed.ProfileStatus != CharacterAssetStatus.Pending)
            {
                merged.ProfileStatus = seed.ProfileStatus;
            }

            if (merged.ExpressionStatus == CharacterAssetStatus.Pending && seed.ExpressionStatus != CharacterAssetStatus.Pending)
            {
                merged.ExpressionStatus = seed.ExpressionStatus;
            }

            if (merged.ThreeViewStatus == CharacterAssetStatus.Pending && seed.ThreeViewStatus != CharacterAssetStatus.Pending)
            {
                merged.ThreeViewStatus = seed.ThreeViewStatus;
            }

            merged.LastError = WorkflowParseHelpers.FirstNonEmpty(merged.LastError, seed.LastError);
            return merged;
        }
    }
}
