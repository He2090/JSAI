using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace JSAI.WinApp
{
    public static class WorkflowPromptBuilder
    {
        public static string BuildTextPrompt(WorkflowNode node, string input)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var builder = new StringBuilder();
            builder.AppendLine("You are a content generation assistant in the JSAI anime comic workflow. Output only the final result for the current node. Do not explain the process or add extraneous commentary.");
            builder.AppendLine($"Current node type: {node.Type}");

            if (node.Type == WorkflowNodeCatalog.Outline)
            {
                builder.AppendLine(BuildOutlinePrompt(node));
            }
            else if (node.Type == WorkflowNodeCatalog.Script)
            {
                builder.AppendLine(BuildScriptPrompt(node, input));
            }
            else if (node.Type == WorkflowNodeCatalog.CreativeDescription)
            {
                builder.AppendLine(BuildCreativeDescriptionStoryboardPrompt(node));
            }
            else if (node.Type == WorkflowNodeCatalog.CharacterDescription)
            {
                builder.AppendLine(BuildCharacterDescriptionPrompt(node));
            }
            else if (node.Type == WorkflowNodeCatalog.StoryboardBreakdown)
            {
                builder.AppendLine(BuildStoryboardBreakdownPrompt(node));
            }

            var parametersText = DescribeParameters(node);
            if (!string.IsNullOrWhiteSpace(parametersText) && parametersText != "<N/A>")
            {
                builder.AppendLine();
                builder.AppendLine("Node parameters:");
                builder.AppendLine(parametersText.Trim());
            }

            var normalizedInput = node.Type == WorkflowNodeCatalog.Script
                ? BuildScriptReadableOutlineContext(input)
                : input;

            if (!string.IsNullOrWhiteSpace(normalizedInput))
            {
                builder.AppendLine();
                builder.AppendLine("Upstream input:");
                builder.AppendLine(normalizedInput.Trim());
            }

            builder.AppendLine();
            builder.AppendLine("Output only the content body ready for the next node.");
            return builder.ToString().Trim();
        }

        private static string BuildOutlinePrompt(WorkflowNode node)
        {
            var coreIdea = string.IsNullOrWhiteSpace(node.Params?.CoreIdea)
                ? "Create a story around a compelling core concept suitable for anime comic adaptation."
                : node.Params!.CoreIdea.Trim();
            var genre = string.IsNullOrWhiteSpace(node.Params?.Genre) ? "Unspecified" : node.Params!.Genre;
            var setting = string.IsNullOrWhiteSpace(node.Params?.Setting) ? "Unspecified" : node.Params!.Setting;
            var visualStyle = WorkflowNodeParameters.GetVisualStyleDisplayName(node.Params?.VisualStyle ?? "ANIME");
            var totalEpisodes = Math.Max(1, node.Params?.Episodes ?? 10);
            var duration = node.Params == null || node.Params.DurationMinutes <= 0 ? 1M : node.Params.DurationMinutes;
            var (chapterCount, episodesPerChapter) = CalculateOutlineStructure(totalEpisodes);
            var (minCharacters, maxCharacters) = CalculateCharacterRange(totalEpisodes);
            var (minItems, maxItems) = CalculateItemRange(totalEpisodes);

            var builder = new StringBuilder();
            builder.AppendLine("You are a professional screenwriter specializing in short-form series and micro-films.");
            builder.AppendLine("Your task is to create an engaging screenplay outline based on the user's core concept and constraints. All creative content (character bios, plot descriptions, chapter summaries) must be written in Chinese.");
            builder.AppendLine();
            builder.AppendLine("Core creative input:");
            builder.AppendLine($"- Core concept: {coreIdea}");
            builder.AppendLine($"- Genre: {genre}");
            builder.AppendLine($"- Setting: {setting}");
            builder.AppendLine($"- Visual style: {visualStyle}");
            builder.AppendLine($"- Total episodes: {totalEpisodes}");
            builder.AppendLine($"- Episode duration: {duration:0.#} min");
            builder.AppendLine();
            builder.AppendLine("Core principle: plan the outline at the chapter level only. Do not break down into per-episode scenes.");
            builder.AppendLine();
            builder.AppendLine("Episode scale requirements:");
            builder.AppendLine($"- This is a {totalEpisodes}-episode series; recommend {chapterCount} chapters.");
            builder.AppendLine($"- Each chapter covers approximately {episodesPerChapter} episodes, with continuous causal chains between chapters.");
            builder.AppendLine($"- Episode duration is {duration:0.#} min; pacing must suit short-form storytelling.");
            builder.AppendLine();
            builder.AppendLine($"Recommended character count: {minCharacters}-{maxCharacters}.");
            builder.AppendLine("- Core characters: 3-5, must include detailed bios, 80-120 chars each.");
            builder.AppendLine("- Supporting characters: 8-12, brief descriptions, 20-40 chars each.");
            builder.AppendLine("- Background characters: brief mentions, 5-10 chars each.");
            builder.AppendLine();
            builder.AppendLine($"Recommended key item count: {minItems}-{maxItems}.");
            builder.AppendLine("- Core items: 3-5, driving the main plot and carrying symbolic meaning.");
            builder.AppendLine("- Supporting items: 5-8, serving specific chapters or plot advancement.");
            builder.AppendLine("- World items: supporting world-building and scene details.");
            builder.AppendLine();
            builder.AppendLine("Chapter pacing requirements:");
            builder.AppendLine("- Place a minor climax at least every 3-5 episodes.");
            builder.AppendLine("- Place a major turning point at least every 10-15 episodes.");
            builder.AppendLine("- Each chapter must have clear setup-development-twist-resolution, and end with a hook that drives continued viewing.");
            builder.AppendLine();
            builder.AppendLine("Output format requirements:");
            builder.AppendLine("- Output only Markdown content. Do not explain. Do not wrap in ```markdown``` code fences.");
            builder.AppendLine("- Preserve blank lines for paragraph separation. Use the exact heading names below — do not rewrite them.");
            builder.AppendLine("- Title line must follow the format `# 剧名 (Title): 《...》`.");
            builder.AppendLine();
            builder.AppendLine("# 剧名 (Title): 《...》");
            builder.AppendLine("**一句话梗概 (Logline)**: ...");
            builder.AppendLine("**类型 (Genre)**: ... | **主题 (Theme)**: ... | **背景 (Setting)**: ... | **视觉风格**: ...");
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
            builder.AppendLine("## 主要人物小传");
            builder.AppendLine();
            builder.AppendLine("### 核心角色(详细小传,80-120字/人)");
            builder.AppendLine("* **[姓名]**: [角色定位] - [年龄] [外貌特征]。性格:[性格特点]。背景:[重要经历]。能力/特征:[特殊能力或标志性特征]。");
            builder.AppendLine();
            builder.AppendLine("### 重要配角(简单描述,20-40字/人)");
            builder.AppendLine("* **[姓名]**: [角色定位和作用,简短描述]");
            builder.AppendLine();
            builder.AppendLine("### 其他角色(一笔带过,5-10字/人)");
            builder.AppendLine("* **[姓名]**: [身份或作用]");
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
            builder.AppendLine("## 关键物品设定");
            builder.AppendLine();
            builder.AppendLine("### 核心物品(30-50字/个)");
            builder.AppendLine("* **[物品名称]**: [物品描述、功能、象征意义]");
            builder.AppendLine();
            builder.AppendLine("### 辅助物品(15-25字/个)");
            builder.AppendLine("* **[物品名称]**: [物品描述和出现时机]");
            builder.AppendLine();
            builder.AppendLine("### 世界物品(10-15字/个)");
            builder.AppendLine("* **[物品名称]**: [简要描述]");
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
            builder.AppendLine($"## 剧集结构规划(共 {totalEpisodes} 集,{chapterCount} 章)");
            builder.AppendLine();
            builder.AppendLine("#### 第X章:章节名称(第A-B集)");
            builder.AppendLine();
            builder.AppendLine("**涉及角色**:角色A、角色B、角色C");
            builder.AppendLine();
            builder.AppendLine("**关键物品**:物品A、物品B");
            builder.AppendLine();
            builder.AppendLine("**章节剧情**(100-150字):");
            builder.AppendLine("Write the overall story for these episodes. Must include setup-development-twist-resolution, and clearly state character relationships, core conflicts, and emotional progression.");
            builder.AppendLine();
            builder.AppendLine("- 第A集:发生了什么");
            builder.AppendLine("- 第A+1集:情节如何推进");
            builder.AppendLine("- 第B集:本章高潮或转折");
            builder.AppendLine();
            builder.AppendLine("**关键节点**:小高潮(第X集)或大转折(第Y集)");
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
            builder.AppendLine("Important rules:");
            builder.AppendLine("1. Plan at chapter level only; do not output per-episode scene scripts.");
            builder.AppendLine("2. Each chapter synopsis should be around 100-150 characters and must have clear setup-development-twist-resolution.");
            builder.AppendLine("3. Use Chinese ordinal numbering for chapter titles, e.g. 第一章, 第二章.");
            builder.AppendLine("4. Pacing must reflect a minor climax every 3-5 episodes and a major turning point every 10-15 episodes.");
            builder.AppendLine("5. Character names and item names must remain consistent throughout; do not substitute synonyms.");
            builder.AppendLine("6. Visual style must match the user's selection; prose should reflect the corresponding aesthetic.");
            builder.AppendLine("7. Use Chinese for all creative content, but retain fixed bilingual field labels such as Title, Logline, Genre, Theme, Setting.");
            builder.AppendLine("8. Keep formatting clean with blank lines, horizontal rules, and heading hierarchy so the downstream Script node can parse it.");
            return builder.ToString().Trim();
        }

        private static string BuildScriptPrompt(WorkflowNode node, string input)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var chapters = WorkflowExecutor.ExtractOutlineChapters(input, 24);
            var selectedChapter = WorkflowExecutor.ResolveScriptChapterSelection(node, chapters);
            var requestedEpisodes = WorkflowExecutor.ResolveScriptEpisodeCount(node, selectedChapter);
            var targetRangeText = WorkflowExecutor.BuildScriptTargetEpisodeRange(selectedChapter, requestedEpisodes);
            var revisionNotes = string.IsNullOrWhiteSpace(node.Params.ScriptRevisionNotes)
                ? string.Empty
                : node.Params.ScriptRevisionNotes.Trim();

            var builder = new StringBuilder();
            builder.AppendLine("Goal: based on the canonical story outline, output per-episode script data that downstream nodes can consume directly.");
            builder.AppendLine("If the upstream input contains a canonical story outline in Markdown, you must read and inherit these sections:");
            builder.AppendLine("- # 故事大纲 / # 剧名 (Title)");
            builder.AppendLine("- ## 基础信息");
            builder.AppendLine("- 一句话梗概 / 类型 / 主题 / 背景 / 视觉风格 / 总集数 / 单集时长");
            builder.AppendLine("- ## 角色清单 / ## 主要人物小传");
            builder.AppendLine("- ## 关键物品 / ## 关键物品设定");
            builder.AppendLine("- ## 章节规划 / ## 剧集结构规划");
            builder.AppendLine("- ## 分集规划 (if present)");
            builder.AppendLine("Strictly follow the current node configuration. Only split the specified chapter; do not cross chapter boundaries.");
            builder.AppendLine();
            if (selectedChapter != null)
            {
                builder.AppendLine($"Target chapter: {selectedChapter.DisplayText}");
                builder.AppendLine($"Episodes to generate: {requestedEpisodes}");
                builder.AppendLine($"Coverage range: {targetRangeText}");
            }
            else
            {
                builder.AppendLine("Target chapter: if the upstream outline already identifies chapters, default to splitting chapter 1.");
                builder.AppendLine($"Episodes to generate: {Math.Max(1, node.Params.ScriptEpisodesToGenerate)}");
            }

            if (!string.IsNullOrWhiteSpace(revisionNotes))
            {
                builder.AppendLine($"Revision notes: {revisionNotes}");
            }

            builder.AppendLine("Output must be a pure JSON array. Do not wrap in ```json``` code fences or add explanatory text.");
            builder.AppendLine($"The JSON array must contain exactly {requestedEpisodes} elements — no fewer, no more.");
            builder.AppendLine("Each array element represents one episode. Fields must follow this exact structure:");
            builder.AppendLine("[");
            builder.AppendLine("  {");
            builder.AppendLine("    \"title\": \"第X集: episode title\",");
            builder.AppendLine("    \"content\": \"Creative description for this episode with clear sections. Order: ## 第X集: title, blank line, **角色**, **关键物品**, blank line, 【场景描述】, 【角色互动】, 【动作与冲突】, 【对白】, 【悬念】\",");
            builder.AppendLine("    \"characters\": \"List of characters in this episode, using standard names from the outline.\",");
            builder.AppendLine("    \"keyItems\": \"List of key items in this episode, using standard names from the outline.\",");
            builder.AppendLine("    \"visualStyleNote\": \"视觉风格与镜头氛围说明，使用简体中文。\",");
            builder.AppendLine("    \"continuityNote\": \"与前后集的连贯性和钩子说明，使用简体中文。\"");
            builder.AppendLine("  }");
            builder.AppendLine("]");
            builder.AppendLine("Content requirements:");
            builder.AppendLine("1. All creative content in Simplified Chinese. Keep character, item, and chapter naming consistent.");
            builder.AppendLine($"2. Generate exactly {requestedEpisodes} episodes for the selected chapter. Do not write content for later chapters.");
            builder.AppendLine("3. Each episode must stand alone while maintaining continuous progression with adjacent episodes.");
            builder.AppendLine("4. Every episode must end with a clear hook that drives the next episode.");
            builder.AppendLine("5. Longer episode duration requires more content; default density: 200-250 characters per minute.");
            builder.AppendLine("6. If the chapter has per-episode summaries in the outline, refine them without contradicting.");
            builder.AppendLine($"7. Episode numbers in titles must match the range \"{targetRangeText}\". Do not restart from episode 1.");
            builder.AppendLine("8. The content field must preserve line breaks; do not flatten into a single paragraph.");
            builder.AppendLine("9. 【对白】(dialogue) sections: dialogue lines in Simplified Chinese Mandarin. Prefix each line with the speaking character's name and a brief Chinese tone note in Chinese brackets, e.g. 张三【愤怒】：..., 李四【低声】：..., 王五【平静】：.... Also include action beats. Use 无 as placeholder when no dialogue occurs in a scene. Do not use English tone tags.");
            return builder.ToString().Trim();
        }

        private static string BuildCharacterDescriptionPrompt(WorkflowNode node)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Goal: Based on the story outline and existing scripts, generate core character profiles ready for the Character View node.");
            builder.AppendLine("Output must be a pure JSON array. Do not wrap in ```json``` code fences.");
            builder.AppendLine("Focus on 3-6 core characters. If information is insufficient, you may reasonably complete it, but never contradict upstream settings.");
            builder.AppendLine("Each object in the array must contain these fields:");
            builder.AppendLine("[");
            builder.AppendLine("  {");
            builder.AppendLine("    \"name\": \"Character name\",");
            builder.AppendLine("    \"alias\": \"Alias or commonly used name\",");
            builder.AppendLine("    \"role\": \"Role and positioning in the story\",");
            builder.AppendLine("    \"basicStats\": \"Age, gender, height, build, hairstyle, facial features, clothing\",");
            builder.AppendLine("    \"profession\": \"Profession and hidden identity\",");
            builder.AppendLine("    \"background\": \"Background and key life experiences\",");
            builder.AppendLine("    \"personality\": \"Core personality traits and behavioral style\",");
            builder.AppendLine("    \"motivation\": \"Core motivation\",");
            builder.AppendLine("    \"values\": \"Values and bottom lines\",");
            builder.AppendLine("    \"weakness\": \"Weaknesses, fears, obsessions\",");
            builder.AppendLine("    \"relationships\": \"Key relationships with other characters\",");
            builder.AppendLine("    \"habits\": \"Speaking style, physical habits, hobbies\",");
            builder.AppendLine("    \"visualTags\": \"Chinese keywords for visual generation, comma-separated\",");
            builder.AppendLine("    \"appearancePrompt\": \"English prompt for image generation, comma-separated. Must include: style, appearance, clothing, pose, expression, lighting\",");
            builder.AppendLine("    \"costumeNotes\": \"Costume and accessory notes\",");
            builder.AppendLine("    \"actingNotes\": \"Typical on-camera states, actions, and presence\"");
            builder.AppendLine("  }");
            builder.AppendLine("]");
            builder.AppendLine("Requirements:");
            builder.AppendLine("1. Character names must match the outline.");
            builder.AppendLine("2. appearancePrompt must be in English, ready for image models.");
            builder.AppendLine("3. visualTags, costumeNotes, actingNotes should serve downstream Character View and Storyboard nodes well.");
            return builder.ToString().Trim();
        }

        private static string BuildStoryboardBreakdownPrompt(WorkflowNode node)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Goal: Based on upstream story outline, episode scripts, and creative descriptions, output an editable cinematography shot list ready for the Storyboard Image node.");
            builder.AppendLine("Output must be a JSON object. Do not wrap in ```json``` code fences. Do not append explanations.");
            builder.AppendLine("Minimum 6 shots, preferably 8-12. Each shot must be a distinct camera unit within the current episode.");
            builder.AppendLine("Strictly use the provided Simplified Chinese cinematography terms for shotSize / cameraAngle / cameraMovement.");
            builder.AppendLine("JSON format must be exactly as follows:");
            builder.AppendLine("{");
            builder.AppendLine("  \"episodeTitle\": \"Episode title\",");
            builder.AppendLine("  \"visualStyle\": \"ANIME / REAL / 3D\",");
            builder.AppendLine("  \"totalShots\": 8,");
            builder.AppendLine("  \"totalDuration\": 24,");
            builder.AppendLine("  \"shots\": [");
            builder.AppendLine("    {");
            builder.AppendLine("      \"id\": \"shot_1\",");
            builder.AppendLine("      \"shotNumber\": 1,");
            builder.AppendLine("      \"duration\": 3,");
            builder.AppendLine("      \"scene\": \"司法部广场 / 雨夜 / 俯瞰全景\",");
            builder.AppendLine("      \"characters\": [\"Character A\", \"Character B\"],");
            builder.AppendLine("      \"shotSize\": \"大远景\",");
            builder.AppendLine("      \"cameraAngle\": \"鸟瞰\",");
            builder.AppendLine("      \"cameraMovement\": \"固定\",");
            builder.AppendLine("      \"visualDescription\": \"简体中文画面描述，用于节点卡片显示\",");
            builder.AppendLine("      \"imagePrompt\": \"English image-generation prompt for this shot, including location, action, composition, lighting, mood, props and any required visible text; all normal visual instructions must be English; visible text itself must be Simplified Chinese characters only\",");
            builder.AppendLine("      \"dialogue\": \"Simplified Chinese dialogue text, or 无 if none\",");
            builder.AppendLine("      \"visualEffects\": \"简体中文视觉效果。无效果写无。\",");
            builder.AppendLine("      \"audioEffects\": \"简体中文环境声、拟音、音乐提示。无声音写无。\",");
            builder.AppendLine("      \"startTime\": 0,");
            builder.AppendLine("      \"endTime\": 3");
            builder.AppendLine("    }");
            builder.AppendLine("  ]");
            builder.AppendLine("}");
            builder.AppendLine("Requirements:");
            builder.AppendLine("1. scene field: concise Simplified Chinese title in format “地点 / 时间 / 镜头重点” for list display.");
            builder.AppendLine("2. visualDescription: Simplified Chinese, specific, and written for the UI card display.");
            builder.AppendLine("3. imagePrompt: English, specific, and ready to drive per-frame storyboard image generation. It must include all key visualEffects when present.");
            builder.AppendLine("4. visualEffects / audioEffects: Simplified Chinese. Use “无” when no effects apply.");
            builder.AppendLine("5. dialogue: Simplified Chinese Mandarin text. Prefix each line with character name and a short Chinese tone tag in Chinese brackets, e.g. 张三【愤怒】：.... Use 无 when no dialogue. Do not use English tone tags.");
            builder.AppendLine("6. shotSize must be one of: 大远景、远景、全景、中景、中近景、近景、特写、大特写.");
            builder.AppendLine("7. cameraAngle must be one of: 平视、高位俯拍、低位仰拍、斜拍、越肩、鸟瞰.");
            builder.AppendLine("8. cameraMovement must be one of: 固定、横移、俯仰、摇移、升降、轨道推拉、变焦推拉、正跟随、倒跟随、环绕、滑轨横移.");
            builder.AppendLine("9. Characters' clothing, environment, lighting, and props must remain consistent within the same scene.");
            builder.AppendLine("10. Visible text rule: dialogue, subtitles, signs, screens, UI, posters, labels, notices and any readable text may only be exact Simplified Chinese characters with Chinese punctuation. In imagePrompt, keep visual instructions English, but keep the actual visible text content in Simplified Chinese inside Chinese quotation marks. Never output English visible text, pinyin, Arabic numerals, Traditional Chinese, random glyphs, or garbled text.");
            builder.AppendLine("11. Do not compress the entire episode into a single shot. Do not output empty/placeholder shots.");
            return builder.ToString().Trim();
        }

        private static string BuildCreativeDescriptionStoryboardPrompt(WorkflowNode node)
        {
            var title = string.IsNullOrWhiteSpace(node.Params?.CoreIdea)
                ? "Current creative description"
                : node.Params!.CoreIdea.Trim();

            var builder = new StringBuilder();
            builder.AppendLine("Goal: Split the current episode's creative description into an editable cinematography shot list for the Storyboard Image node to render frame by frame.");
            builder.AppendLine($"Current creative title: {title}");
            builder.AppendLine();
            builder.AppendLine("Output constraints:");
            builder.AppendLine("- Output only JSON. No explanations, no Markdown, no code fences.");
            builder.AppendLine("- Top-level structure must be an object with fields: episodeTitle, visualStyle, totalShots, totalDuration, shots.");
            builder.AppendLine("- shots must be an array. Recommend 6-12 shots.");
            builder.AppendLine("- Each shot must contain these fields:");
            builder.AppendLine("  id, shotNumber, duration, scene, characters, shotSize, cameraAngle, cameraMovement, visualDescription, imagePrompt, dialogue, visualEffects, audioEffects, startTime, endTime");
            builder.AppendLine("- scene: concise Simplified Chinese title for display on storyboard cards, e.g. “司法部广场 / 雨夜 / 俯瞰全景”.");
            builder.AppendLine("- characters: array of character names. Empty array when no characters.");
            builder.AppendLine("- shotSize must be one of: 大远景、远景、全景、中景、中近景、近景、特写、大特写.");
            builder.AppendLine("- cameraAngle must be one of: 平视、高位俯拍、低位仰拍、斜拍、越肩、鸟瞰.");
            builder.AppendLine("- cameraMovement must be one of: 固定、横移、俯仰、摇移、升降、轨道推拉、变焦推拉、正跟随、倒跟随、环绕、滑轨横移.");
            builder.AppendLine("- visualDescription: Simplified Chinese description of what the shot captures. This is shown in the local UI.");
            builder.AppendLine("- imagePrompt: English image-generation prompt for the same shot. Include location, characters, action, composition, camera framing, lighting, atmosphere, props, visualEffects, and any required visible text. All normal visual instructions must be English; visible text itself must be Simplified Chinese characters only.");
            builder.AppendLine("- dialogue: Simplified Chinese Mandarin. Prefix each line with character name + short Chinese tone tag in Chinese brackets, e.g. 张三【愤怒】：.... Use 无 when no dialogue. Do not use English tone tags.");
            builder.AppendLine("- visualEffects / audioEffects: Simplified Chinese. Use 无 when no effects apply.");
            builder.AppendLine("- Visible text rule: dialogue, subtitles, signs, screens, UI, posters, labels, notices and any readable text may only be exact Simplified Chinese characters with Chinese punctuation. In imagePrompt, keep visual instructions English, but keep the actual visible text content in Simplified Chinese inside Chinese quotation marks. Never output English visible text, pinyin, Arabic numerals, Traditional Chinese, random glyphs, or garbled text.");
            builder.AppendLine("- startTime and endTime increment in seconds. duration should be 2-5 seconds.");
            builder.AppendLine();
            builder.AppendLine("Breakdown principles:");
            builder.AppendLine("- Must prioritize covering all sections in the creative description: scene descriptions, character interactions, action & conflict, dialogue, and hooks.");
            builder.AppendLine("- Shot order must show clear rhythm and progression. Do not compress the entire content into a single image.");
            builder.AppendLine("- Each shot must have a clear visual subject, suitable for single-frame image generation.");
            builder.AppendLine("- If the creative description contains establishing shots, reaction shots, key action shots, and dialogue shots, break each out separately.");
            builder.AppendLine();
            builder.AppendLine("Output the final JSON directly.");
            return builder.ToString().Trim();
        }

        private static string BuildScriptReadableOutlineContext(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var normalizedInput = input.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
            var title = ExtractMarkdownField(normalizedInput, "剧名 (Title)", "剧名", "标题");
            var logline = ExtractMarkdownField(normalizedInput, "一句话梗概 (Logline)", "一句话梗概", "Logline");
            var genre = ExtractMarkdownField(normalizedInput, "类型 (Genre)", "类型", "Genre");
            var theme = ExtractMarkdownField(normalizedInput, "主题 (Theme)", "主题", "Theme");
            var setting = ExtractMarkdownField(normalizedInput, "背景 (Setting)", "背景", "Setting");
            var visualStyle = ExtractMarkdownField(normalizedInput, "视觉风格", "Visual Style");
            var (episodeCount, chapterCount) = ExtractOutlineScale(normalizedInput);
            var characterSection = ExtractMarkdownSection(
                normalizedInput,
                new[] { "## 主要人物小传", "## 角色清单" },
                "## 关键物品设定",
                "## 关键物品",
                "## 剧集结构规划",
                "## 章节规划",
                "## 分集规划");
            var itemsSection = ExtractMarkdownSection(
                normalizedInput,
                new[] { "## 关键物品设定", "## 关键物品" },
                "## 剧集结构规划",
                "## 章节规划",
                "## 分集规划");
            var chapterPlans = WorkflowExecutor.ExtractOutlineChapters(normalizedInput, 16);
            var chapterTitles = chapterPlans.Select(plan => plan.DisplayText).ToList();

            if (string.IsNullOrWhiteSpace(title) &&
                string.IsNullOrWhiteSpace(logline) &&
                string.IsNullOrWhiteSpace(characterSection) &&
                string.IsNullOrWhiteSpace(itemsSection) &&
                chapterTitles.Count == 0)
            {
                return normalizedInput;
            }

            var builder = new StringBuilder();
            builder.AppendLine("[Parsed Story Outline]");
            AppendIfPresent(builder, "Title", title);
            AppendIfPresent(builder, "Logline", logline);
            AppendIfPresent(builder, "Genre", genre);
            AppendIfPresent(builder, "Theme", theme);
            AppendIfPresent(builder, "Setting", setting);
            AppendIfPresent(builder, "Visual Style", visualStyle);
            AppendIfPresent(builder, "Total Episodes", episodeCount);
            AppendIfPresent(builder, "Chapters", chapterCount);

            if (!string.IsNullOrWhiteSpace(characterSection))
            {
                builder.AppendLine();
                builder.AppendLine("[Character Bios]");
                builder.AppendLine(characterSection.Trim());
            }

            if (!string.IsNullOrWhiteSpace(itemsSection))
            {
                builder.AppendLine();
                builder.AppendLine("[Key Items]");
                builder.AppendLine(itemsSection.Trim());
            }

            if (chapterTitles.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("[Chapter Titles]");
                foreach (var chapter in chapterTitles)
                {
                    builder.AppendLine($"- {chapter}");
                }
            }

            builder.AppendLine();
            builder.AppendLine("[Full Original Outline]");
            builder.AppendLine(normalizedInput);
            return builder.ToString().Trim();
        }

        private static string DescribeParameters(WorkflowNode node)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            if (node.Type == WorkflowNodeCatalog.Outline)
            {
                var summary = node.Params.BuildOutlineSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<N/A>" : summary;
            }

            if (node.Type == WorkflowNodeCatalog.Script)
            {
                var summary = node.Params.BuildScriptSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<N/A>" : summary;
            }

            if (node.Type == WorkflowNodeCatalog.CharacterView || node.Type == WorkflowNodeCatalog.CharacterDescription)
            {
                var summary = node.Params.BuildCharacterDesignSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<N/A>" : summary;
            }

            if (node.Type == WorkflowNodeCatalog.StoryboardBreakdown)
            {
                var summary = node.Params.BuildStoryboardBreakdownSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<N/A>" : summary;
            }

            if (node.Type == WorkflowNodeCatalog.StoryboardImage)
            {
                var summary = node.Params.BuildStoryboardImageSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<N/A>" : summary;
            }

            if (node.Type == WorkflowNodeCatalog.StoryboardVideo)
            {
                var summary = node.Params.BuildStoryboardVideoSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<N/A>" : summary;
            }

            if (node.Type == WorkflowNodeCatalog.VideoCollection)
            {
                var summary = node.Params.BuildVideoCollectionSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<N/A>" : summary;
            }

            return string.IsNullOrWhiteSpace(node.Params.Input) ? "<N/A>" : node.Params.Input.Trim();
        }

        private static (int ChapterCount, int EpisodesPerChapter) CalculateOutlineStructure(int totalEpisodes)
        {
            var safeEpisodes = Math.Max(1, totalEpisodes);
            var targetPerChapter = 4D;
            var candidates = new List<(int ChapterCount, int EpisodesPerChapter, double Score)>();

            for (var chapterCount = 2; chapterCount <= safeEpisodes; chapterCount++)
            {
                if (safeEpisodes % chapterCount != 0)
                {
                    continue;
                }

                var episodesPerChapter = safeEpisodes / chapterCount;
                if (episodesPerChapter < 2 || episodesPerChapter > 5)
                {
                    continue;
                }

                var score = Math.Abs(episodesPerChapter - targetPerChapter);
                candidates.Add((chapterCount, episodesPerChapter, score));
            }

            if (candidates.Count > 0)
            {
                var best = candidates
                    .OrderBy(candidate => candidate.Score)
                    .ThenBy(candidate => Math.Abs(candidate.ChapterCount - 5))
                    .First();
                return (best.ChapterCount, best.EpisodesPerChapter);
            }

            var fallbackChapterCount = Math.Max(2, (int)Math.Ceiling(safeEpisodes / 4D));
            var fallbackEpisodesPerChapter = Math.Max(2, (int)Math.Ceiling((double)safeEpisodes / fallbackChapterCount));
            return (fallbackChapterCount, fallbackEpisodesPerChapter);
        }

        private static (int MinCharacters, int MaxCharacters) CalculateCharacterRange(int totalEpisodes)
        {
            return totalEpisodes switch
            {
                <= 10 => (8, 12),
                <= 20 => (10, 16),
                <= 30 => (12, 18),
                _ => (14, 22),
            };
        }

        private static (int MinItems, int MaxItems) CalculateItemRange(int totalEpisodes)
        {
            return totalEpisodes switch
            {
                <= 10 => (6, 10),
                <= 20 => (8, 12),
                <= 30 => (10, 14),
                _ => (12, 16),
            };
        }

        private static string ExtractMarkdownField(string text, params string[] labels)
        {
            foreach (var label in labels)
            {
                var escapedLabel = Regex.Escape(label);
                var patterns = new[]
                {
                    $@"(?im)^[#>\-\*\s]*{escapedLabel}\s*[::]\s*(.+)$",
                    $@"(?im)(?:^|\|)\s*\*{{0,2}}\s*{escapedLabel}\s*\*{{0,2}}\s*[::]\s*(.+?)(?=\s*\||\r?\n|$)"
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(text, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        return CleanExtractedValue(match.Groups[1].Value);
                    }
                }
            }

            return string.Empty;
        }

        private static (string EpisodeCount, string ChapterCount) ExtractOutlineScale(string text)
        {
            var match = Regex.Match(text, @"##\s*剧集结构规划(共\s*(.+?)\s*集,\s*(.+?)\s*章)", RegexOptions.Multiline);
            if (match.Success)
            {
                return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
            }

            var episodeCount = ExtractMarkdownField(text, "总集数");
            var episodeNumberMatch = Regex.Match(episodeCount, @"\d+");
            if (episodeNumberMatch.Success)
            {
                episodeCount = episodeNumberMatch.Value;
            }

            var chapterSection = ExtractMarkdownSection(
                text,
                new[] { "## 章节规划", "## 剧集结构规划" },
                "## 分集规划");
            var chapterCount = Regex.Matches(
                    chapterSection,
                    @"(?im)^###\s*(?:章节\s*\d+|第[一二三四五六七八九十百零\d]+章|最终章)\s*$")
                .Count;

            if (chapterCount == 0)
            {
                chapterCount = WorkflowExecutor.ExtractOutlineChapters(text, 64).Count;
            }

            return (
                episodeCount,
                chapterCount > 0 ? chapterCount.ToString() : string.Empty);
        }

        private static string ExtractMarkdownSection(string text, IEnumerable<string> headings, params string[] nextHeadings)
        {
            foreach (var heading in headings)
            {
                if (string.IsNullOrWhiteSpace(heading))
                {
                    continue;
                }

                var startIndex = text.IndexOf(heading, StringComparison.Ordinal);
                if (startIndex < 0)
                {
                    continue;
                }

                var endIndex = text.Length;
                foreach (var nextHeading in nextHeadings.Where(value => !string.IsNullOrWhiteSpace(value)))
                {
                    var nextIndex = text.IndexOf(nextHeading, startIndex + heading.Length, StringComparison.Ordinal);
                    if (nextIndex >= 0)
                    {
                        endIndex = Math.Min(endIndex, nextIndex);
                    }
                }

                return text[startIndex..endIndex].Trim();
            }

            return string.Empty;
        }

        private static void AppendIfPresent(StringBuilder builder, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                builder.AppendLine($"{label}: {value}");
            }
        }

        private static string CleanExtractedValue(string value)
        {
            return value
                .Replace("**", string.Empty, StringComparison.Ordinal)
                .Replace("`", string.Empty, StringComparison.Ordinal)
                .Trim()
                .Trim('|')
                .Trim();
        }
    }
}
