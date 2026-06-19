using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JSAI.WinApp
{
    internal static class WorkflowParseHelpers
    {
        public static string CleanExtractedValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("**", string.Empty, StringComparison.Ordinal)
                .Replace("`", string.Empty, StringComparison.Ordinal)
                .Trim()
                .Trim('|')
                .Trim();
        }

        public static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
        }

        public static string ChooseLongerText(string current, string candidate)
        {
            if (string.IsNullOrWhiteSpace(current))
            {
                return candidate?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(candidate))
            {
                return current.Trim();
            }

            return candidate.Trim().Length > current.Trim().Length ? candidate.Trim() : current.Trim();
        }

        public static string ExtractMarkdownSection(string text, string heading, string? nextHeading = null)
        {
            return ExtractMarkdownSection(
                text,
                new[] { heading },
                string.IsNullOrWhiteSpace(nextHeading) ? Array.Empty<string>() : new[] { nextHeading! });
        }

        public static string ExtractMarkdownSection(string text, IEnumerable<string> headings, params string[] nextHeadings)
        {
            text ??= string.Empty;
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

        public static string ExtractMarkdownField(string text, params string[] labels)
        {
            text ??= string.Empty;
            foreach (var label in labels)
            {
                var escapedLabel = Regex.Escape(label);
                var patterns = new[]
                {
                    $@"(?im)^[#>\-\*\s]*{escapedLabel}\s*[:：]\s*(.+)$",
                    $@"(?im)(?:^|\|)\s*\*{{0,2}}\s*{escapedLabel}\s*\*{{0,2}}\s*[:：]\s*(.+?)(?=\s*\||\r?\n|$)"
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

        public static string ReadJsonString(JsonElement element, string propertyName)
        {
            if (!TryGetJsonProperty(element, propertyName, out var value))
            {
                return string.Empty;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty,
            };
        }

        public static int ReadJsonInt(JsonElement element, string propertyName, int defaultValue)
        {
            if (!TryGetJsonProperty(element, propertyName, out var value))
            {
                return defaultValue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out var number) => number,
                JsonValueKind.String when int.TryParse(value.GetString(), out var textValue) => textValue,
                _ => defaultValue,
            };
        }

        public static List<string> ReadJsonStringList(JsonElement element, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (!TryGetJsonProperty(element, propertyName, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Array)
                {
                    return value
                        .EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()?.Trim() ?? string.Empty)
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .ToList();
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    return Regex.Split(value.GetString() ?? string.Empty, @"[、,，/｜\| ]+")
                        .Select(item => item.Trim())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .ToList();
                }
            }

            return new List<string>();
        }

        private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement value)
        {
            value = default;
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var normalizedName = NormalizeJsonPropertyName(propertyName);
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(NormalizeJsonPropertyName(property.Name), normalizedName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeJsonPropertyName(string value)
        {
            return new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }
    }
}
