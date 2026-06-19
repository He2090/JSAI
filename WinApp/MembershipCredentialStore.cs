using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JSAI.WinApp;

internal static class MembershipCredentialStore
{
    private const string IntegritySecret = "JSAI.MemberCredential.2026.TerryHe20900";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static string CredentialPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "member-credential.dat");

    public static (string Email, string Password)? Load()
    {
        if (!File.Exists(CredentialPath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(CredentialPath);
            var jsonBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(jsonBytes);
            var cache = JsonSerializer.Deserialize<CredentialCache>(json, JsonOptions);
            if (cache == null || !IsValid(cache))
            {
                return null;
            }

            return (cache.Email, cache.Password);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var cache = new CredentialCache
        {
            Email = email.Trim(),
            Password = password,
            MachineBinding = ComputeMachineBinding(),
        };
        cache.IntegritySignature = ComputeIntegrity(cache);

        var json = JsonSerializer.Serialize(cache, JsonOptions);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(CredentialPath, protectedBytes);
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(CredentialPath))
            {
                File.Delete(CredentialPath);
            }
        }
        catch
        {
        }
    }

    private static bool IsValid(CredentialCache cache)
    {
        if (string.IsNullOrWhiteSpace(cache.Email) ||
            string.IsNullOrWhiteSpace(cache.Password) ||
            string.IsNullOrWhiteSpace(cache.MachineBinding) ||
            string.IsNullOrWhiteSpace(cache.IntegritySignature))
        {
            return false;
        }

        if (!string.Equals(cache.MachineBinding, ComputeMachineBinding(), StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(cache.IntegritySignature, ComputeIntegrity(cache), StringComparison.Ordinal);
    }

    private static string ComputeMachineBinding()
    {
        var raw = $"{Environment.MachineName}|{Environment.UserName}|{AppDomain.CurrentDomain.BaseDirectory}";
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)));
    }

    private static string ComputeIntegrity(CredentialCache cache)
    {
        var source = string.Join("|",
            cache.Email,
            cache.Password,
            cache.MachineBinding,
            IntegritySecret);
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(source)));
    }

    private sealed class CredentialCache
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string MachineBinding { get; set; } = string.Empty;
        public string IntegritySignature { get; set; } = string.Empty;
    }
}
