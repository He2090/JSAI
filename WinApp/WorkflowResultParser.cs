using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JSAI.WinApp
{
    public static class WorkflowResultParser
    {
        internal static readonly JsonSerializerOptions ReadableJsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static string NormalizeTextResult(string nodeType, string result)
        {
            var normalized = (result ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            normalized = normalized.Replace("\r\n", "\n").Replace("\r", "\n");
            normalized = Regex.Replace(normalized, @"^\s*```(?:markdown|md|text|txt|json)?\s*\n?", string.Empty, RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\n?```\s*$", string.Empty, RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"^\s*json\s*(?=[\{\[])", string.Empty, RegexOptions.IgnoreCase);
            if ((normalized.StartsWith("{", StringComparison.Ordinal) || normalized.StartsWith("[", StringComparison.Ordinal)) &&
                TryFormatJson(normalized, out var formattedJson))
            {
                normalized = formattedJson;
            }

            normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
            return normalized.Replace("\n", Environment.NewLine).Trim();
        }

        public static bool TryExtractJsonPayload(string text, out string jsonPayload)
        {
            var normalized = NormalizeTextResult(string.Empty, text);
            if (TryFormatJson(normalized, out var formattedJson))
            {
                jsonPayload = formattedJson;
                return true;
            }

            foreach (var openingChar in new[] { '{', '[' })
            {
                var start = normalized.IndexOf(openingChar);
                if (start < 0)
                {
                    continue;
                }

                if (!TryFindMatchingJsonEnd(normalized, start, out var end))
                {
                    continue;
                }

                var candidate = normalized.Substring(start, end - start + 1).Trim();
                if (TryFormatJson(candidate, out formattedJson))
                {
                    jsonPayload = formattedJson;
                    return true;
                }
            }

            jsonPayload = string.Empty;
            return false;
        }

        public static bool TryFormatJson(string rawJson, out string formattedJson)
        {
            try
            {
                using var document = JsonDocument.Parse(rawJson, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
                formattedJson = JsonSerializer.Serialize(document.RootElement, ReadableJsonOptions);
                return true;
            }
            catch
            {
                formattedJson = string.Empty;
                return false;
            }
        }

        private static bool TryFindMatchingJsonEnd(string text, int startIndex, out int endIndex)
        {
            endIndex = -1;
            if (string.IsNullOrWhiteSpace(text) || startIndex < 0 || startIndex >= text.Length)
            {
                return false;
            }

            var openingChar = text[startIndex];
            var closingChar = openingChar == '{' ? '}' : ']';
            var depth = 0;
            var inString = false;
            var escaped = false;

            for (var index = startIndex; index < text.Length; index++)
            {
                var current = text[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == openingChar)
                {
                    depth++;
                }
                else if (current == closingChar)
                {
                    depth--;
                    if (depth == 0)
                    {
                        endIndex = index;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
