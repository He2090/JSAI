using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JSAI.WinApp
{
    public static class WorkflowExecutor
    {
        private const string StoryboardVisibleSimplifiedChineseRule = "use English for all normal visual, camera, lighting and composition instructions; the only allowed visible written content inside the generated image is exact short Simplified Chinese characters with Chinese punctuation explicitly provided by the storyboard; never render English words, Latin letters, Arabic numerals, Traditional Chinese, Japanese/Korean characters, random glyphs, pseudo text, mojibake or garbled text; translate any sign name or label into Simplified Chinese, do not add English translations under Chinese text, do not use pinyin, and do not invent extra readable text; if exact Simplified Chinese text cannot be rendered, leave the surface blank";
        private const string StoryboardVisibleSimplifiedChineseNegativePrompt = "foreign text, pseudo text, watermark";
        private const string StoryboardVideoNoVisibleTextRule = "No visible text, subtitles, captions, logos, watermark, UI, signs, posters, labels, or screen text; dialogue and subtitles are post-production reference only.";
        private const string StoryboardVideoNoVisibleTextNegativePrompt = "subtitles, captions, visible text, watermark";

        public static bool ValidateBeforeRun(WorkflowDocument document, out string errorMessage)
        {
            var outlineNodes = document.Nodes.Where(node => node.Type == WorkflowNodeCatalog.Outline).ToList();
            var scriptNodes = document.Nodes.Where(node => node.Type == WorkflowNodeCatalog.Script).ToList();

            if (outlineNodes.Count > 0 && scriptNodes.Count > 0)
            {
                var connected = document.Edges.Any(edge =>
                    outlineNodes.Any(node => node.Id == edge.From) &&
                    scriptNodes.Any(node => node.Id == edge.To));

                var hasGeneratedOutline = outlineNodes.Any(node => !string.IsNullOrWhiteSpace(node.Output));
                if (!connected && !hasGeneratedOutline)
                {
                    errorMessage = "Connect the Story Outline node to the Story Script node before executing.";
                    return false;
                }
            }

            errorMessage = string.Empty;
            return true;
        }

        public static List<WorkflowNode> ComputeExecutionOrder(WorkflowDocument document)
        {
            var inDegree = document.Nodes.ToDictionary(node => node.Id, _ => 0);
            var graph = document.Nodes.ToDictionary(node => node.Id, _ => new List<string>());

            foreach (var edge in document.Edges)
            {
                if (!graph.ContainsKey(edge.From) || !inDegree.ContainsKey(edge.To))
                {
                    continue;
                }

                graph[edge.From].Add(edge.To);
                inDegree[edge.To]++;
            }

            var queue = new Queue<string>(document.Nodes
                .Where(node => inDegree[node.Id] == 0)
                .Select(node => node.Id));
            var result = new List<WorkflowNode>();

            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                var node = document.Nodes.FirstOrDefault(item => item.Id == currentId);
                if (node == null)
                {
                    continue;
                }

                result.Add(node);
                foreach (var nextId in graph[currentId])
                {
                    inDegree[nextId]--;
                    if (inDegree[nextId] == 0)
                    {
                        queue.Enqueue(nextId);
                    }
                }
            }

            if (result.Count == document.Nodes.Count)
            {
                return result;
            }

            foreach (var node in document.Nodes)
            {
                if (result.All(item => item.Id != node.Id))
                {
                    result.Add(node);
                }
            }

            return result;
        }

        public static string CollectUpstreamOutput(WorkflowDocument document, WorkflowNode node)
        {
            return string.Join(
                Environment.NewLine,
                CollectUpstreamNodes(document, node)
                    .Select(candidate => candidate.Output)
                    .Where(output => !string.IsNullOrWhiteSpace(output))
                    .Cast<string>());
        }

        public static List<WorkflowNode> CollectUpstreamNodes(WorkflowDocument document, WorkflowNode node)
        {
            var upstreamNodes = document.Edges
                .Where(edge => edge.To == node.Id)
                .Select(edge => document.Nodes.FirstOrDefault(candidate => candidate.Id == edge.From))
                .Where(candidate => candidate != null)
                .Cast<WorkflowNode>()
                .ToList();

            AddFallbackUpstreamNode(document, node, upstreamNodes);
            return upstreamNodes;
        }

        private static void AddFallbackUpstreamNode(WorkflowDocument document, WorkflowNode node, List<WorkflowNode> upstreamNodes)
        {
            if (node.Type != WorkflowNodeCatalog.Script)
            {
                return;
            }

            if (upstreamNodes.Any(candidate => candidate.Type == WorkflowNodeCatalog.Outline))
            {
                return;
            }

            var fallbackOutline = document.Nodes
                .Where(candidate => candidate.Id != node.Id && candidate.Type == WorkflowNodeCatalog.Outline)
                .OrderBy(candidate => string.IsNullOrWhiteSpace(candidate.Output) ? 0 : 1)
                .ThenBy(candidate => document.Nodes.IndexOf(candidate))
                .LastOrDefault();

            if (fallbackOutline != null)
            {
                upstreamNodes.Insert(0, fallbackOutline);
            }
        }

        public static List<string> CollectUpstreamArtifactPaths(WorkflowDocument document, WorkflowNode node, string? artifactKind = null)
        {
            return CollectUpstreamNodes(document, node)
                .Where(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.ArtifactPath) &&
                    (artifactKind == null || string.Equals(candidate.ArtifactKind, artifactKind, StringComparison.OrdinalIgnoreCase)))
                .Select(candidate => candidate.ArtifactPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool IsTextNode(string nodeType)
        {
            return nodeType == WorkflowNodeCatalog.Outline ||
                   nodeType == WorkflowNodeCatalog.Script ||
                   nodeType == WorkflowNodeCatalog.CreativeDescription ||
                   nodeType == WorkflowNodeCatalog.StoryboardBreakdown;
        }

        public static ModelCategory? GetModelCategory(string nodeType)
        {
            if (nodeType == WorkflowNodeCatalog.TextToImage ||
                nodeType == WorkflowNodeCatalog.ImageToImage)
            {
                return ModelCategory.Image;
            }

            if (nodeType == WorkflowNodeCatalog.TextToVideo || nodeType == WorkflowNodeCatalog.TextImageToVideo)
            {
                return ModelCategory.Video;
            }

            if (IsTextNode(nodeType))
            {
                return ModelCategory.Text;
            }

            if (nodeType == WorkflowNodeCatalog.CharacterView ||
                nodeType == WorkflowNodeCatalog.CharacterDescription ||
                nodeType == WorkflowNodeCatalog.StoryboardImage)
            {
                return ModelCategory.Image;
            }

            if (nodeType == WorkflowNodeCatalog.StoryboardVideo)
            {
                return ModelCategory.Video;
            }

            return null;
        }

        public static IReadOnlyList<ModelCategory> GetRequiredModelCategories(string nodeType)
        {
            if (string.Equals(nodeType, WorkflowNodeCatalog.TextToImage, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(nodeType, WorkflowNodeCatalog.ImageToImage, StringComparison.OrdinalIgnoreCase))
            {
                return new[] { ModelCategory.Text, ModelCategory.Image };
            }

            if (string.Equals(nodeType, WorkflowNodeCatalog.TextToVideo, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(nodeType, WorkflowNodeCatalog.TextImageToVideo, StringComparison.OrdinalIgnoreCase))
            {
                return new[] { ModelCategory.Text, ModelCategory.Video };
            }

            if (string.Equals(nodeType, WorkflowNodeCatalog.CharacterView, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(nodeType, WorkflowNodeCatalog.CharacterDescription, StringComparison.OrdinalIgnoreCase))
            {
                return new[] { ModelCategory.Text, ModelCategory.Image };
            }

            if (string.Equals(nodeType, WorkflowNodeCatalog.StoryboardVideo, StringComparison.OrdinalIgnoreCase))
            {
                return new[] { ModelCategory.Text, ModelCategory.Video };
            }

            var category = GetModelCategory(nodeType);
            return category == null
                ? Array.Empty<ModelCategory>()
                : new[] { category.Value };
        }

        public static void ApplyResult(WorkflowNode node, string input, string result)
        {
            if (IsTextNode(node.Type))
            {
                node.ArtifactPath = string.Empty;
                node.ArtifactKind = string.Empty;
                node.Output = NormalizeTextResult(node.Type, result);
                return;
            }

            var parametersText = DescribeParameters(node);
            var prefix = $"[{node.Type}]";
            var inputText = string.IsNullOrWhiteSpace(input)
                ? string.Empty
                : $"{Environment.NewLine}{Environment.NewLine}Upstream input:{Environment.NewLine}{input}";
            node.ArtifactPath = string.Empty;
            node.ArtifactKind = string.Empty;
            node.Output = $"{prefix}{Environment.NewLine}Parameters:{parametersText}{inputText}{Environment.NewLine}Result:{result}";
        }

        public static void ApplyArtifactResult(WorkflowNode node, string input, string artifactPath, string artifactKind, string summary)
        {
            var parametersText = DescribeParameters(node);
            var prefix = $"[{node.Type}]";
            var inputText = string.IsNullOrWhiteSpace(input)
                ? string.Empty
                : $"{Environment.NewLine}{Environment.NewLine}Upstream input:{Environment.NewLine}{input}";
            node.ArtifactPath = artifactPath ?? string.Empty;
            node.ArtifactKind = artifactKind ?? string.Empty;
            node.Output =
                $"{prefix}{Environment.NewLine}Parameters:{parametersText}" +
                $"{inputText}" +
                $"{Environment.NewLine}File type:{artifactKind}" +
                $"{Environment.NewLine}File path:{artifactPath}" +
                $"{Environment.NewLine}Result:{summary}";
        }

        public static void ApplyStructuredArtifactResult(WorkflowNode node, string textOutput, string artifactPath, string artifactKind)
        {
            node.ArtifactPath = artifactPath ?? string.Empty;
            node.ArtifactKind = artifactKind ?? string.Empty;
            node.Output = NormalizeTextResult(node.Type, textOutput);
        }

        public static void ExecuteMockNode(WorkflowNode node, string input)
        {
            if (node.Type == WorkflowNodeCatalog.LocalAsset)
            {
                node.ArtifactPath = node.Params?.Input ?? string.Empty;
                node.ArtifactKind = "file";
            }

            ApplyResult(node, input, GenerateMockResult(node, input));
        }

        public static string NormalizeTextResult(string nodeType, string result)
        {
            return WorkflowResultParser.NormalizeTextResult(nodeType, result);
        }

        public static bool TryExtractJsonPayload(string text, out string jsonPayload)
        {
            return WorkflowResultParser.TryExtractJsonPayload(text, out jsonPayload);
        }

        public static string BuildTextPrompt(WorkflowNode node, string input)
        {
            return WorkflowPromptBuilder.BuildTextPrompt(node, input);
        }

        public static bool SyncCharacterDesignEntries(WorkflowNode node, string outlineText, string characterDescriptionText, int limit = 12)
        {
            return WorkflowCharacterParser.SyncCharacterDesignEntries(node, outlineText, characterDescriptionText, limit);
        }

        public static List<CharacterDesignEntry> ExtractCharacterDesignEntries(string outlineText, string characterDescriptionText, int limit = 12)
        {
            return WorkflowCharacterParser.ExtractCharacterDesignEntries(outlineText, characterDescriptionText, limit);
        }

        public static string BuildCharacterProfilePrompt(WorkflowNode node, string input, CharacterDesignEntry entry)
        {
            var styleDesc = ResolveCharacterDesignStyleDescriptor(node);
            var styleDescChinese = ResolveCharacterDesignStyleDescriptorChinese(node);
            var builder = new StringBuilder();
            builder.AppendLine("Goal: Based on the upstream story outline, script, and current character name, output a single JSON object ready for character design.");
            builder.AppendLine("Output only one JSON object. No explanations, no ```json``` fences.");
            builder.AppendLine("If the upstream includes '## Character List', prioritize the name, role positioning, appearance traits, personality labels, core goals, and relationships from that section.");
            builder.AppendLine("Keep the character name exactly as defined upstream, and refine the description so it is directly usable for subsequent expression-sheet and turnaround-sheet generation.");
            builder.AppendLine("Required fields:");
            builder.AppendLine("{");
            builder.AppendLine("  \"name\": \"Character name\",");
            builder.AppendLine("  \"alias\": \"Title or alias\",");
            builder.AppendLine("  \"role\": \"Character role\",");
            builder.AppendLine("  \"basicStats\": \"Age, gender, build, hairstyle, facial features, clothing\",");
            builder.AppendLine("  \"profession\": \"Profession and identity\",");
            builder.AppendLine("  \"background\": \"Background or key life events\",");
            builder.AppendLine("  \"personality\": \"Personality and temperament\",");
            builder.AppendLine("  \"motivation\": \"Core motivation\",");
            builder.AppendLine("  \"values\": \"Values, principles, beliefs\",");
            builder.AppendLine("  \"weakness\": \"Weaknesses, fears, obsessions\",");
            builder.AppendLine("  \"relationships\": \"Key relationships, summarized in Chinese\",");
            builder.AppendLine("  \"habits\": \"Habits, interests, or signature mannerisms\",");
            builder.AppendLine("  \"visualTags\": \"Visual keywords in Chinese, comma-separated\",");
            builder.AppendLine("  \"appearancePrompt\": \"English image prompt suitable for character design generation\",");
            builder.AppendLine("  \"costumeNotes\": \"Costume and accessory notes\",");
            builder.AppendLine("  \"actingNotes\": \"Pose, expression, and action keywords suitable for on-camera performance\"");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("Gender consistency requirements:");
            builder.AppendLine("- If the character is male, appearancePrompt must specify adult male, masculine facial structure, masculine jawline, male body proportions, flat chest — strictly no feminine features or girlish appearance.");
            builder.AppendLine("- If the character is female, appearancePrompt must specify adult female, feminine facial structure — do not misrepresent as male physique or facial features.");
            builder.AppendLine("- Clothing, hairstyle, and accessories must serve as consistent anchors for the subsequent expression sheet and turnaround sheet — no drift.");
            builder.AppendLine();
            builder.AppendLine("Single outfit lock requirements:");
            builder.AppendLine("- basicStats, appearancePrompt, and costumeNotes must describe ONE exact outfit anchor only.");
            builder.AppendLine("- If upstream gives clothing examples or options, choose one primary outfit and discard the other alternatives.");
            builder.AppendLine("- Do not write multiple clothing choices with or, slash, such as, for example, e.g., etc., 如, 例如, 比如, 等, /, or 、.");
            builder.AppendLine("- costumeNotes must be concrete and repeatable: exact visible top or outerwear, collar or neckline, lower garment, shoes, and wearable accessories.");
            builder.AppendLine();
            builder.AppendLine("Visual style requirements:");
            builder.AppendLine($"- The selected visual style is: {styleDescChinese}.");
            builder.AppendLine($"- appearancePrompt must explicitly use this style: {styleDesc}.");
            builder.AppendLine("- Do not default to 2D anime unless the selected visual style is anime.");
            builder.AppendLine();
            builder.AppendLine($"Current character: {entry.Name}");
            if (!string.IsNullOrWhiteSpace(entry.RoleType))
            {
                builder.AppendLine($"Character mode: {entry.RoleType}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Summary))
            {
                builder.AppendLine($"Extracted summary: {entry.Summary}");
            }

            if (!string.IsNullOrWhiteSpace(input))
            {
                builder.AppendLine();
                builder.AppendLine("Upstream content:");
                builder.AppendLine(input.Trim());
            }

            return builder.ToString().Trim();
        }

        public static string BuildCharacterExpressionPrompt(WorkflowNode node, CharacterDesignEntry entry, string input)
        {
            var styleDesc = ResolveCharacterDesignStyleDescriptor(node);
            var builder = new StringBuilder();
            builder.AppendLine("You are a senior character prompt engineer.");
            builder.AppendLine("Return one final English image prompt only.");
            builder.AppendLine("Create a master prompt for ONE isolated close-up character portrait only.");
            builder.AppendLine("The app will render expression variants separately and compose the final board in code, so the returned prompt must never describe a board layout.");
            builder.AppendLine("Lock identity, hairstyle, hairline, bangs, face shape, eyebrows, eyes, nose, mouth, skin tone, age, gender, and color palette.");
            builder.AppendLine($"The style must be: {styleDesc}.");
            builder.AppendLine("Use a seamless plain light gray or white studio background, clean production-reference look, even lighting, no dramatic lighting.");
            builder.AppendLine("Background lock: every expression portrait must have an empty uniform studio backdrop only. Treat profession, workplace, and story setting as identity context only; never draw scenery, vehicles, food trucks, rooms, storefronts, doors, windows, signs, posters, furniture, shelves, plants, tables, props, or any background object.");
            builder.AppendLine("Do not describe a collage, grid, panel layout, expression sheet, contact sheet, multiple heads, duplicate faces, or multiple characters. Do not output JSON. Do not explain.");
            builder.AppendLine();
            AppendCharacterEntryContext(builder, entry);
            AppendCharacterConsistencyAnchor(builder, entry, fullBody: false);
            AppendCharacterGenderLock(builder, entry, fullBody: false);
            if (!string.IsNullOrWhiteSpace(input))
            {
                builder.AppendLine();
                builder.AppendLine("Upstream context:");
                builder.AppendLine(input.Trim());
            }

            builder.AppendLine();
            builder.AppendLine("Need: exactly one close-up head-and-shoulders portrait, exactly one head, exactly one face, exactly one person, face, neck, collar neckline and top of shoulders only, crop at the collarbone, shoulders-above framing, never show chest, bust, torso, clothing body, jacket buttons below collarbone, waist, belt, pants, hips, hands, or arms, centered framing, same camera distance, same lens feeling, same soft light, production-reference quality.");
            builder.AppendLine("Important: focus on face, eyes, mouth, eyebrows, and hairstyle. Do not emphasize hands, torso, props, or full outfit.");
            builder.AppendLine("Important: keep the exact same hairstyle, same fringe and hair volume, same facial proportions, same eye design, same colors, same age in every later expression variant.");
            builder.AppendLine("Important: never alter face shape, jawline, cheekbone structure, nose bridge, mouth width, hairline, bangs, hair length, hair volume, hair parting, or hairstyle silhouette between expression cells.");
            if (CharacterPromptTextBuilder.DetectGender(entry) == CharacterGenderHint.Male)
            {
                builder.AppendLine("Important: if the male character has facial hair, keep the exact same beard, mustache, stubble shape, length, density, placement, and color in every expression cell; never add or remove facial hair between cells.");
            }
            builder.AppendLine($"End with: {styleDesc}, one close-up head-and-shoulders portrait only, one head only, one face only, one person only, face, neck, collar neckline and top of shoulders only, crop at the collarbone, shoulders-above framing, no chest, no bust, no torso, no arms, centered composition, seamless plain light gray or white background only, no environment, no vehicle, no room, no storefront, no props in background, consistent identity, same exact hairstyle, no waist, no belt, no pants, no hips, no full torso, no grid, no contact sheet, no duplicate face, no hands, no props, no text, no watermark.");
            return builder.ToString().Trim();
        }

        public static string BuildCharacterThreeViewPrompt(WorkflowNode node, CharacterDesignEntry entry, string input)
        {
            var styleDesc = ResolveCharacterDesignStyleDescriptor(node);
            var builder = new StringBuilder();
            builder.AppendLine("You are a senior character turnaround prompt engineer.");
            builder.AppendLine("Return one final English image prompt only.");
            builder.AppendLine("Create a master prompt for ONE consistent full-body character turnaround reference.");
            builder.AppendLine("The system will render each view separately and compose the final three-view board later.");
            builder.AppendLine("Lock hairstyle, costume, accessories, body proportions, shoes, and silhouette.");
            builder.AppendLine($"The style must be: {styleDesc}.");
            builder.AppendLine("Use a seamless plain light gray or white studio background, orthographic reference feel, even lighting, no dramatic shadow.");
            builder.AppendLine("Background lock: every view must have an empty uniform studio backdrop only. Treat profession, workplace, and story setting as costume/identity context only; never draw scenery, vehicles, food trucks, rooms, storefronts, doors, windows, signs, posters, furniture, shelves, plants, tables, props, or any background object.");
            builder.AppendLine("Do not describe a collage, grid, or multiple people. Do not output JSON. Do not explain.");
            builder.AppendLine();
            AppendCharacterEntryContext(builder, entry);
            AppendCharacterConsistencyAnchor(builder, entry, fullBody: true);
            AppendCharacterGenderLock(builder, entry, fullBody: true);
            if (!string.IsNullOrWhiteSpace(input))
            {
                builder.AppendLine();
                builder.AppendLine("Upstream context:");
                builder.AppendLine(input.Trim());
            }

            builder.AppendLine();
            builder.AppendLine("Need: full body design reference, every view must show the complete character from the top of the head to the soles of the shoes, neutral standing pose, arms relaxed at sides, hands visible, empty hands, no handheld props, no worn or carried bags, readable costume breakdown, clear body proportions, production-ready reference.");
            builder.AppendLine("Mandatory full-body framing: include the complete head, torso, both legs, both feet, and shoes inside the frame with small margin around head and feet. Never output a portrait, bust, half-body, waist-up, knee-up, cropped feet, cropped head, or any partial-body view.");
            builder.AppendLine("Important: all views must use the exact same outfit, same outerwear, same inner shirt, same pants, same shoes, same accessories worn on the body, same body proportions, and same color palette.");
            builder.AppendLine("Important: clothing structure must remain perfectly identical between front, side, and back views. No redesign, no color change, no accessory change, no missing layers.");
            builder.AppendLine("Important: the final turnaround must contain only three views: front view, side view, and back view. Do not include three-quarter view, extra angle, inset portrait, extra small panel, text label, or a fourth view.");
            builder.AppendLine("Important: the side view must be a true 90-degree standing profile, with the body facing left or right, shoulders and hips stacked in profile, only one eye and one ear visible, toes pointing sideways, no front torso, and no face looking at the camera.");
            builder.AppendLine("Important: remove all handheld objects and worn bags even if upstream text or reference images mention them. No backpack, no shoulder bag, no tote bag, no handbag, no purse, no satchel, no messenger bag, no crossbody bag, no sling bag, no waist bag, no pouch, no luggage, no bag strap, no shoulder strap, no crossbody strap, no backpack straps, no bag handles, no bag hardware, no weapon, no thermos, no umbrella, no suitcase, no staff, no box, no phone in hands.");
            builder.AppendLine("Important: only the character may appear. No microphone, mic stand, camera, tripod, light stand, boom pole, phone, tablet, tool, weapon, umbrella, staff, cane, suitcase, box, paper, folder, clipboard, desk, chair, stool, podium, cable, badge prop, handheld prop, floating prop, foreground prop, background prop, object beside the character, object near the character, or profession equipment.");
            builder.AppendLine("Important: remove all background context even if upstream text mentions a workplace, street, shop, vehicle, food truck, room, or story location. Only the character may remain.");
            builder.AppendLine($"End with: {styleDesc}, orthographic turnaround reference, complete full body visible from head to toe, feet and shoes fully visible, empty hands, no handheld props, no external objects, no microphone, no mic stand, no camera, no tripod, no phone, no tool, no weapon, no bag, no backpack, no shoulder bag, no crossbody bag, no bag straps, seamless plain light gray or white background only, no environment, no vehicle, no room, no storefront, no props in background, same exact outfit across all views, same exact shoes, same exact wearable clothing accessories except bags, same exact hairstyle, no text, no watermark.");
            return builder.ToString().Trim();
        }
        public static string BuildCharacterExpressionNegativePrompt(WorkflowNode node, CharacterDesignEntry entry)
        {
            var styleNegatives = ResolveCharacterDesignStyleNegativePrefix(node);
            var negatives = new List<string>
            {
                "different character",
                "different face",
                "different hairstyle",
                "different identity",
                "different hair color",
                "different outfit",
                "different clothes",
            };

            if (!string.IsNullOrWhiteSpace(styleNegatives))
            {
                negatives.Insert(0, styleNegatives);
            }

            negatives.AddRange(GetCharacterGenderNegativeTags(entry));
            return string.Join(", ", negatives.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        public static string BuildCharacterThreeViewNegativePrompt(WorkflowNode node, CharacterDesignEntry entry)
        {
            var styleNegatives = ResolveCharacterDesignStyleNegativePrefix(node);
            var negatives = new List<string>
            {
                "different character",
                "different face",
                "different hairstyle",
                "different outfit",
                "different clothes",
            };

            if (!string.IsNullOrWhiteSpace(styleNegatives))
            {
                negatives.Insert(0, styleNegatives);
            }

            negatives.AddRange(GetCharacterGenderNegativeTags(entry));
            return string.Join(", ", negatives.Distinct(StringComparer.OrdinalIgnoreCase));
        }
        public static string BuildImageNegativePrompt(WorkflowNode node, string input)
        {
            return node.Type switch
            {
                WorkflowNodeCatalog.CharacterView or WorkflowNodeCatalog.CharacterDescription =>
                    "photorealistic, 3d render, cgi, bad anatomy, extra limbs, blurry, lowres",
                WorkflowNodeCatalog.StoryboardImage =>
                    "lowres, blurry, bad anatomy, extra limbs, multi panel, " + StoryboardVisibleSimplifiedChineseNegativePrompt,
                _ =>
                    "lowres, blurry, bad anatomy, text, watermark"
            };
        }

        public static string BuildCharacterDesignOutput(WorkflowNode node)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var payload = new
            {
                selectedCharacter = node.Params.SelectedCharacterName,
                characterCount = node.Params.CharacterEntries.Count,
                characters = node.Params.CharacterEntries.Select(entry => new
                {
                    name = entry.Name,
                    alias = entry.Alias,
                    roleType = entry.RoleType,
                    summary = entry.Summary,
                    basicStats = entry.BasicStats,
                    profession = entry.Profession,
                    background = entry.Background,
                    personality = entry.Personality,
                    motivation = entry.Motivation,
                    values = entry.Values,
                    weakness = entry.Weakness,
                    relationships = entry.Relationships,
                    habits = entry.Habits,
                    visualTags = entry.VisualTags,
                    appearancePrompt = entry.AppearancePrompt,
                    costumeNotes = entry.CostumeNotes,
                    actingNotes = entry.ActingNotes,
                    profileStatus = entry.ProfileStatus.ToString(),
                    referencePortraitPath = entry.ReferencePortraitPath,
                    expressionSheetPath = entry.ExpressionSheetPath,
                    threeViewSheetPath = entry.ThreeViewSheetPath,
                    expressionStatus = entry.ExpressionStatus.ToString(),
                    threeViewStatus = entry.ThreeViewStatus.ToString(),
                }),
            };

            return JsonSerializer.Serialize(payload, WorkflowResultParser.ReadableJsonOptions);
        }

        public static string BuildStoryboardBreakdownOutput(WorkflowNode node)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);
            var normalizedShots = WorkflowStoryboardParser.NormalizeStoryboardShots(node.Params.StoryboardShots ?? new List<StoryboardShot>());
            var payload = new
            {
                totalShots = normalizedShots.Count,
                totalDuration = normalizedShots.Sum(shot => Math.Max(1, shot.DurationSeconds)),
                shots = normalizedShots.Select(shot => new
                {
                    id = shot.Id,
                    shotNumber = shot.ShotNumber,
                    duration = Math.Max(1, shot.DurationSeconds),
                    scene = shot.Scene,
                    characters = shot.Characters,
                    shotSize = shot.ShotSize,
                    cameraAngle = shot.CameraAngle,
                    cameraMovement = shot.CameraMovement,
                    visualDescription = shot.VisualDescription,
                    imagePrompt = shot.ImagePrompt,
                    dialogue = shot.Dialogue,
                    visualEffects = shot.VisualEffects,
                    audioEffects = shot.AudioEffects,
                    startTime = shot.StartTime,
                    endTime = shot.EndTime,
                    splitImagePath = shot.SplitImagePath,
                    sourceNodeId = shot.SourceNodeId,
                    sourcePage = shot.SourcePage,
                    panelIndex = shot.PanelIndex,
                }),
            };

            return JsonSerializer.Serialize(payload, WorkflowResultParser.ReadableJsonOptions);
        }

        public static List<StoryboardShot> ParseStoryboardShots(string text)
        {
            return WorkflowStoryboardParser.ParseStoryboardShots(text);
        }

        public static List<StoryboardShot> CollectStoryboardShots(WorkflowDocument document, WorkflowNode node, int limit = 48)
        {
            return WorkflowStoryboardParser.CollectStoryboardShots(CollectUpstreamNodes(document, node), node, limit);
        }

        public static int GetStoryboardShotsPerPage(string? gridLayout)
        {
            return WorkflowStoryboardParser.GetStoryboardShotsPerPage(gridLayout);
        }

        public static List<GeneratedScriptEpisode> ParseGeneratedScriptEpisodes(string text)
        {
            return WorkflowScriptParser.ParseGeneratedScriptEpisodes(text);
        }

        public static List<GeneratedScriptEpisode> NormalizeGeneratedScriptEpisodeCount(
            WorkflowNode node,
            string outlineInput,
            IReadOnlyList<GeneratedScriptEpisode> episodes)
        {
            return WorkflowScriptParser.NormalizeGeneratedScriptEpisodeCount(node, outlineInput, episodes);
        }

        public static string BuildScriptEpisodesOutput(IEnumerable<GeneratedScriptEpisode> episodes)
        {
            return WorkflowScriptParser.BuildScriptEpisodesOutput(episodes);
        }

        public static string BuildImagePrompt(WorkflowNode node, string input)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var builder = new StringBuilder();
            builder.AppendLine("You are a prompt engineer for image generation.");
            builder.AppendLine("Return one final English prompt only. No bullet points. No JSON. No explanation. No quotation marks.");
            builder.AppendLine($"Node type: {node.Type}");

            if (node.Type == WorkflowNodeCatalog.CharacterView)
            {
                builder.AppendLine("Goal: Generate a main character portrait / character view, emphasizing appearance, clothing, pose, expression, composition, and consistent art style.");
                builder.AppendLine("Prompt must include: visual style, character appearance, clothing, pose, facial expression, framing, lighting, background, rendering quality.");
            }
            else if (node.Type == WorkflowNodeCatalog.StoryboardImage)
            {
                var gridLayout = string.Equals(node.Params.StoryboardGridLayout, "2x3", StringComparison.Ordinal)
                    ? "six-panel storyboard page, 2 columns by 3 rows"
                    : "nine-panel storyboard page, 3 columns by 3 rows";
                var orientation = string.Equals(node.Params.StoryboardPanelOrientation, "9:16", StringComparison.Ordinal)
                    ? "portrait panels, each panel framed like 9:16"
                    : "landscape panels, each panel framed like 16:9";
                builder.AppendLine("Goal: Generate a full-page storyboard image, not individual single-shot frames.");
                builder.AppendLine($"Required output: {gridLayout}, {orientation}.");
                builder.AppendLine("Prompt must include: panel layout, shot progression, characters, action beats, composition, shot type, camera angle, lighting, mood, environment, continuity, cinematic quality.");
                builder.AppendLine("Emphasize that this is a multi-panel storyboard page. Maintain character consistency, clothing consistency, and shot rhythm continuity across all panels.");
            }

            if (!string.IsNullOrWhiteSpace(node.Params.Input))
            {
                builder.AppendLine();
                builder.AppendLine("Node input:");
                builder.AppendLine(node.Params.Input.Trim());
            }

            if (!string.IsNullOrWhiteSpace(input))
            {
                builder.AppendLine();
                builder.AppendLine("Upstream text:");
                builder.AppendLine(input.Trim());
            }

            if (node.Type == WorkflowNodeCatalog.CharacterView || node.Type == WorkflowNodeCatalog.StoryboardImage)
            {
                builder.AppendLine();
                builder.AppendLine("Prioritize appearancePrompt / visualPrompt / visualTags / negativePrompt fields from upstream JSON. Synthesize them into a single high-quality English image prompt.");
                builder.AppendLine(node.Type == WorkflowNodeCatalog.StoryboardImage
                    ? "Append to the end of the prompt: high detail, cinematic lighting, clean composition, no watermark; any visible text must be exact Simplified Chinese only, no English, no Traditional Chinese, no garbled text."
                    : "Append to the end of the prompt: high detail, cinematic lighting, clean composition, no watermark, no text.");
            }

            return builder.ToString().Trim();
        }

        public static string BuildVideoPrompt(WorkflowNode node, string input)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var builder = new StringBuilder();
            builder.AppendLine("You are a cinematic video prompt engineer.");
            builder.AppendLine("Return one final English video prompt only. No bullet points. No JSON. No explanation.");
            builder.AppendLine($"Node type: {node.Type}");

            if (node.Type == WorkflowNodeCatalog.StoryboardVideo)
            {
                builder.AppendLine("Goal: Generate short video / storyboard video clips, emphasizing camera motion, character action, pacing, transitions, and environmental changes.");
            }
            else if (node.Type == WorkflowNodeCatalog.VideoCollection)
            {
                builder.AppendLine("Goal: Synthesize upstream assets into an executable video compilation edit plan.");
            }

            if (!string.IsNullOrWhiteSpace(node.Params.Input))
            {
                builder.AppendLine();
                builder.AppendLine("Node input:");
                builder.AppendLine(node.Params.Input.Trim());
            }

            if (!string.IsNullOrWhiteSpace(input))
            {
                builder.AppendLine();
                builder.AppendLine("Upstream text:");
                builder.AppendLine(input.Trim());
            }

            builder.AppendLine();
            builder.AppendLine("Prioritize reading shot, scene, visualDescription, dialogue, cameraLanguage, visualEffects fields from upstream JSON.");
            builder.AppendLine("Complete camera movement, composition, subject motion, lighting, mood, transitions, cinematic quality.");
            builder.AppendLine("Keep it concise and production-ready.");
            return builder.ToString().Trim();
        }

        public static string BuildStoryboardVideoPromptDraft(
            WorkflowDocument document,
            WorkflowNode node,
            IReadOnlyList<StoryboardShot> selectedShots,
            string upstreamInput)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var normalizedShots = (selectedShots ?? Array.Empty<StoryboardShot>())
                .Where(shot => shot != null)
                .OrderBy(shot => Math.Max(1, shot.ShotNumber))
                .Select(shot => shot.Clone())
                .ToList();
            var builder = new StringBuilder();
            builder.AppendLine("风格：电影真实质感，真实光照，景深自然，镜头语言明确");
            builder.AppendLine("全局规则：");
            builder.AppendLine("- 保持角色身份、发型、服装、道具一致");
            builder.AppendLine("- 场景连续性、雨光方向、道具位置一致");
            builder.AppendLine("- 视频画面内不生成任何文字，尤其不要底部字幕、英文、伪文字、乱码和招牌小字；对白与字幕只作为后期剪辑信息");
            builder.AppendLine("- 无水印、无标识");
            builder.AppendLine("- 每个镜头必须有明确运镜或明确的静态构图理由，不能整段都写成固定镜头");

            var characterEntries = CollectStoryboardCharacterEntries(document, node, normalizedShots);
            if (characterEntries.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("角色一致性：");
                foreach (var entry in characterEntries)
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(entry.Name))
                    {
                        parts.Add(entry.Name.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(entry.BasicStats))
                    {
                        parts.Add(entry.BasicStats.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(entry.AppearancePrompt))
                    {
                        parts.Add(entry.AppearancePrompt.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(entry.CostumeNotes))
                    {
                        parts.Add($"服装：{entry.CostumeNotes.Trim()}");
                    }

                    if (parts.Count > 0)
                    {
                        var displayText = KeepSimplifiedChineseVisibleTextOnly(string.Join("，", parts));
                        if (!string.IsNullOrWhiteSpace(displayText))
                        {
                            builder.AppendLine($"- {displayText}");
                        }
                    }
                }
            }

            foreach (var shot in normalizedShots)
            {
                builder.AppendLine();
                builder.AppendLine($"镜头 {Math.Max(1, shot.ShotNumber)}：");
                builder.AppendLine($"时长：{Math.Max(1, shot.DurationSeconds)}秒");
                builder.AppendLine($"场景：{BuildStoryboardVideoChineseSceneCue(shot)}");
                builder.AppendLine($"出场角色：{BuildStoryboardVideoChineseCharactersCue(shot)}");
                builder.AppendLine($"景别：{GetStoryboardChineseDisplayCue(shot.ShotSize, "中景")}");
                builder.AppendLine($"机位：{BuildStoryboardVideoChineseAngleCue(shot)}");
                builder.AppendLine($"镜头运动：{BuildStoryboardVideoChineseCameraCue(shot)}");
                builder.AppendLine($"画面：{BuildStoryboardVideoChineseActionCue(shot)}");
                builder.AppendLine($"光影：{BuildStoryboardVideoChineseLightingCue(shot)}");
                builder.AppendLine($"视觉效果：{BuildStoryboardVideoChineseOptionalCue(shot.VisualEffects)}");
                builder.AppendLine($"声音：{(node.Params.StoryboardVideoNeedSound ? BuildStoryboardVideoChineseOptionalCue(shot.AudioEffects) : "无")}");
                builder.AppendLine($"对白/字幕：{BuildStoryboardVideoChineseOptionalCue(shot.Dialogue)}");
            }

            return builder.ToString().Trim();
        }

        public static string BuildStoryboardVideoModelPromptDraft(
            WorkflowDocument document,
            WorkflowNode node,
            IReadOnlyList<StoryboardShot> selectedShots,
            string upstreamInput)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var normalizedShots = (selectedShots ?? Array.Empty<StoryboardShot>())
                .Where(shot => shot != null)
                .OrderBy(shot => Math.Max(1, shot.ShotNumber))
                .Select(shot => shot.Clone())
                .ToList();

            var builder = new StringBuilder();
            builder.AppendLine("[Global Rules]");
            builder.AppendLine("Keep character identity, hairstyle, costume, props, and silhouette consistent across every shot.");
            builder.AppendLine("Maintain scene continuity, rain direction, lighting direction, and prop positions.");
            builder.AppendLine(StoryboardVideoNoVisibleTextRule);
            builder.AppendLine("Keep all surfaces clean and natural.");
            builder.AppendLine("Cinematic realism, realistic lighting, natural depth of field, film texture.");
            if (node.Params.StoryboardVideoNeedSound)
            {
                builder.AppendLine("For audio, use each shot's SFX as reference only; do not add unrelated music.");
            }

            var characterEntries = CollectStoryboardCharacterEntries(document, node, normalizedShots);
            foreach (var entry in characterEntries)
            {
                var parts = new List<string>();
                var appearancePrompt = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.AppearancePrompt);
                var costumeNotes = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.CostumeNotes);
                if (!string.IsNullOrWhiteSpace(appearancePrompt))
                {
                    parts.Add(TranslateStoryboardTextToPromptFragment(appearancePrompt));
                }
                else if (!string.IsNullOrWhiteSpace(entry.CompactSummary))
                {
                    parts.Add(TranslateStoryboardTextToPromptFragment(entry.CompactSummary));
                }

                if (!string.IsNullOrWhiteSpace(costumeNotes))
                {
                    parts.Add($"costume: {TranslateStoryboardTextToPromptFragment(costumeNotes)}, one exact outfit only");
                }

                if (parts.Count > 0)
                {
                    builder.AppendLine($"Character lock - {entry.Name}: {string.Join("; ", parts)}");
                }
            }

            foreach (var shot in normalizedShots)
            {
                builder.AppendLine();
                builder.AppendLine(BuildStoryboardVideoEnglishShotHeader(shot));
                builder.AppendLine($"Shot: {TranslateStoryboardShotSize(shot.ShotSize)}, {BuildStoryboardVideoEnglishLensCue(shot)}");
                builder.AppendLine($"Camera: {BuildStoryboardVideoEnglishCameraCue(shot)}; {TranslateStoryboardCameraAngle(shot.CameraAngle)}");
                builder.AppendLine($"Character action: {BuildStoryboardVideoEnglishActionCue(shot)}");
                builder.AppendLine($"Lighting: {BuildStoryboardVideoEnglishLightingCue(shot)}");
                builder.AppendLine($"VFX: {BuildStoryboardVideoEnglishOptionalCue(shot.VisualEffects)}");
                builder.AppendLine($"SFX: {(node.Params.StoryboardVideoNeedSound ? BuildStoryboardVideoEnglishSoundCue(shot.AudioEffects) : "none")}");
                builder.AppendLine($"Dialogue: {BuildStoryboardVideoEnglishDialogueCue(shot.Dialogue)}");
            }

            return builder.ToString().Trim();
        }

        public static string BuildStoryboardVideoModelPromptRequest(
            WorkflowDocument document,
            WorkflowNode node,
            IReadOnlyList<StoryboardShot> selectedShots,
            string upstreamInput)
        {
            var builder = new StringBuilder();
            var needSound = node.Params?.StoryboardVideoNeedSound == true;
            builder.AppendLine("You are a senior cinematic storyboard-to-video prompt engineer.");
            builder.AppendLine("Return plain English only. Do not output JSON. Do not explain.");
            builder.AppendLine("Rewrite the storyboard information below into the exact production prompt sheet format.");
            builder.AppendLine("Keep this structure exactly:");
            builder.AppendLine("[Global Rules]");
            builder.AppendLine("[Shot{number} - {scene} / {time} / {shot size}] {duration}s");
            builder.AppendLine("Shot:");
            builder.AppendLine("Camera:");
            builder.AppendLine("Character action:");
            builder.AppendLine("Lighting:");
            builder.AppendLine("VFX:");
            builder.AppendLine("SFX:");
            builder.AppendLine("Dialogue:");
            builder.AppendLine("Normal prompt instructions must be English. Use one compact text rule only: no visible text in video pixels; dialogue/subtitles are post-production reference only.");
            builder.AppendLine("Do not repeat subtitle/text bans across every shot. Keep the final prompt short, visual, and camera-action focused.");
            builder.AppendLine("If a reference image contains text or signage, say only: keep all surfaces clean and natural.");
            builder.AppendLine("If camera movement is missing, generic, or marked locked-off/fixed by default, infer a specific cinematic movement from the shot action and reference image. Do not make every shot fixed. Use still camera only when the shot has a clear dramatic reason.");
            builder.AppendLine(needSound
                ? "Audio is required. Use each shot's SFX as reference only; do not add unrelated music."
                : "No audio is needed. Use SFX: none and Dialogue: post-production note only; never render subtitles in the frame.");
            builder.AppendLine();
            builder.AppendLine("Draft to rewrite:");
            builder.AppendLine(BuildStoryboardVideoModelPromptDraft(document, node, selectedShots, upstreamInput));
            builder.AppendLine();
            builder.AppendLine("Original Chinese display sheet for translation context. Translate non-visible production instructions into English; keep dialogue/subtitle text as post-production notes only:");
            builder.AppendLine(BuildStoryboardVideoPromptDraft(document, node, selectedShots, upstreamInput));
            return builder.ToString().Trim();
        }

        private static string BuildStoryboardVideoChineseShotHeader(StoryboardShot shot)
        {
            var shotNumber = Math.Max(1, shot.ShotNumber);
            var scene = GetStoryboardChineseDisplayCue(shot.Scene, $"第{shotNumber}镜");
            var shotSize = GetStoryboardChineseDisplayCue(shot.ShotSize, "中景");
            return $"[第{shotNumber}镜-{scene}/{shotSize}]   {Math.Max(1, shot.DurationSeconds)}秒";
        }

        private static string BuildStoryboardVideoChineseSceneCue(StoryboardShot shot)
        {
            var shotNumber = Math.Max(1, shot.ShotNumber);
            var fallback = $"第{shotNumber}镜场景";
            var scene = GetStoryboardChineseDisplayCue(shot.Scene, fallback);
            return string.Equals(scene, $"第{shotNumber}镜", StringComparison.Ordinal) ||
                   string.Equals(scene, "第镜", StringComparison.Ordinal)
                ? fallback
                : scene;
        }

        private static string BuildStoryboardVideoChineseCharactersCue(StoryboardShot shot)
        {
            if (shot.Characters == null || shot.Characters.Count == 0)
            {
                return "按分镜画面中的主体角色";
            }

            var characters = string.Join("、", shot.Characters
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => KeepSimplifiedChineseVisibleTextOnly(value.Trim()))
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return string.IsNullOrWhiteSpace(characters) ? "按分镜画面中的主体角色" : characters;
        }

        private static string BuildStoryboardVideoChineseLensCue(StoryboardShot shot)
        {
            var characters = shot.Characters == null || shot.Characters.Count == 0
                ? string.Empty
                : string.Join("、", shot.Characters.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));
            if (!string.IsNullOrWhiteSpace(characters))
            {
                var chineseCharacters = KeepSimplifiedChineseVisibleTextOnly(characters);
                return string.IsNullOrWhiteSpace(chineseCharacters)
                    ? "按分镜主体构图"
                    : $"{chineseCharacters}在画面中，保持分镜构图";
            }

            return "按分镜主体构图";
        }

        private static string BuildStoryboardVideoChineseAngleCue(StoryboardShot shot)
        {
            return GetStoryboardChineseDisplayCue(shot.CameraAngle, "根据分镜构图选择平视、俯拍、仰拍或越肩机位");
        }

        private static string BuildStoryboardVideoChineseCameraCue(StoryboardShot shot)
        {
            var camera = GetStoryboardChineseDisplayCue(shot.CameraMovement, string.Empty);
            if (string.IsNullOrWhiteSpace(camera) || string.Equals(camera, "固定", StringComparison.Ordinal))
            {
                return "根据分镜画面推导具体运镜，优先使用轻微推进、横移、跟随、摇移或环绕来强化情绪；只有明确需要静止时才固定";
            }

            return $"{camera}，保持主体清晰并延续上一镜头节奏";
        }

        private static string BuildStoryboardVideoChineseActionCue(StoryboardShot shot)
        {
            return GetStoryboardChineseDisplayCue(shot.VisualDescription, "按当前分镜画面执行，保持角色动作和情绪连续");
        }

        private static string BuildStoryboardVideoChineseLightingCue(StoryboardShot shot)
        {
            if (!IsStoryboardNoneCue(shot.VisualEffects) && ContainsCjk(shot.VisualEffects))
            {
                var visibleChinese = KeepSimplifiedChineseVisibleTextOnly(shot.VisualEffects);
                return string.IsNullOrWhiteSpace(visibleChinese)
                    ? "保持当前场景光线方向，电影真实光照，景深自然"
                    : visibleChinese;
            }

            return "保持当前场景光线方向，电影真实光照，景深自然";
        }

        private static string BuildStoryboardVideoChineseOptionalCue(string value)
        {
            if (IsStoryboardNoneCue(value))
            {
                return "无";
            }

            if (!ContainsCjk(value))
            {
                return "无";
            }

            var visibleChinese = KeepSimplifiedChineseVisibleTextOnly(value);
            return string.IsNullOrWhiteSpace(visibleChinese) ? "无" : visibleChinese;
        }

        private static string BuildStoryboardVideoEnglishShotHeader(StoryboardShot shot)
        {
            var shotNumber = Math.Max(1, shot.ShotNumber);
            var scene = GetStoryboardEnglishCue(shot.Scene, $"scene {shotNumber}");
            return $"[Shot{shotNumber} - {scene} / {TranslateStoryboardShotSize(shot.ShotSize)}] {Math.Max(1, shot.DurationSeconds)}s";
        }

        private static string BuildStoryboardVideoEnglishLensCue(StoryboardShot shot)
        {
            var characters = shot.Characters == null || shot.Characters.Count == 0
                ? string.Empty
                : string.Join(", ", shot.Characters.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));
            return string.IsNullOrWhiteSpace(characters)
                ? "compose around the main subject from the selected storyboard reference"
                : $"characters on screen: {characters}; preserve their blocking from the storyboard reference";
        }

        private static string BuildStoryboardVideoEnglishCameraCue(StoryboardShot shot)
        {
            if (string.IsNullOrWhiteSpace(shot.CameraMovement) ||
                string.Equals(shot.CameraMovement.Trim(), "固定", StringComparison.OrdinalIgnoreCase))
            {
                return "infer a specific cinematic camera movement from the storyboard reference, such as a subtle dolly-in, tracking move, pan, follow shot, crane move or orbit; avoid locked-off framing unless the shot explicitly requires stillness";
            }

            return TranslateStoryboardCameraMovement(shot.CameraMovement);
        }

        private static string BuildStoryboardVideoEnglishActionCue(StoryboardShot shot)
        {
            var imagePrompt = GetStoryboardImagePromptText(shot);
            if (!string.IsNullOrWhiteSpace(imagePrompt) && !ContainsCjk(imagePrompt))
            {
                return SanitizeStoryboardVisualTextCue(imagePrompt);
            }

            return GetStoryboardEnglishCue(
                shot.VisualDescription,
                "use the selected storyboard panel as the visual reference; preserve blocking, emotion, posture, and subject motion");
        }

        private static string BuildStoryboardVideoEnglishLightingCue(StoryboardShot shot)
        {
            return GetStoryboardEnglishCue(
                shot.VisualEffects,
                "preserve the current scene lighting direction; cinematic realistic light, natural depth of field");
        }

        private static string BuildStoryboardVideoEnglishOptionalCue(string value)
        {
            if (IsStoryboardNoneCue(value))
            {
                return "none";
            }

            return GetStoryboardEnglishCue(value, "subtle production cue matching the storyboard reference");
        }

        private static string BuildStoryboardVideoEnglishSoundCue(string value)
        {
            if (IsStoryboardNoneCue(value))
            {
                return "none";
            }

            var translated = TranslateStoryboardSoundCueToEnglish(value);
            if (!string.IsNullOrWhiteSpace(translated))
            {
                return translated;
            }

            return GetStoryboardEnglishCue(value, "natural diegetic sound effects matching the visible action; no background music unless explicitly requested");
        }

        private static string TranslateStoryboardSoundCueToEnglish(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var text = CleanExtractedValue(value).Trim();
            var cues = new List<string>();
            bool wantsMusic = ContainsAny(text, "音乐", "配乐", "背景乐", "background music", "score", "underscore");

            void AddCue(string cue)
            {
                if (!string.IsNullOrWhiteSpace(cue) && !cues.Any(item => string.Equals(item, cue, StringComparison.OrdinalIgnoreCase)))
                {
                    cues.Add(cue);
                }
            }

            if (ContainsAny(text, "暴雨", "大雨", "雨声", "下雨", "heavy rain", "rainstorm", "rain"))
            {
                AddCue("heavy rain ambience");
            }

            if (ContainsAny(text, "雷", "雷声", "雷鸣", "thunder"))
            {
                AddCue("thunder claps");
            }

            if (ContainsAny(text, "脚步", "footstep"))
            {
                AddCue(ContainsAny(text, "湿", "wet") ? "wet footsteps" : "footsteps");
            }

            if (ContainsAny(text, "风", "呼啸", "wind"))
            {
                AddCue(ContainsAny(text, "呼啸", "howl", "whistle") ? "whistling wind" : "wind ambience");
            }

            if (ContainsAny(text, "鬼哭", "鬼叫", "嚎叫", "哭嚎", "ghost", "wail"))
            {
                AddCue("ghostly wails");
            }

            if (ContainsAny(text, "呼吸", "喘息", "breath"))
            {
                AddCue("tense breathing");
            }

            if (ContainsAny(text, "骨骼", "骨头", "摩擦", "crack", "bone"))
            {
                AddCue("bone cracking and grinding");
            }

            if (ContainsAny(text, "撞击", "冲击", "击中", "impact", "hit"))
            {
                AddCue("sharp impact sound");
            }

            if (ContainsAny(text, "低音", "紧张", "压抑", "tension", "tense"))
            {
                AddCue(wantsMusic ? "low tense background music" : "low tension ambience");
            }

            if (wantsMusic && !cues.Any(item => item.Contains("music", StringComparison.OrdinalIgnoreCase)))
            {
                AddCue("background music matching the scene mood");
            }

            if (cues.Count == 0)
            {
                return string.Empty;
            }

            if (!wantsMusic)
            {
                AddCue("diegetic sound effects only, no unrelated background music");
            }

            return string.Join(", ", cues);
        }

        private static bool ContainsAny(string value, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (var token in tokens)
            {
                if (!string.IsNullOrWhiteSpace(token) && value.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildStoryboardVideoEnglishDialogueCue(string value)
        {
            if (IsStoryboardNoneCue(value))
            {
                return "no dialogue";
            }

            var normalized = KeepSimplifiedChineseVisibleTextOnly(value);
            return ContainsCjk(normalized)
                ? $"dialogue reference only: {normalized}"
                : "no dialogue";
        }

        private static string GetStoryboardChineseDisplayCue(string value, string fallback)
        {
            var normalized = KeepSimplifiedChineseVisibleTextOnly(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return fallback;
            }

            return ContainsCjk(normalized) ? normalized : fallback;
        }

        private static string GetStoryboardEnglishCue(string value, string fallback)
        {
            if (IsStoryboardNoneCue(value))
            {
                return fallback;
            }

            var normalized = TranslateStoryboardTextToPromptFragment(value);
            if (string.IsNullOrWhiteSpace(normalized) || ContainsCjk(normalized))
            {
                return fallback;
            }

            return normalized;
        }

        private static bool IsStoryboardNoneCue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            var normalized = value.Trim();
            return string.Equals(normalized, "无", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "无对白", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "无字幕", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "n/a", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsCjk(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"[\u3400-\u9FFF]");
        }

        public static string BuildStoryboardVideoPromptRequest(
            WorkflowDocument document,
            WorkflowNode node,
            IReadOnlyList<StoryboardShot> selectedShots,
            string upstreamInput)
        {
            var builder = new StringBuilder();
            var outputEnglish = string.Equals(node.Params?.StoryboardVideoPromptLanguage, "en", StringComparison.OrdinalIgnoreCase);
            var needSound = node.Params?.StoryboardVideoNeedSound == true;
            if (outputEnglish)
            {
                builder.AppendLine("You are a senior cinematic storyboard-to-video prompt engineer.");
                builder.AppendLine("Return plain English only. Do not output JSON. Do not explain.");
                builder.AppendLine("Rewrite the storyboard information below into a production-ready video prompt sheet.");
                builder.AppendLine("Keep the exact structure:");
                builder.AppendLine("Style:");
                builder.AppendLine("Global Rules:");
                builder.AppendLine("Character Consistency:");
                builder.AppendLine("Shot 1:");
                builder.AppendLine("duration:");
                builder.AppendLine("scene:");
                builder.AppendLine("characters:");
                builder.AppendLine("shot size:");
                builder.AppendLine("camera angle:");
                builder.AppendLine("camera movement:");
                builder.AppendLine("visual:");
                builder.AppendLine("dialogue:");
                builder.AppendLine("vfx:");
                builder.AppendLine("sfx:");
                builder.AppendLine("Keep each shot concise, cinematic, and visually explicit.");
                builder.AppendLine("Use one compact text rule only: no visible text in video pixels; dialogue/subtitles are post-production reference only.");
                builder.AppendLine("Do not repeat subtitle/text bans. If a reference image already contains text or signage, say only: keep all surfaces clean and natural.");
                builder.AppendLine(needSound
                    ? "Audio is required. Keep ambience and sound effects specific; do not add unrelated music."
                    : "No audio is needed. Treat this as a silent video prompt and set dialogue/sfx to None when unnecessary.");
            }
            else
            {
                builder.AppendLine("你是资深短剧分镜转视频提示词工程师。");
                builder.AppendLine("只输出简体中文纯文本，不要 JSON，不要解释过程。");
                builder.AppendLine("把下方分镜资料改写成可直接用于视频生成的提示词表。");
                builder.AppendLine("保持以下结构：");
                builder.AppendLine("风格：");
                builder.AppendLine("全局规则：");
                builder.AppendLine("角色一致性：");
                builder.AppendLine("镜头 1：");
                builder.AppendLine("时长：");
                builder.AppendLine("场景：");
                builder.AppendLine("出场角色：");
                builder.AppendLine("景别：");
                builder.AppendLine("机位：");
                builder.AppendLine("镜头运动：");
                builder.AppendLine("画面：");
                builder.AppendLine("对白/字幕：");
                builder.AppendLine("视觉效果：");
                builder.AppendLine("声音：");
                builder.AppendLine("每个镜头要简洁、电影感强、画面明确。只保留一条文字规则：视频画面内无任何可见文字；对白/字幕只作为后期剪辑参考。不要反复堆叠文字禁令。");
                builder.AppendLine("如果参考图已有文字或招牌，只写“保持表面干净自然”。");
                builder.AppendLine("如果原始分镜没有明确运镜，或默认写成固定，必须根据角色动作、情绪、景别和参考图推导具体运镜；不能把所有镜头都写成固定。只有有明确戏剧理由时才使用固定镜头。");
                builder.AppendLine(needSound
                    ? "需要声音：环境音和音效要具体，禁止无关背景音乐。"
                    : "不需要声音：按无声视频提示词处理，不必要的对白和音效写“无”。");
            }
            builder.AppendLine();
            builder.AppendLine("Storyboard Data:");
            builder.AppendLine(BuildStoryboardVideoPromptDraft(document, node, selectedShots, upstreamInput));
            return builder.ToString().Trim();
        }

        public static string BuildStoryboardVideoNegativePrompt()
        {
            return "blurry, lowres, watermark, subtitles, deformed, bad anatomy";
        }

        public static string BuildStoryboardFallbackPrompt(WorkflowNode node, string input)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var builder = new StringBuilder();
            builder.AppendLine("You are a professional film storyboard artist and cinematographer.");
            builder.AppendLine("Based on the upstream script or narrative text, output a detailed storyboard JSON array ready for video production.");
            builder.AppendLine("If the upstream contains multiple episodes and the node input does not specify a target episode, default to the first episode.");
            builder.AppendLine("Output must be a pure JSON array. No markdown code fences. No explanation.");
            builder.AppendLine("Each object must contain:");
            builder.AppendLine("[");
            builder.AppendLine("  {");
            builder.AppendLine("    \"shotNumber\": 1,");
            builder.AppendLine("    \"duration\": 2,");
            builder.AppendLine("    \"scene\": \"地点 / 时间 / 镜头重点\",");
            builder.AppendLine("    \"characters\": [\"CharA\", \"CharB\"],");
            builder.AppendLine("    \"shotSize\": \"大远景 / 远景 / 全景 / 中景 / 中近景 / 近景 / 特写 / 大特写\",");
            builder.AppendLine("    \"cameraAngle\": \"平视 / 高位俯拍 / 低位仰拍 / 斜拍 / 越肩 / 鸟瞰\",");
            builder.AppendLine("    \"cameraMovement\": \"固定 / 横移 / 俯仰 / 摇移 / 升降 / 轨道推拉 / 变焦推拉 / 正跟随 / 倒跟随 / 环绕 / 滑轨横移\",");
            builder.AppendLine("    \"visualDescription\": \"简体中文画面描述，用于节点卡片显示\",");
            builder.AppendLine("    \"imagePrompt\": \"English image-generation prompt for this shot; keep normal visual instructions in English, but keep any exact visible text content as Simplified Chinese characters only\",");
            builder.AppendLine("    \"dialogue\": \"简体中文对白或无\",");
            builder.AppendLine("    \"visualEffects\": \"简体中文视觉效果\",");
            builder.AppendLine("    \"audioEffects\": \"简体中文环境声、音效、音乐提示\"");
            builder.AppendLine("  }");
            builder.AppendLine("]");
            builder.AppendLine("Rules:");
            builder.AppendLine("1. Each shot duration must be between 1-4 seconds.");
            builder.AppendLine("2. visualDescription, scene, dialogue, visualEffects and audioEffects must use Simplified Chinese for local UI display.");
            builder.AppendLine("3. Maintain action and spatial continuity across shots.");
            builder.AppendLine("4. imagePrompt must be English and ready for image generation. If subtitles, signs, screens, UI, posters or labels include visible text, the actual visible text may only be exact Simplified Chinese characters and Chinese punctuation; never English visible text, pinyin, Arabic numerals, Traditional Chinese, random glyphs or garbled text.");

            if (!string.IsNullOrWhiteSpace(node.Params.Input))
            {
                builder.AppendLine();
                builder.AppendLine("Node Input:");
                builder.AppendLine(node.Params.Input.Trim());
            }

            if (!string.IsNullOrWhiteSpace(input))
            {
                builder.AppendLine();
                builder.AppendLine("Upstream Content:");
                builder.AppendLine(input.Trim());
            }

            return builder.ToString().Trim();
        }

        public static string BuildStoryboardPanelPrompt(WorkflowDocument document, WorkflowNode node, StoryboardShot shot, string upstreamInput)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var orientation = string.Equals(node.Params.StoryboardPanelOrientation, "9:16", StringComparison.Ordinal)
                ? "portrait 9:16 panel"
                : "landscape 16:9 panel";
            var visualStyle = ResolveStoryboardVisualStyle(document, node, upstreamInput);
            var characterEntries = CollectStoryboardCharacterEntries(document, node, new[] { shot });
            var imagePrompt = GetStoryboardImagePromptText(shot);

            var parts = new List<string>
            {
                visualStyle,
                "single cinematic storyboard panel",
                orientation,
                "not a grid, not a collage, not a storyboard sheet",
                "keep exact same character face, hairstyle, silhouette and costume as the character design reference",
            };

            if (string.IsNullOrWhiteSpace(imagePrompt) && !string.IsNullOrWhiteSpace(shot.Scene))
            {
                parts.Add($"scene: {SanitizeStoryboardVisualTextCue(shot.Scene)}");
            }

            if (shot.Characters != null && shot.Characters.Count > 0)
            {
                parts.Add($"characters: {string.Join(", ", shot.Characters.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()))}");
            }

            if (characterEntries.Count > 0)
            {
                foreach (var entry in characterEntries)
                {
                    var appearancePrompt = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.AppearancePrompt);
                    var costumeNotes = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.CostumeNotes);
                    if (!string.IsNullOrWhiteSpace(appearancePrompt))
                    {
                        parts.Add($"character appearance lock - {entry.Name}: {TranslateStoryboardTextToPromptFragment(appearancePrompt)}");
                    }
                    else if (!string.IsNullOrWhiteSpace(entry.CompactSummary))
                    {
                        parts.Add($"character appearance lock - {entry.Name}: {TranslateStoryboardTextToPromptFragment(entry.CompactSummary)}");
                    }

                    if (!string.IsNullOrWhiteSpace(costumeNotes))
                    {
                        parts.Add($"character costume lock - {entry.Name}: {TranslateStoryboardTextToPromptFragment(costumeNotes)}, one exact outfit only, no clothing alternatives");
                    }

                    if (entry.HasReferencePortrait)
                    {
                        parts.Add($"reference priority for {entry.Name}: use the existing front portrait as the primary face and hairstyle reference");
                    }
                    else if (entry.HasThreeViewSheet)
                    {
                        parts.Add($"reference support for {entry.Name}: use the existing three-view sheet only to confirm costume details and silhouette, never reproduce the sheet layout");
                    }
                    else if (entry.HasExpressionSheet)
                    {
                        parts.Add($"reference support for {entry.Name}: use the existing expression sheet only to confirm face and hairstyle consistency, never reproduce the sheet layout");
                    }
                }
            }

            parts.Add($"shot size: {TranslateStoryboardShotSize(shot.ShotSize)}");
            parts.Add($"camera angle: {TranslateStoryboardCameraAngle(shot.CameraAngle)}");
            parts.Add($"camera movement feeling: {TranslateStoryboardCameraMovement(shot.CameraMovement)}");

            if (!string.IsNullOrWhiteSpace(imagePrompt))
            {
                parts.Add($"English image prompt for this shot: {SanitizeStoryboardVisualTextCue(imagePrompt)}");
            }
            else if (!string.IsNullOrWhiteSpace(shot.VisualDescription))
            {
                parts.Add(SanitizeStoryboardVisualTextCue(shot.VisualDescription));
            }

            if (!string.IsNullOrWhiteSpace(shot.Dialogue) && !string.Equals(shot.Dialogue.Trim(), "无", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"dialogue and subtitle cue: {NormalizeStoryboardVisibleChineseText(shot.Dialogue)}; if any subtitle or caption is visible, render exact Simplified Chinese characters only");
            }

            if (string.IsNullOrWhiteSpace(imagePrompt) && !string.IsNullOrWhiteSpace(shot.VisualEffects) && !string.Equals(shot.VisualEffects.Trim(), "无", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"visual effects: {SanitizeStoryboardVisualTextCue(shot.VisualEffects)}");
            }

            if (!string.IsNullOrWhiteSpace(shot.AudioEffects) && !string.Equals(shot.AudioEffects.Trim(), "无", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"sound atmosphere cue only: {TranslateStoryboardTextToPromptFragment(shot.AudioEffects)}");
            }

            parts.Add("maintain exact character appearance consistency, exact costume continuity, environment continuity");
            parts.Add("exactly one cinematic shot only, never a character sheet, never a turnaround sheet, never a model sheet, never a lineup, never a reference board");
            parts.Add("do not invent extra written words; required subtitles, signs, screens, UI, posters and labels must use exact Simplified Chinese characters only, with no English, no pinyin, no Arabic numerals, no Traditional Chinese and no garbled text; do not add English translations under Chinese text; leave text areas blank if exact Chinese cannot be rendered");
            parts.Add(StoryboardVisibleSimplifiedChineseRule);
            parts.Add("cinematic composition, dramatic yet readable staging, no watermark, no logo, no comic style unless ANIME is explicitly selected");
            return string.Join(", ", parts.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim().Trim(',')));
        }

        public static string BuildStoryboardPanelNegativePrompt()
        {
            return "multi panel, grid, duplicate person, speech bubble, blurry, lowres, " + StoryboardVisibleSimplifiedChineseNegativePrompt;
        }

        public static string BuildStoryboardPanelNegativePrompt(WorkflowDocument document, WorkflowNode node, string upstreamInput)
        {
            var negativePrompt = BuildStoryboardPanelNegativePrompt();
            if (!IsStoryboardAnimeStyle(document, node, upstreamInput))
            {
                negativePrompt += ", comic style, manga style, anime style, cartoon, cel shading, clean line art, sketch, illustrated look";
            }

            return negativePrompt;
        }

        public static string BuildStoryboardGridPrompt(
            WorkflowDocument document,
            WorkflowNode node,
            IReadOnlyList<StoryboardShot> pageShots,
            string upstreamInput,
            int pageIndex,
            int totalPages)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            var columns = string.Equals(node.Params.StoryboardGridLayout, "2x3", StringComparison.Ordinal) ? 2 : 3;
            const int rows = 3;
            var gridLayout = columns == 2 ? "2x3" : "3x3";
            var panelOrientation = string.Equals(node.Params.StoryboardPanelOrientation, "9:16", StringComparison.Ordinal)
                ? "9:16 portrait (vertical)"
                : "16:9 landscape (horizontal)";
            var visualStyle = ResolveStoryboardVisualStyle(document, node, upstreamInput);
            var characterEntries = CollectStoryboardCharacterEntries(document, node, pageShots);
            var sceneConsistencySection = BuildStoryboardSceneConsistencySection(pageShots);

            var builder = new StringBuilder();
            builder.AppendLine($"Create one professional cinematic storyboard page in a {gridLayout} grid.");
            builder.AppendLine("Generate ONE complete storyboard board image, not a single shot, not unrelated images, not a contact sheet.");
            builder.AppendLine($"Grid Layout: {gridLayout} ({columns} columns x {rows} rows).");
            builder.AppendLine($"Panel Orientation: {panelOrientation}.");
            builder.AppendLine($"Page: {pageIndex + 1} of {Math.Max(1, totalPages)}.");
            builder.AppendLine($"Art Style: {visualStyle}.");
            builder.AppendLine();
            builder.AppendLine("GLOBAL REQUIREMENTS:");
            builder.AppendLine("- every panel must be visually distinct and represent a different shot or beat");
            builder.AppendLine("- keep the same characters identical across all panels");
            builder.AppendLine("- keep costumes, hairstyle, props, and color palette consistent across the page");
            builder.AppendLine("- keep the same scene visually consistent when multiple panels happen in the same location");
            builder.AppendLine("- no panel numbers, no speech bubbles, no watermark, no logo, no garbled text");
            builder.AppendLine("- visible text policy: subtitles, captions, signs, screens, labels, UI and posters may only contain exact short Simplified Chinese characters and Chinese punctuation; no English, no pinyin, no Arabic numerals, no Traditional Chinese, no random glyphs; do not add English translations under Chinese text");
            builder.AppendLine("- dialogue and sound cues should be expressed through acting, atmosphere and composition; when subtitles are shown, use exact Simplified Chinese only");
            builder.AppendLine("- clean readable cinematic staging, dramatic but controlled composition, consistent with the selected visual style");
            builder.AppendLine();

            if (characterEntries.Count > 0)
            {
                builder.AppendLine("CHARACTER CONSISTENCY (CRITICAL):");
                builder.AppendLine(BuildStoryboardCharacterConsistencySection(characterEntries));
                builder.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(sceneConsistencySection))
            {
                builder.AppendLine(sceneConsistencySection);
                builder.AppendLine();
            }

            builder.AppendLine("PANEL BREAKDOWN:");
            foreach (var shot in pageShots)
            {
                builder.AppendLine("---");
                builder.AppendLine($"{panelOrientation} - {BuildStoryboardGridShotPrompt(shot)}");
            }

            var expectedPanels = columns * rows;
            for (var index = pageShots.Count; index < expectedPanels; index++)
            {
                builder.AppendLine("---");
                builder.AppendLine($"{panelOrientation} - empty panel, leave clean blank storyboard cell");
            }

            builder.AppendLine();
            builder.AppendLine("ABSOLUTE RULES:");
            builder.AppendLine("- keep this as one complete storyboard board image");
            builder.AppendLine("- do not draw English textual annotations inside the generated image; any required visible text must be exact Simplified Chinese characters only; if exact Chinese cannot be rendered, leave the surface blank");
            builder.AppendLine("- no collage chaos, no repeated same frame, no random extra characters");
            builder.AppendLine("- characters must match the established design in every panel");
            builder.AppendLine("- clothing and silhouette must stay consistent across the full page");
            return builder.ToString().Trim();
        }

        public static string BuildStoryboardGridNegativePrompt()
        {
            return "panel number, speech bubble, duplicate person, blurry, lowres, " + StoryboardVisibleSimplifiedChineseNegativePrompt;
        }

        private static string NormalizeStoryboardVisibleChineseText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text.Trim();
            normalized = Regex.Replace(normalized, @"\[[A-Za-z][A-Za-z\s,.;:'""!?-]{0,40}\]\s*", string.Empty);
            normalized = Regex.Replace(normalized, @"\b(None|N/A|null)\b", "无", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        private static string KeepSimplifiedChineseVisibleTextOnly(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var filtered = Regex.Replace(text, @"\[[^\]]*\]", string.Empty);
            filtered = Regex.Replace(filtered, @"[A-Za-z0-9_./\\:@#%&+=*<>|~`^$]+", string.Empty);
            filtered = Regex.Replace(filtered, @"[^\u3400-\u4DBF\u4E00-\u9FFF，。！？、：；“”‘’（）《》【】—…\s]", string.Empty);
            filtered = Regex.Replace(filtered, @"\s+", " ").Trim();
            return Regex.IsMatch(filtered, @"[\u3400-\u4DBF\u4E00-\u9FFF]") ? filtered : string.Empty;
        }

        private static string GetStoryboardImagePromptText(StoryboardShot shot)
        {
            if (shot == null)
            {
                return string.Empty;
            }

            return !string.IsNullOrWhiteSpace(shot.ImagePrompt)
                ? shot.ImagePrompt.Trim()
                : TranslateStoryboardTextToPromptFragment(shot.VisualDescription);
        }

        private static string SanitizeStoryboardVisualTextCue(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var sanitized = TranslateStoryboardTextToPromptFragment(text);
            sanitized = Regex.Replace(sanitized, @"\b(None|N/A|null)\b", "无", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
            sanitized += "; visible text policy: any subtitle, dialogue caption, sign, screen, phone UI, poster, label, document, card or notice text must be exact short Simplified Chinese characters only; no English letters, no pinyin, no Arabic numerals, no Traditional Chinese, no random glyphs, no garbled text.";
            return sanitized;
        }

        private static string BuildStoryboardGridShotPrompt(StoryboardShot shot)
        {
            var parts = new List<string>();
            var imagePrompt = GetStoryboardImagePromptText(shot);

            if (!string.IsNullOrWhiteSpace(imagePrompt))
            {
                parts.Add($"English image prompt: {SanitizeStoryboardVisualTextCue(imagePrompt)}");
            }
            else if (!string.IsNullOrWhiteSpace(shot.VisualDescription))
            {
                parts.Add(SanitizeStoryboardVisualTextCue(shot.VisualDescription));
            }

            if (string.IsNullOrWhiteSpace(imagePrompt) && !string.IsNullOrWhiteSpace(shot.Scene))
            {
                parts.Add($"environment: {SanitizeStoryboardVisualTextCue(shot.Scene)}");
            }

            if (shot.Characters != null && shot.Characters.Count > 0)
            {
                parts.Add($"characters on screen: {string.Join(", ", shot.Characters.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()))}");
            }

            parts.Add($"shot size: {TranslateStoryboardShotSize(shot.ShotSize)}");
            parts.Add($"camera angle: {TranslateStoryboardCameraAngle(shot.CameraAngle)}");
            parts.Add($"camera movement: {TranslateStoryboardCameraMovement(shot.CameraMovement)}");

            if (!string.IsNullOrWhiteSpace(shot.Dialogue) && !string.Equals(shot.Dialogue.Trim(), "无", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"dialogue/subtitle cue: {NormalizeStoryboardVisibleChineseText(shot.Dialogue)}; if visible, render exact Simplified Chinese characters only");
            }

            if (string.IsNullOrWhiteSpace(imagePrompt) && !string.IsNullOrWhiteSpace(shot.VisualEffects) && !string.Equals(shot.VisualEffects.Trim(), "无", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"visual effects: {SanitizeStoryboardVisualTextCue(shot.VisualEffects)}");
            }

            if (!string.IsNullOrWhiteSpace(shot.AudioEffects) && !string.Equals(shot.AudioEffects.Trim(), "无", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"audio atmosphere cue only: {TranslateStoryboardTextToPromptFragment(shot.AudioEffects)}");
            }

            parts.Add(StoryboardVisibleSimplifiedChineseRule);
            return string.Join(". ", parts.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string BuildStoryboardSceneConsistencySection(IReadOnlyList<StoryboardShot> pageShots)
        {
            var groups = pageShots
                .Where(shot => !string.IsNullOrWhiteSpace(shot.Scene))
                .GroupBy(shot => shot.Scene.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .ToList();

            if (groups.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine("SCENE CONSISTENCY (CRITICAL):");
            foreach (var group in groups)
            {
                var shotIndexes = string.Join(", ", group.Select(shot => $"#{Math.Max(1, shot.ShotNumber)}"));
                var descriptionSeed = group
                    .Select(GetStoryboardImagePromptText)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? group.Key;
                builder.AppendLine($"- Scene \"{NormalizeStoryboardVisibleChineseText(group.Key)}\" appears in panels {shotIndexes}.");
                builder.AppendLine($"  Keep architecture, props, lighting direction, color temperature, weather and atmosphere identical. English visual anchor: {SanitizeStoryboardVisualTextCue(descriptionSeed)}");
            }

            return builder.ToString().TrimEnd();
        }

        public static List<CharacterDesignEntry> CollectStoryboardCharacterEntries(
            WorkflowDocument document,
            WorkflowNode node,
            IReadOnlyList<StoryboardShot> pageShots)
        {
            var referencedNames = new HashSet<string>(
                pageShots
                    .SelectMany(shot => shot.Characters ?? new List<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var entries = new Dictionary<string, CharacterDesignEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var upstreamNode in CollectUpstreamNodes(document, node)
                         .Where(candidate => candidate.Type == WorkflowNodeCatalog.CharacterView))
            {
                upstreamNode.Params ??= new WorkflowNodeParameters();
                upstreamNode.Params.EnsureDefaults(upstreamNode.Type);
                foreach (var entry in upstreamNode.Params.CharacterEntries ?? new List<CharacterDesignEntry>())
                {
                    if (string.IsNullOrWhiteSpace(entry.Name))
                    {
                        continue;
                    }

                    if (referencedNames.Count > 0 && !referencedNames.Contains(entry.Name.Trim()))
                    {
                        continue;
                    }

                    entries[entry.Name.Trim()] = entry;
                }
            }

            var ordered = new List<CharacterDesignEntry>();
            foreach (var name in referencedNames)
            {
                if (entries.TryGetValue(name, out var entry))
                {
                    ordered.Add(entry);
                }
            }

            return ordered;
        }

        private static string BuildStoryboardCharacterConsistencySection(IReadOnlyList<CharacterDesignEntry> entries)
        {
            var builder = new StringBuilder();
            foreach (var entry in entries)
            {
                var summaryParts = new List<string>();
                var appearancePrompt = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.AppearancePrompt);
                var costumeNotes = CharacterPromptTextBuilder.NormalizeSingleOutfitAnchorText(entry.CostumeNotes);
                if (!string.IsNullOrWhiteSpace(appearancePrompt))
                {
                    summaryParts.Add(TranslateStoryboardTextToPromptFragment(appearancePrompt));
                }
                else if (!string.IsNullOrWhiteSpace(entry.CompactSummary))
                {
                    summaryParts.Add(TranslateStoryboardTextToPromptFragment(entry.CompactSummary));
                }

                if (!string.IsNullOrWhiteSpace(costumeNotes))
                {
                    summaryParts.Add($"costume: {TranslateStoryboardTextToPromptFragment(costumeNotes)}, one exact outfit only");
                }

                if (!string.IsNullOrWhiteSpace(entry.ActingNotes))
                {
                    summaryParts.Add($"acting note: {TranslateStoryboardTextToPromptFragment(entry.ActingNotes)}");
                }

                if (entry.HasThreeViewSheet)
                {
                    summaryParts.Add("character node already has three-view sheet");
                }
                else if (entry.HasExpressionSheet)
                {
                    summaryParts.Add("character node already has expression sheet");
                }

                builder.AppendLine($"- {entry.Name}: {string.Join("; ", summaryParts.Where(value => !string.IsNullOrWhiteSpace(value)))}");
            }

            builder.AppendLine("- every panel must preserve the same face, hairstyle, silhouette, clothing layers, accessories, shoes and colors");
            builder.AppendLine("- if a character profile contains clothing examples or alternatives, choose one concrete outfit and keep that same outfit in every panel");
            builder.AppendLine("- never redesign a character between panels");
            return builder.ToString().TrimEnd();
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
                chapterCount = ExtractOutlineChapters(text, 64).Count;
            }

            return (
                episodeCount,
                chapterCount > 0 ? chapterCount.ToString() : string.Empty);
        }

        public static List<CharacterDesignEntry> ParseCharacterDescriptionEntries(string text)
        {
            return WorkflowCharacterParser.ParseCharacterDescriptionEntries(text);
        }

        private static void AppendCharacterEntryContext(StringBuilder builder, CharacterDesignEntry entry)
        {
            builder.AppendLine($"Character: {entry.Name}");
            if (!string.IsNullOrWhiteSpace(entry.RoleType))
            {
                builder.AppendLine($"Role mode: {entry.RoleType}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Summary))
            {
                builder.AppendLine($"Summary: {entry.Summary}");
            }

            if (!string.IsNullOrWhiteSpace(entry.BasicStats))
            {
                builder.AppendLine($"Stats: {entry.BasicStats}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Profession))
            {
                builder.AppendLine($"Profession: {entry.Profession}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Personality))
            {
                builder.AppendLine($"Personality: {entry.Personality}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Motivation))
            {
                builder.AppendLine($"Motivation: {entry.Motivation}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Values))
            {
                builder.AppendLine($"Values: {entry.Values}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Weakness))
            {
                builder.AppendLine($"Weakness: {entry.Weakness}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Relationships))
            {
                builder.AppendLine($"Relationships: {entry.Relationships}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Habits))
            {
                builder.AppendLine($"Habits: {entry.Habits}");
            }

            if (!string.IsNullOrWhiteSpace(entry.VisualTags))
            {
                builder.AppendLine($"Visual tags: {entry.VisualTags}");
            }

            if (!string.IsNullOrWhiteSpace(entry.AppearancePrompt))
            {
                builder.AppendLine($"Base appearance prompt: {entry.AppearancePrompt}");
            }

            if (!string.IsNullOrWhiteSpace(entry.CostumeNotes))
            {
                builder.AppendLine($"Costume notes: {entry.CostumeNotes}");
            }

            if (!string.IsNullOrWhiteSpace(entry.ActingNotes))
            {
                builder.AppendLine($"Acting notes: {entry.ActingNotes}");
            }
        }

        private static string ExtractMarkdownSection(string text, string heading, string? nextHeading = null)
        {
            return ExtractMarkdownSection(
                text,
                new[] { heading },
                string.IsNullOrWhiteSpace(nextHeading) ? Array.Empty<string>() : new[] { nextHeading! });
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

        public static List<OutlineChapterPlan> ExtractOutlineChapters(string text, int limit = 24, int totalEpisodes = 0)
        {
            return WorkflowScriptParser.ExtractOutlineChapters(text, limit, totalEpisodes);
        }

        public static OutlineChapterPlan? ResolveScriptChapterSelection(WorkflowNode node, IReadOnlyList<OutlineChapterPlan> chapters)
        {
            return WorkflowScriptParser.ResolveScriptChapterSelection(node, chapters);
        }

        public static int ResolveScriptEpisodeCount(WorkflowNode node, OutlineChapterPlan? selectedChapter)
        {
            return WorkflowScriptParser.ResolveScriptEpisodeCount(node, selectedChapter);
        }

        public static string BuildScriptTargetEpisodeRange(OutlineChapterPlan? selectedChapter, int episodeCount)
        {
            return WorkflowScriptParser.BuildScriptTargetEpisodeRange(selectedChapter, episodeCount);
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

        private static string DescribeParameters(WorkflowNode node)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);

            if (node.Type == WorkflowNodeCatalog.Outline)
            {
                var summary = node.Params.BuildOutlineSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<无>" : summary;
            }

            if (node.Type == WorkflowNodeCatalog.Script)
            {
                var summary = node.Params.BuildScriptSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<无>" : summary;
            }

            if (node.Type == WorkflowNodeCatalog.CharacterView || node.Type == WorkflowNodeCatalog.CharacterDescription)
            {
                var summary = node.Params.BuildCharacterDesignSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<无>" : summary;
            }

            if (node.Type == WorkflowNodeCatalog.StoryboardBreakdown)
            {
                var summary = node.Params.BuildStoryboardBreakdownSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<无>" : summary;
            }

            if (node.Type == WorkflowNodeCatalog.StoryboardImage)
            {
                var summary = node.Params.BuildStoryboardImageSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<无>" : summary;
            }

            if (node.Type == WorkflowNodeCatalog.StoryboardVideo)
            {
                var summary = node.Params.BuildStoryboardVideoSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<无>" : summary;
            }

            if (node.Type == WorkflowNodeCatalog.VideoCollection)
            {
                var summary = node.Params.BuildVideoCollectionSummary();
                return string.IsNullOrWhiteSpace(summary) ? "<无>" : summary;
            }

            return string.IsNullOrWhiteSpace(node.Params.Input) ? "<无>" : node.Params.Input.Trim();
        }

        private static string ResolveStoryboardVisualStyle(string text)
        {
            return ResolveStoryboardVisualStyle(null, null, text);
        }

        private static string ResolveStoryboardVisualStyle(WorkflowDocument? document, WorkflowNode? node, string? text)
        {
            return ResolveStoryboardVisualStyleDescriptor(ResolveStoryboardVisualStyleCode(document, node, text));
        }

        private static bool IsStoryboardAnimeStyle(WorkflowDocument? document, WorkflowNode? node, string? text)
        {
            return string.Equals(ResolveStoryboardVisualStyleCode(document, node, text), "ANIME", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveStoryboardVisualStyleCode(WorkflowDocument? document, WorkflowNode? node, string? text)
        {
            var outlineStyle = ResolveUpstreamOutlineVisualStyle(document, node);
            if (!string.IsNullOrWhiteSpace(outlineStyle))
            {
                return outlineStyle;
            }

            var detectedStyle = DetectStoryboardVisualStyleFromText(text);
            if (!string.IsNullOrWhiteSpace(detectedStyle))
            {
                return detectedStyle;
            }

            var nodeStyle = (node?.Params?.VisualStyle ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(nodeStyle) &&
                WorkflowNodeCatalog.OutlineVisualStyles.Contains(nodeStyle, StringComparer.OrdinalIgnoreCase) &&
                node?.Type != WorkflowNodeCatalog.StoryboardImage)
            {
                return nodeStyle.ToUpperInvariant();
            }

            return "REAL";
        }

        private static string ResolveUpstreamOutlineVisualStyle(WorkflowDocument? document, WorkflowNode? node)
        {
            if (document == null)
            {
                return string.Empty;
            }

            WorkflowNode? outlineNode = null;
            if (node != null)
            {
                outlineNode = CollectUpstreamNodes(document, node)
                    .FirstOrDefault(candidate => candidate.Type == WorkflowNodeCatalog.Outline);
            }

            outlineNode ??= document.Nodes
                .Where(candidate => candidate.Type == WorkflowNodeCatalog.Outline)
                .OrderBy(candidate => string.IsNullOrWhiteSpace(candidate.Output) ? 0 : 1)
                .ThenBy(candidate => document.Nodes.IndexOf(candidate))
                .LastOrDefault();

            var style = (outlineNode?.Params?.VisualStyle ?? string.Empty).Trim();
            return WorkflowNodeCatalog.OutlineVisualStyles.Contains(style, StringComparer.OrdinalIgnoreCase)
                ? style.ToUpperInvariant()
                : string.Empty;
        }

        private static string DetectStoryboardVisualStyleFromText(string? text)
        {
            var normalized = (text ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            if (normalized.Contains("3D", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("三维", StringComparison.OrdinalIgnoreCase))
            {
                return "3D";
            }

            if (normalized.Contains("REAL", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("真人", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("写实", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("realistic", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("photorealistic", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("live action", StringComparison.OrdinalIgnoreCase))
            {
                return "REAL";
            }

            if (normalized.Contains("ANIME", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("动漫", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("漫画", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("二次元", StringComparison.OrdinalIgnoreCase))
            {
                return "ANIME";
            }

            return string.Empty;
        }

        private static string ResolveStoryboardVisualStyleDescriptor(string styleCode)
        {
            return styleCode switch
            {
                "3D" => "stylized 3d cinematic storyboard render, detailed textures, consistent 3d lighting",
                "ANIME" => "2d anime cinematic storyboard frame, cel shading, clean line art",
                _ => "cinematic live action storyboard frame, photorealistic film still, natural lighting, realistic lens and depth of field, not anime, not cartoon, not line art",
            };
        }

        public static string ResolveCharacterDesignStyleDescriptor(WorkflowNode node)
        {
            var style = (node.Params?.VisualStyle ?? string.Empty).Trim().ToUpperInvariant();
            if (style == "REAL" || style == "真人")
            {
                return "cinematic live action, photorealistic, film quality, natural skin texture, not anime, not cartoon, not cel shaded, not illustrated";
            }

            if (style == "3D")
            {
                return "stylized 3d render, detailed textures, volumetric lighting, subsurface scattering, not anime, not cel shaded, not flat, not cartoon";
            }

            return "2d anime, cel shading, bold clean line art, vibrant colors, not photorealistic, not realistic, not 3d";
        }

        public static string ResolveCharacterDesignStyleNegativePrefix(WorkflowNode node)
        {
            var style = (node.Params?.VisualStyle ?? string.Empty).Trim().ToUpperInvariant();
            if (style == "REAL" || style == "真人")
            {
                return "anime, cartoon, illustration, cel shading, 2d art, flat color, painted look, 3d render, cgi";
            }

            if (style == "3D")
            {
                return "anime, cartoon, illustration, cel shading, 2d art, flat color, painted look, hand drawn, sketch";
            }

            return "photorealistic, real person, live action, 3d render, cgi, semi realistic skin";
        }

        /// <summary>Returns a Chinese-language character design style string for Gemini cloud prompts.</summary>
        public static string ResolveCharacterDesignStyleDescriptorChinese(WorkflowNode node)
        {
            var style = (node.Params?.VisualStyle ?? string.Empty).Trim().ToUpperInvariant();
            if (style == "REAL" || style == "真人")
            {
                return "真人影视级，电影质感，自然肤质，非动漫，非卡通，非赛璐珞";
            }

            if (style == "3D")
            {
                return "3D风格渲染，精细纹理，体积光，非动漫，非赛璐珞，非手绘";
            }

            return "2D动漫，赛璐珞勾线，洁净线稿";
        }

        private static string TranslateStoryboardShotSize(string value)
        {
            return value switch
            {
                "大远景" => "extreme long shot",
                "远景" => "long shot",
                "全景" => "full shot",
                "中景" => "medium shot",
                "中近景" => "medium close-up shot",
                "近景" => "close shot",
                "特写" => "close-up shot",
                "大特写" => "extreme close-up shot",
                _ => "medium shot",
            };
        }

        private static string TranslateStoryboardCameraAngle(string value)
        {
            return value switch
            {
                "高位俯拍" => "high angle shot",
                "低位仰拍" => "low angle shot",
                "斜拍" => "dutch angle",
                "越肩" => "over the shoulder shot",
                "鸟瞰" => "bird's eye view",
                _ => "eye-level angle",
            };
        }

        private static string TranslateStoryboardCameraMovement(string value)
        {
            return value switch
            {
                "横移" => "lateral tracking movement",
                "俯仰" => "tilt movement",
                "摇移" => "panning movement",
                "升降" => "vertical crane movement",
                "轨道推拉" => "dolly push pull",
                "变焦推拉" => "zoom push pull",
                "正跟随" => "forward follow shot",
                "倒跟随" => "reverse follow shot",
                "环绕" => "orbit movement",
                "滑轨横移" => "slider lateral move",
                _ => "infer a cinematic camera movement from the shot action and reference image; avoid locked-off framing unless dramatically required",
            };
        }

        private static string TranslateStoryboardTextToPromptFragment(string value)
        {
            var cleaned = CleanExtractedValue(value);
            return cleaned
                .Replace(Environment.NewLine, " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Replace(":", ": ", StringComparison.Ordinal)
                .Trim();
        }

        private static string GenerateMockResult(WorkflowNode node, string inputs)
        {
            var parametersText = DescribeParameters(node);

            if (node.Type == WorkflowNodeCatalog.Outline)
            {
                var coreIdea = string.IsNullOrWhiteSpace(node.Params?.CoreIdea) ? "No core idea provided" : node.Params.CoreIdea.Trim();
                var genre = string.IsNullOrWhiteSpace(node.Params?.Genre) ? "No genre selected" : node.Params.Genre;
                var setting = string.IsNullOrWhiteSpace(node.Params?.Setting) ? "No setting selected" : node.Params.Setting;
                var style = WorkflowNodeParameters.GetVisualStyleDisplayName(node.Params?.VisualStyle ?? "ANIME");
                var episodes = Math.Max(1, node.Params?.Episodes ?? 10);
                var duration = node.Params == null || node.Params.DurationMinutes <= 0 ? 1M : node.Params.DurationMinutes;
                return $"Story outline generated: core idea \"{coreIdea}\", genre \"{genre}\", setting \"{setting}\", style \"{style}\", {episodes} episodes, {duration:0.##} min each.";
            }

            if (node.Type == WorkflowNodeCatalog.Script)
            {
                var outlineText = string.IsNullOrWhiteSpace(inputs) ? string.Empty : ExtractOutlineSummary(inputs);
                var planningText = string.IsNullOrWhiteSpace(inputs) ? string.Empty : ExtractPlanningSummary(inputs);
                var planningInfo = string.IsNullOrWhiteSpace(planningText) ? string.Empty : $"{Environment.NewLine}{planningText}";
                var outlineInfo = string.IsNullOrWhiteSpace(outlineText) ? string.Empty : $"{Environment.NewLine}--大纲摘录:{outlineText}";
                return $"Script generated ({(string.IsNullOrWhiteSpace(inputs) ? "standalone" : "from outline")}){planningInfo}{outlineInfo}";
            }

            if (node.Type == WorkflowNodeCatalog.CharacterDescription)
            {
                return $"Character description generated ({parametersText})";
            }

            if (node.Type == WorkflowNodeCatalog.StoryboardBreakdown)
            {
                return $"Storyboard breakdown generated ({parametersText})";
            }

            if (node.Type == WorkflowNodeCatalog.CharacterView)
            {
                return $"Character view generated ({parametersText})";
            }

            if (node.Type == WorkflowNodeCatalog.StoryboardImage)
            {
                return $"Storyboard image generated ({parametersText})";
            }

            if (node.Type == WorkflowNodeCatalog.StoryboardVideo)
            {
                return $"Storyboard video generated ({(string.IsNullOrWhiteSpace(inputs) ? "standalone" : "from upstream data")})";
            }

            if (node.Type == WorkflowNodeCatalog.VideoCollection)
            {
                return $"Video collection generated ({(string.IsNullOrWhiteSpace(inputs) ? "standalone" : "merged upstream videos")})";
            }

            if (node.Type == WorkflowNodeCatalog.LocalAsset)
            {
                return $"Local asset referenced ({parametersText})";
            }

            return "Executed (sample result)";
        }

        private static string ExtractOutlineSummary(string inputs)
        {
            var marker = "已生成短剧故事大纲";
            var index = inputs.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                return inputs[index..].Trim();
            }

            marker = "生成了故事大纲";
            index = inputs.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                return string.Empty;
            }

            var text = inputs[index..];
            var planningIndex = text.IndexOf(Environment.NewLine + "【规划】", StringComparison.Ordinal);
            return (planningIndex >= 0 ? text[..planningIndex] : text).Trim();
        }

        private static string ExtractPlanningSummary(string inputs)
        {
            var marker = "【规划】";
            var index = inputs.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                return string.Empty;
            }

            var line = inputs[index..];
            var end = line.IndexOfAny(new[] { '\r', '\n' });
            return (end >= 0 ? line[..end] : line).Trim();
        }

        private static void AppendCharacterConsistencyAnchor(StringBuilder builder, CharacterDesignEntry entry, bool fullBody)
        {
            if (fullBody)
            {
                builder.AppendLine("Consistency anchor: lock hairstyle, costume, accessories, body proportions, shoes, and silhouette across all views.");
            }
            else
            {
                builder.AppendLine("Consistency anchor: lock identity, hairstyle, hairline, bangs, face shape, eyebrows, eyes, nose, mouth, skin tone, age, gender, and color palette.");
            }
        }

        private static void AppendCharacterGenderLock(StringBuilder builder, CharacterDesignEntry entry, bool fullBody)
        {
            var hint = CharacterPromptTextBuilder.DetectGender(entry);
            if (hint == CharacterGenderHint.Male)
            {
                builder.AppendLine(fullBody ? "Gender lock: male body proportions, male shoulder width, male waist-hip ratio, do not change gender." : "Gender lock: male facial features, male face shape, male brow bone, male jawline, do not change gender.");
            }
            else if (hint == CharacterGenderHint.Female)
            {
                builder.AppendLine(fullBody ? "Gender lock: female body proportions, female shoulder width, female waist-hip ratio, do not change gender." : "Gender lock: female facial features, female face shape, female brow bone, female jawline, do not change gender.");
            }
        }

        private static List<string> GetCharacterGenderNegativeTags(CharacterDesignEntry entry)
        {
            var hint = CharacterPromptTextBuilder.DetectGender(entry);
            if (hint == CharacterGenderHint.Male)
            {
                return new List<string> { "female body", "female face", "feminine features", "gender swap" };
            }
            else if (hint == CharacterGenderHint.Female)
            {
                return new List<string> { "male body", "male face", "masculine features", "gender swap" };
            }
            return new List<string> { "gender swap" };
        }
    }
}
