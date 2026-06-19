using System.Text.Json.Serialization;

namespace JSAI.WinApp;

public sealed class UserSessionResponse
{
    public string Token { get; set; } = string.Empty;
    public UserProfileResponse User { get; set; } = new();
    public List<MembershipOrderResponse> Orders { get; set; } = new();
    public DateTime LastValidatedAtUtc { get; set; }
    public int OfflineGraceHours { get; set; } = 24 * 7;
}

public sealed class UserProfileResponse
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string MembershipPlan { get; set; } = string.Empty;
    public DateTime? MembershipExpiresAt { get; set; }
    public DateTime? TrialExpiresAt { get; set; }
    public bool HasActiveMembership { get; set; }
    public bool IsTrial { get; set; }
    public bool CanSaveProjects { get; set; }
}

public sealed class MembershipPlanResponse
{
    public string PlanCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int DurationMonths { get; set; }
    public decimal Price { get; set; }
    public string BillingCycle { get; set; } = string.Empty;
}

public sealed class MembershipOrderResponse
{
    public string OrderId { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public string PlanDisplayName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime MembershipExpiresAt { get; set; }
}

public sealed class VerificationCodeResponse
{
    public string Email { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public int CooldownSeconds { get; set; }
}

internal sealed class SendVerificationCodeRequest
{
    public string Email { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
}

internal sealed class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public string VerificationCode { get; set; } = string.Empty;
}

internal sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string VerificationCode { get; set; } = string.Empty;
    public string CaptchaToken { get; set; } = string.Empty;
}

internal sealed class AutoLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

internal sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

internal sealed class ResetPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string VerificationCode { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class LoginCaptchaResponse
{
    public string Token { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

internal sealed class SubscribeRequest
{
    public string PlanCode { get; set; } = string.Empty;
}

internal sealed class MembershipSessionCache
{
    public string Token { get; set; } = string.Empty;
    public UserProfileResponse User { get; set; } = new();
    public List<MembershipOrderResponse> Orders { get; set; } = new();
    public DateTime LastValidatedAtUtc { get; set; }
    public int OfflineGraceHours { get; set; } = 24 * 7;
    public string MachineBinding { get; set; } = string.Empty;
    public string IntegritySignature { get; set; } = string.Empty;
}

internal sealed class ApiErrorResponse
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
