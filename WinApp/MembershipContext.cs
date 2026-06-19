namespace JSAI.WinApp;

internal static class MembershipContext
{
    public static UserSessionResponse? CurrentSession { get; set; }

    public static bool HasPaidMembership =>
        CurrentSession?.User?.HasActiveMembership == true &&
        CurrentSession.User.CanSaveProjects;

    public static bool CanAccessSoftware(UserSessionResponse? session)
    {
        return CanAccessSoftware(session?.User);
    }

    public static bool CanAccessSoftware(UserProfileResponse? user)
    {
        if (user == null)
        {
            return false;
        }

        if (string.Equals(user.MembershipPlan, "activation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(user.MembershipPlan, "unlicensed", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var now = DateTime.Now;
        if (user.HasActiveMembership)
        {
            if (!user.MembershipExpiresAt.HasValue)
            {
                return true;
            }

            if (user.MembershipExpiresAt.Value > now)
            {
                return true;
            }
        }

        return user.IsTrial &&
               user.TrialExpiresAt.HasValue &&
               user.TrialExpiresAt.Value > now;
    }

    public static string CurrentDisplayText
    {
        get
        {
            if (CurrentSession?.User == null)
            {
                return string.Empty;
            }

            var user = CurrentSession.User;
            if (string.Equals(user.MembershipPlan, "activation", StringComparison.OrdinalIgnoreCase))
            {
                return $"{user.DisplayName} · 注册码授权 · 保存权限允许";
            }

            if (string.Equals(user.MembershipPlan, "unlicensed", StringComparison.OrdinalIgnoreCase))
            {
                return $"{user.DisplayName} · 未激活 · 保存权限禁止";
            }

            var expiresText = user.MembershipExpiresAt?.ToString("yyyy-MM-dd HH:mm") ?? "未开通";
            var planText = user.IsTrial
                ? "试用会员"
                : user.MembershipPlan?.Trim().ToLowerInvariant() switch
                {
                    "monthly" => "月度会员",
                    "yearly" => "年度会员",
                    _ => string.IsNullOrWhiteSpace(user.MembershipPlan) ? "未开通" : user.MembershipPlan,
                };
            return $"{user.DisplayName} · {planText} · 到期 {expiresText}";
        }
    }
}
