using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace JSAI.WinApp
{
    public enum CharacterGenderHint
    {
        Unknown,
        Male,
        Female
    }

    public static class CharacterPromptTextBuilder
    {
        private const string ExpressionFramingFront =
            "exact straight-on front close-up head-and-shoulders portrait, face parallel to camera, head level, no head turn, no head tilt, both eyes equally visible, both cheeks symmetrically visible, face, neck, collar neckline and top of shoulders only, crop at the collarbone, shoulders-above framing, no chest, no bust, no torso, no arms, no hands, centered, seamless plain light gray or white studio background only, empty background with no objects, no scenery, no workplace, no vehicle, no food truck, no room, no signs, no posters, no props";

        // 男女九宫格统一使用同一套九种表情，仅人物身份与性别锁定不同。
        private static readonly IReadOnlyList<ExpressionCell> UnifiedCells = new ExpressionCell[]
        {
            new("平静", "冷静", "neutral expression", ExpressionFramingFront),
            new("微笑", "温和笑容", "smile", ExpressionFramingFront),
            new("生气", "皱眉，严肃", "angry", ExpressionFramingFront),
            new("惊讶", "睁大眼睛", "surprised", ExpressionFramingFront),
            new("伤感", "失落", "sad", ExpressionFramingFront),
            new("大笑", "开心", "laughing", ExpressionFramingFront),
            new("思考", "疑惑", "thinking expression", ExpressionFramingFront),
            new("闭眼浅笑", "放松", "peaceful", ExpressionFramingFront),
            new("不屑", "轻微挑眉", "skeptical expression", ExpressionFramingFront)
        };

        public static IReadOnlyList<ExpressionCell> GetExpressions(CharacterDesignEntry entry)
        {
            return UnifiedCells;
        }

        public static IReadOnlyList<string> GetDetailedExpressionPrompts(CharacterDesignEntry entry)
        {
            return GetExpressions(entry).Select(cell => cell.DetailedPrompt).ToList();
        }

        public static string BuildGptImageExpressionList(CharacterDesignEntry entry)
        {
            return string.Join(", ", GetExpressions(entry).Select(cell => cell.EnglishLabel));
        }

        public static string NormalizeSingleOutfitAnchorText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(value.Trim(), @"\s+", " ");
            normalized = Regex.Replace(
                normalized,
                "（(?<inner>[^（）]{1,140})）",
                match => NormalizeOutfitAlternativeParenthetical(match, "（", "）"),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            normalized = Regex.Replace(
                normalized,
                "\\((?<inner>[^()]{1,140})\\)",
                match => NormalizeOutfitAlternativeParenthetical(match, "(", ")"),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return normalized.Trim().Trim(',', '，', '.', '。', ';', '；');
        }

        private static string NormalizeOutfitAlternativeParenthetical(Match match, string open, string close)
        {
            var inner = match.Groups["inner"].Value.Trim();
            if (!LooksLikeOutfitAlternativeText(inner))
            {
                return match.Value;
            }

            var choice = ExtractFirstOutfitChoice(inner);
            return string.IsNullOrWhiteSpace(choice)
                ? string.Empty
                : $"{open}单一服装锁定：{choice}{close}";
        }

        private static bool LooksLikeOutfitAlternativeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !ContainsClothingCue(value))
            {
                return false;
            }

            return Regex.IsMatch(value, "(如|例如|比如|可选|任选|等|、|/|或者|或|such as|for example|e\\.g\\.|option|optional|either|\\bor\\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string ExtractFirstOutfitChoice(string value)
        {
            var cleaned = Regex.Replace(
                    value,
                    "^(例如|比如|如|可选|任选|such as|for example|e\\.g\\.|including|includes)\\s*[:：]?",
                    string.Empty,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Trim();
            cleaned = Regex.Replace(cleaned, "(等|etc\\.?|and so on)$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();

            var candidates = Regex.Split(cleaned, "\\s*(?:、|/|或者|或|,|，|;|；|\\bor\\b|\\beither\\b)\\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                .Select(part => Regex.Replace(part, "(等|etc\\.?)$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim().Trim(',', '，', '.', '。', ';', '；'))
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();

            var selected = candidates.FirstOrDefault(ContainsClothingCue) ?? candidates.FirstOrDefault() ?? string.Empty;
            return selected.Length > 48 ? selected[..48].Trim() : selected;
        }

        private static bool ContainsClothingCue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var lower = value.ToLowerInvariant();
            string[] cues =
            {
                "衣", "服", "衫", "外套", "夹克", "针织", "牛仔", "大衣", "风衣", "西装", "制服", "裙", "裤", "鞋", "帽", "围巾",
                "shirt", "sweater", "knit", "jacket", "coat", "hoodie", "denim", "jeans", "dress", "skirt", "pants", "trousers",
                "suit", "uniform", "boots", "sneakers", "loafers", "vest", "blazer", "cardigan", "outerwear", "costume", "outfit"
            };
            return cues.Any(cue => lower.Contains(cue, StringComparison.OrdinalIgnoreCase));
        }

        public static string BuildChineseExpressionPrompt(CharacterDesignEntry entry)
            => BuildChineseExpressionPrompt(null, entry);

        public static string BuildChineseThreeViewPrompt(CharacterDesignEntry entry)
            => BuildChineseThreeViewPrompt(null, entry);

        public static string BuildChinesePromptBundle(CharacterDesignEntry entry)
            => BuildChinesePromptBundle(null, entry);

        public static string BuildChineseExpressionPrompt(WorkflowNode? node, CharacterDesignEntry entry)
        {
            var cells = GetExpressions(entry);
            var styleLine = node != null
                ? WorkflowExecutor.ResolveCharacterDesignStyleDescriptorChinese(node)
                : "2D动漫，赛璐璐勾线，干净线稿";

            var sb = new StringBuilder();
            sb.AppendLine("请生成一张完整的 3×3 九宫格表情板。");
            sb.AppendLine("画面包含 9 个等大方格，三行三列排列，每个格子展示同一个角色的不同表情。");
            AppendChineseCharacterIdentity(sb, entry, fullBody: false);
            sb.AppendLine("Background lock: seamless plain light gray or white studio background only in every expression cell; no scenery, workplace, vehicles, food trucks, rooms, doors, windows, signs, posters, furniture, shelves, plants, props, or background objects.");
            sb.AppendLine("Framing lock: every expression cell must be shoulder-up only, showing only the head, neck, collar neckline and top of shoulders. Crop at the collarbone. Do not show chest, bust, torso, waist, arms, hands, clothing body, buttons below the collarbone, or any object.");
            sb.AppendLine("从左到右、从上到下，九个表情固定为：");
            for (int i = 0; i < cells.Count; i++)
            {
                sb.AppendLine($"  {i + 1}. {cells[i].Label}（{cells[i].Description}，{cells[i].EnglishLabel}）");
            }

            sb.AppendLine("硬性要求：九个格子都必须是同一个角色，只允许表情变化，不允许换脸、不允许改发型、不允许改性别。");
            sb.AppendLine("九个格子都必须是严格正面头像：脸部与镜头平行，头部水平，不转头，不歪头，双眼完整可见，双颊对称可见。");
            sb.AppendLine("脸型、五官比例、眼型、鼻型、嘴型、发际线、刘海、发量、发长、发型轮廓、发色和服装领口必须完全一致。");
            if (DetectGender(entry) == CharacterGenderHint.Male)
            {
                sb.AppendLine("男性胡须一致性：如果角色有胡须、络腮胡、小胡子或胡茬，九个格子必须保持完全相同的胡须形状、长度、密度、位置和颜色。");
            }
            sb.AppendLine($"风格：{styleLine}。");
            return sb.ToString().Trim();
        }

        public static string BuildChineseThreeViewPrompt(WorkflowNode? node, CharacterDesignEntry entry)
        {
            var styleLine = node != null
                ? WorkflowExecutor.ResolveCharacterDesignStyleDescriptorChinese(node)
                : "2D动漫，赛璐璐勾线，干净线稿";

            var sb = new StringBuilder();
            sb.AppendLine("请生成一张完整的三视图设定板。");
            sb.AppendLine("如果本次请求附带了已生成的九宫格正面表情头像，请将该正面头像作为唯一脸部身份参考，并把它扩展成完整全身角色形象。");
            sb.AppendLine("只需要三个视图，从左到右依次为：正面视图、侧面视图、背面视图。不要添加四分之三视图、额外小图、头像插图、文字标签或多余面板。");
            sb.AppendLine("三个视图都必须是完整全身照：从头顶到脚底完整可见，包含完整头部、躯干、双腿、双脚和鞋子，脚底不能被画面边缘裁切。");
            sb.AppendLine("视角必须严格锁定：正面图必须胸口和胯部正对镜头；侧面图必须是严格 90 度纯侧身；背面图必须是严格 180 度纯背身。");
            sb.AppendLine("禁止斜身、迈步、扭胯、回头、三分之四视角、走姿、时装摆拍和任何头像特写。");
            AppendChineseCharacterIdentity(sb, entry, fullBody: true);
            sb.AppendLine("Background lock: seamless plain light gray or white studio background only; no scenery, workplace, vehicles, food trucks, rooms, doors, windows, signs, posters, furniture, shelves, plants, props, or background objects.");
            sb.AppendLine("No-bag lock: the character must not wear, carry, hold, or have any bag; no backpack, shoulder bag, tote bag, handbag, purse, satchel, messenger bag, crossbody bag, sling bag, waist bag, pouch, luggage, bag strap, shoulder strap, crossbody strap, backpack straps, bag handles, or bag hardware.");
            sb.AppendLine("Only-person lock: only the character's body, hair, clothing and shoes may appear. All other objects are forbidden: no microphone, mic stand, camera, tripod, light stand, phone, tablet, weapon, tool, umbrella, staff, suitcase, box, desk, chair, podium, cable, badge prop, handheld prop, floating prop, foreground prop, background prop, or any object near the character.");
            sb.AppendLine("同一个角色，保持完全一致的脸型、发型、发际线、刘海、发量、服装结构、鞋子和配色。");
            sb.AppendLine("站姿中性，身体垂直，双肩水平，双手自然下垂，空手，无道具，浅灰纯色背景。");
            sb.AppendLine($"风格：{styleLine}。");
            return sb.ToString().Trim();
        }

        public static string BuildChinesePromptBundle(WorkflowNode? node, CharacterDesignEntry entry)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【表情九宫格提示词】");
            sb.AppendLine(BuildChineseExpressionPrompt(node, entry));
            sb.AppendLine();
            sb.AppendLine("【三视图提示词】");
            sb.AppendLine(BuildChineseThreeViewPrompt(node, entry));
            return sb.ToString().Trim();
        }

        private static void AppendChineseCharacterIdentity(StringBuilder sb, CharacterDesignEntry entry, bool fullBody)
        {
            sb.AppendLine();
            sb.AppendLine("角色身份与外形锁定：");
            AppendChinesePromptLine(sb, "角色名", entry.Name);
            AppendChinesePromptLine(sb, "别名/身份", entry.Alias);
            AppendChinesePromptLine(sb, "基础外形", entry.BasicStats);
            AppendChinesePromptLine(sb, "职业身份", entry.Profession);
            AppendChinesePromptLine(sb, "外观提示词", entry.AppearancePrompt);
            AppendChinesePromptLine(sb, "服装与配饰", entry.CostumeNotes);
            AppendChinesePromptLine(sb, "视觉标签", entry.VisualTags);
            AppendChinesePromptLine(sb, "表演说明", entry.ActingNotes);
            sb.AppendLine(BuildChineseGenderLock(entry, fullBody));
            sb.AppendLine("English gender lock: " + BuildEnglishGenderLock(entry, fullBody));
            sb.AppendLine("Outfit single-choice lock: use one exact outfit anchor only; if text contains clothing examples or alternatives, keep only one concrete outfit and ignore the rest.");
            sb.AppendLine("English lock: preserve the exact same character identity, same face shape, same hairstyle, same hairline, same age, same gender, same outfit, same visible collar and neckline, same outfit colors.");
            sb.AppendLine();
        }

        private static void AppendChinesePromptLine(StringBuilder sb, string label, string? value)
        {
            var normalized = NormalizeSingleOutfitAnchorText(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                sb.AppendLine($"- {label}：{normalized}");
            }
        }

        private static string BuildChineseGenderLock(CharacterDesignEntry entry, bool fullBody)
        {
            return DetectGender(entry) switch
            {
                CharacterGenderHint.Male => fullBody
                    ? "- 性别硬锁：必须是成年男性；男性骨相、男性身形比例、平胸、非女性轮廓；严禁女性脸、女性身体、胸部、裙装。"
                    : "- 性别硬锁：必须是成年男性；男性脸型、男性下颌线、男性眉骨与发际线；严禁女性脸、少女感、女性妆容、长睫毛、女性化五官。",
                CharacterGenderHint.Female => fullBody
                    ? "- 性别硬锁：必须是成年女性；女性身形比例与女性轮廓；严禁男性骨相、男性躯干、胡须或男性化脸型。"
                    : "- 性别硬锁：必须是成年女性；女性脸型与女性五官比例；严禁男性脸、胡须、男性眉骨或男性化五官。",
                _ => "- 性别硬锁：严格遵循角色原始性别和年龄，不允许生成成另一种性别表现。"
            };
        }

        private static string BuildEnglishGenderLock(CharacterDesignEntry entry, bool fullBody)
        {
            return DetectGender(entry) switch
            {
                CharacterGenderHint.Male => fullBody
                    ? "adult male only, masculine body proportions, flat chest, no woman, no girl, no feminine silhouette, no breasts"
                    : "adult male only, masculine facial structure, masculine jawline, stronger brow ridge, male hairline, no woman, no girl, no feminine face, no beard changes if facial hair exists",
                CharacterGenderHint.Female => fullBody
                    ? "adult female only, feminine body proportions, feminine silhouette, no man, no boy, no masculine torso, no beard"
                    : "adult female only, feminine facial structure, feminine facial proportions, no man, no boy, no masculine face, no beard, no mustache",
                _ => "preserve the original character gender exactly, do not switch gender presentation"
            };
        }

        public static CharacterGenderHint DetectGender(CharacterDesignEntry entry)
        {
            if (entry == null)
            {
                return CharacterGenderHint.Unknown;
            }

            string haystack = string.Join(" ",
                entry.Name ?? string.Empty,
                entry.Alias ?? string.Empty,
                entry.BasicStats ?? string.Empty,
                entry.Summary ?? string.Empty,
                entry.Profession ?? string.Empty,
                entry.Personality ?? string.Empty,
                entry.AppearancePrompt ?? string.Empty,
                entry.CostumeNotes ?? string.Empty,
                entry.VisualTags ?? string.Empty);

            bool female = ContainsAnyTerm(haystack,
                "女", "女性", "女主", "女角色", "女子", "姑娘", "小姐",
                "female", "woman", "girl", "feminine",
                "she", "her", "hers", "mother", "daughter", "sister", "aunt", "princess", "lady", "queen",
                "empress", "wife", "girlfriend", "miss", "ms.", "mrs.");
            bool male = ContainsAnyTerm(haystack,
                "男", "男性", "男主", "男角色", "男子", "先生", "男孩",
                "male", "man", "boy", "masculine",
                "he", "him", "his", "father", "son", "brother", "uncle", "prince", "lord", "king",
                "emperor", "husband", "boyfriend", "gentleman", "sir", "mr.");

            if (female && !male)
            {
                return CharacterGenderHint.Female;
            }

            if (male && !female)
            {
                return CharacterGenderHint.Male;
            }

            if (female)
            {
                return CharacterGenderHint.Female;
            }

            return male ? CharacterGenderHint.Male : CharacterGenderHint.Unknown;
        }

        private static bool ContainsAnyTerm(string haystack, params string[] terms)
        {
            if (string.IsNullOrWhiteSpace(haystack))
            {
                return false;
            }

            return terms.Any(term => ContainsTerm(haystack, term));
        }

        private static bool ContainsTerm(string haystack, string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return false;
            }

            if (term.Any(IsCjkCharacter))
            {
                return haystack.Contains(term, StringComparison.OrdinalIgnoreCase);
            }

            string pattern = $@"(?<![A-Za-z0-9]){Regex.Escape(term.Trim())}(?![A-Za-z0-9])";
            return Regex.IsMatch(haystack, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool IsCjkCharacter(char ch)
        {
            return ch >= '\u3400' && ch <= '\u9fff';
        }

        public sealed class ExpressionCell
        {
            public string Label { get; }

            public string Description { get; }

            public string EnglishLabel { get; }

            public string DetailedPrompt { get; }

            public ExpressionCell(string label, string description, string englishLabel, string framing)
            {
                Label = label;
                Description = description;
                EnglishLabel = englishLabel;
                DetailedPrompt = $"{englishLabel}, {framing}";
            }

            public override string ToString() => $"{Label}（{Description}，{EnglishLabel}）";
        }
    }
}
