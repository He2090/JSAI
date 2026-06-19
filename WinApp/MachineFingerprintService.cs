using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace JSAI.WinApp;

internal sealed class MachineFingerprintSnapshot
{
    public string CpuId { get; init; } = string.Empty;
    public string DiskSerial { get; init; } = string.Empty;
    public string RawFingerprint { get; init; } = string.Empty;
    public string MachineCode { get; init; } = string.Empty;

    public bool HasCpuId => !string.IsNullOrWhiteSpace(CpuId);

    public bool HasDiskSerial => !string.IsNullOrWhiteSpace(DiskSerial);
}

internal static class MachineFingerprintService
{
    public static MachineFingerprintSnapshot GetSnapshot()
    {
        var cpuId = NormalizeComponent(
            ReadWmicValue("cpu", "ProcessorId") ??
            ReadPowerShellValue("Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty ProcessorId"));

        var diskSerial = NormalizeComponent(
            ReadWmicValue("diskdrive", "SerialNumber") ??
            ReadPowerShellValue("Get-CimInstance Win32_DiskDrive | Select-Object -First 1 -ExpandProperty SerialNumber"));

        var fallback = NormalizeComponent($"{Environment.MachineName}|{Environment.UserName}");
        var raw = $"{cpuId}|{diskSerial}|{fallback}";
        if (string.IsNullOrWhiteSpace(cpuId) && string.IsNullOrWhiteSpace(diskSerial))
        {
            raw = BuildFallbackFingerprint(fallback);
        }

        return new MachineFingerprintSnapshot
        {
            CpuId = cpuId,
            DiskSerial = diskSerial,
            RawFingerprint = raw,
            MachineCode = ActivationCodeGenerator.CreateMachineCode(raw),
        };
    }

    public static string ComputeFingerprintHash(string rawFingerprint)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(rawFingerprint ?? string.Empty)));
    }

    private static string? ReadWmicValue(string alias, string propertyName)
    {
        var output = RunProcess("wmic.exe", $"{alias} get {propertyName} /value");
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            var prefix = propertyName + "=";
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed[prefix.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? ReadPowerShellValue(string command)
    {
        var output = RunProcess("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"");
        return string.IsNullOrWhiteSpace(output)
            ? null
            : output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
    }

    private static string? RunProcess(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            if (!process.WaitForExit(3000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return null;
            }

            return process.StandardOutput.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeComponent(string? value)
    {
        return new string((value ?? string.Empty)
            .Trim()
            .Where(ch => !char.IsWhiteSpace(ch))
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string BuildFallbackFingerprint(string fallback)
    {
        var baseDirectory = NormalizeComponent(AppDomain.CurrentDomain.BaseDirectory);
        return $"FALLBACK|{fallback}|{baseDirectory}";
    }
}
