using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace JSAI.WinApp
{
    public sealed class ModelCallUsage
    {
        public int? PromptTokens { get; init; }

        public int? CompletionTokens { get; init; }

        public int? TotalTokens { get; init; }

        public string ToDisplayText()
        {
            if (PromptTokens == null && CompletionTokens == null && TotalTokens == null)
            {
                return "未返回";
            }

            return $"prompt={PromptTokens?.ToString() ?? "-"}, completion={CompletionTokens?.ToString() ?? "-"}, total={TotalTokens?.ToString() ?? "-"}";
        }

        public static ModelCallUsage? FromJson(JsonElement root)
        {
            if (!TryFindUsageElement(root, out var usageElement))
            {
                return null;
            }

            var usage = new ModelCallUsage
            {
                PromptTokens = ReadUsageInt(usageElement, "prompt_tokens", "input_tokens"),
                CompletionTokens = ReadUsageInt(usageElement, "completion_tokens", "output_tokens"),
                TotalTokens = ReadUsageInt(usageElement, "total_tokens", "tokens"),
            };

            return usage.PromptTokens == null && usage.CompletionTokens == null && usage.TotalTokens == null
                ? null
                : usage;
        }

        private static bool TryFindUsageElement(JsonElement root, out JsonElement usageElement)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("usage", out usageElement) && usageElement.ValueKind == JsonValueKind.Object)
                {
                    return true;
                }

                foreach (var property in root.EnumerateObject())
                {
                    if (TryFindUsageElement(property.Value, out usageElement))
                    {
                        return true;
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    if (TryFindUsageElement(item, out usageElement))
                    {
                        return true;
                    }
                }
            }

            usageElement = default;
            return false;
        }

        private static int? ReadUsageInt(JsonElement usageElement, params string[] names)
        {
            foreach (var name in names)
            {
                if (!usageElement.TryGetProperty(name, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                {
                    return number;
                }

                if (value.ValueKind == JsonValueKind.String &&
                    int.TryParse(value.GetString(), out number))
                {
                    return number;
                }
            }

            return null;
        }
    }

    public static class ModelCallLogService
    {
        private static readonly object SyncRoot = new();
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string LogPath = Path.Combine(LogDirectory, "model-call-success.log");
        private static readonly string FailureLogPath = Path.Combine(LogDirectory, "model-call-failure.log");

        public static string LogFilePath => LogPath;

        public static string LogFolderPath => LogDirectory;

        public static string FailureLogFilePath => FailureLogPath;

        public static void LogSuccess(string module, ModelInfo model, ModelCallUsage? usage, string? note = null)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);

                var provider = GetProviderName(model.Url);
                var builder = new StringBuilder();
                builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 模块：{module}");
                builder.AppendLine($"模型：{model.Name} ({model.Id})");
                builder.AppendLine($"类别：{model.Category}");
                builder.AppendLine($"提供方：{provider}");
                builder.AppendLine($"地址：{model.Url}");
                builder.AppendLine($"Token：{usage?.ToDisplayText() ?? "未返回"}");
                if (!string.IsNullOrWhiteSpace(note))
                {
                    builder.AppendLine($"备注：{note}");
                }

                builder.AppendLine();

                lock (SyncRoot)
                {
                    File.AppendAllText(LogPath, builder.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Ignore logging failures.
            }
        }

        public static void LogFailure(string module, ModelInfo? model, string error, string? responseBody = null, string? note = null)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);

                var builder = new StringBuilder();
                builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 模块：{module}");
                if (model != null)
                {
                    var provider = GetProviderName(model.Url);
                    builder.AppendLine($"模型：{model.Name} ({model.Id})");
                    builder.AppendLine($"类别：{model.Category}");
                    builder.AppendLine($"提供方：{provider}");
                    builder.AppendLine($"地址：{model.Url}");
                }
                else
                {
                    builder.AppendLine("模型：未识别");
                }

                builder.AppendLine($"错误：{error}");
                if (!string.IsNullOrWhiteSpace(responseBody))
                {
                    builder.AppendLine("返回：");
                    builder.AppendLine(responseBody);
                }

                if (!string.IsNullOrWhiteSpace(note))
                {
                    builder.AppendLine($"备注：{note}");
                }

                builder.AppendLine();

                lock (SyncRoot)
                {
                    File.AppendAllText(FailureLogPath, builder.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Ignore logging failures.
            }
        }

        private static string GetProviderName(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "未知";
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return "未知";
            }

            return uri.Host;
        }
    }
}
