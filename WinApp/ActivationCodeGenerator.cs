using System.Security.Cryptography;
using System.Text;

namespace JSAI.WinApp;

internal static class ActivationCodeGenerator
{
    private const string Secret = "JSAI.MachineActivation.2026.TerryHe20900";
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const string MachinePrefix = "MC";
    private const string RegistrationPrefix = "JSAI";

    public static string CreateMachineCode(string rawFingerprint)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(rawFingerprint ?? string.Empty));
        return FormatGrouped(MachinePrefix, ToBase32(hash, 20));
    }

    public static bool TryGenerateRegistrationCode(
        string machineCode,
        out string registrationCode,
        out string normalizedMachineCode,
        out string message)
    {
        registrationCode = string.Empty;
        normalizedMachineCode = NormalizeMachineCode(machineCode);
        if (!IsPlausibleMachineCode(normalizedMachineCode))
        {
            message = "机器码格式不正确，请复制客户端授权窗口里的完整机器码。";
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var payload = $"JSAI|activation|v1|{normalizedMachineCode}";
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        registrationCode = FormatGrouped(RegistrationPrefix, ToBase32(hash, 25));
        message = "注册码已生成。";
        return true;
    }

    public static bool IsRegistrationCodeValid(string machineCode, string registrationCode)
    {
        if (!TryGenerateRegistrationCode(machineCode, out var expected, out _, out _))
        {
            return false;
        }

        return string.Equals(
            CompactCode(expected),
            CompactCode(registrationCode),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeMachineCode(string value)
    {
        var compact = CompactCode(value);
        if (compact.StartsWith(MachinePrefix, StringComparison.OrdinalIgnoreCase))
        {
            compact = compact[MachinePrefix.Length..];
        }

        return FormatGrouped(MachinePrefix, compact);
    }

    public static string NormalizeRegistrationCode(string value)
    {
        var compact = CompactCode(value);
        if (compact.StartsWith(RegistrationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            compact = compact[RegistrationPrefix.Length..];
        }

        return FormatGrouped(RegistrationPrefix, compact);
    }

    private static bool IsPlausibleMachineCode(string value)
    {
        var compact = CompactCode(value);
        return compact.StartsWith(MachinePrefix, StringComparison.OrdinalIgnoreCase) &&
               compact.Length >= MachinePrefix.Length + 16;
    }

    private static string CompactCode(string value)
    {
        return new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string FormatGrouped(string prefix, string payload)
    {
        var compact = CompactCode(payload);
        if (compact.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            compact = compact[prefix.Length..];
        }

        var groups = Enumerable.Range(0, (compact.Length + 4) / 5)
            .Select(index => compact.Substring(index * 5, Math.Min(5, compact.Length - index * 5)))
            .Where(group => group.Length > 0);
        return $"{prefix}-{string.Join("-", groups)}";
    }

    private static string ToBase32(byte[] bytes, int length)
    {
        var output = new StringBuilder(length);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5 && output.Length < length)
            {
                bitsLeft -= 5;
                output.Append(Alphabet[(buffer >> bitsLeft) & 31]);
            }

            if (bitsLeft > 0)
            {
                buffer &= (1 << bitsLeft) - 1;
            }

            if (output.Length >= length)
            {
                break;
            }
        }

        while (output.Length < length)
        {
            output.Append(Alphabet[0]);
        }

        return output.ToString();
    }
}
