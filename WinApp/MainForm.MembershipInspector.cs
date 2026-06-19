using System;
using System.Drawing;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public partial class MainForm
    {
        private readonly Label _membershipUsernameValueLabel = new();
        private readonly Label _membershipPlanValueLabel = new();
        private readonly Label _membershipExpiryValueLabel = new();
        private readonly Label _membershipSavePermissionValueLabel = new();
        private readonly Label _membershipInspectorStatusLabel = new();

        private Control BuildMembershipInfoBody()
        {
            var root = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                BackColor = Color.FromArgb(18, 22, 32),
                AutoScroll = true,
            };

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = Color.Transparent,
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            _membershipInspectorStatusLabel.Dock = DockStyle.Top;
            _membershipInspectorStatusLabel.AutoSize = true;
            _membershipInspectorStatusLabel.Margin = new Padding(0, 0, 0, 10);
            _membershipInspectorStatusLabel.ForeColor = Color.FromArgb(171, 183, 205);
            _membershipInspectorStatusLabel.Text = "当前本机授权信息会显示在这里。";

            content.Controls.Add(_membershipInspectorStatusLabel, 0, 0);
            content.Controls.Add(CreateMembershipInfoRow("机器码", _membershipUsernameValueLabel), 0, 1);
            content.Controls.Add(CreateMembershipInfoRow("授权类型", _membershipPlanValueLabel), 0, 2);
            content.Controls.Add(CreateMembershipInfoRow("授权时间", _membershipExpiryValueLabel), 0, 3);
            content.Controls.Add(CreateMembershipInfoRow("保存权限", _membershipSavePermissionValueLabel), 0, 4);

            root.Controls.Add(content);
            return root;
        }

        private Control CreateMembershipInfoRow(string labelText, Label valueLabel)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                ColumnCount = 2,
                Margin = new Padding(0, 0, 0, 8),
                BackColor = Color.Transparent,
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var label = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(177, 190, 214),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = labelText,
            };

            valueLabel.Dock = DockStyle.Fill;
            valueLabel.ForeColor = Color.White;
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            valueLabel.AutoEllipsis = true;

            panel.Controls.Add(label, 0, 0);
            panel.Controls.Add(valueLabel, 1, 0);
            return panel;
        }

        private void RefreshMembershipInspector()
        {
            var session = MembershipContext.CurrentSession;
            var user = session?.User;
            if (user == null)
            {
                _membershipInspectorStatusLabel.Text = "当前机器未激活。";
                _membershipUsernameValueLabel.Text = "未激活";
                _membershipPlanValueLabel.Text = "未激活";
                _membershipExpiryValueLabel.Text = "--";
                _membershipSavePermissionValueLabel.Text = "禁止";
                return;
            }

            _membershipUsernameValueLabel.Text = string.IsNullOrWhiteSpace(user.UserId) ? user.DisplayName : user.UserId;
            _membershipPlanValueLabel.Text = DescribeMembershipPlan(user);
            _membershipExpiryValueLabel.Text = ResolveMembershipExpiryText(user);
            _membershipSavePermissionValueLabel.Text = user.CanSaveProjects ? "允许" : "禁止";
            _membershipInspectorStatusLabel.Text = MembershipContext.CurrentDisplayText;
        }

        private static string DescribeMembershipPlan(UserProfileResponse user)
        {
            if (user.IsTrial)
            {
                return string.Equals(user.MembershipPlan, "unlicensed", StringComparison.OrdinalIgnoreCase)
                    ? "未激活"
                    : "试用会员";
            }

            return (user.MembershipPlan ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "activation" => "注册码授权",
                "unlicensed" => "未激活",
                "monthly" => "月度会员",
                "yearly" => "年度会员",
                _ => string.IsNullOrWhiteSpace(user.MembershipPlan) ? "未开通" : user.MembershipPlan
            };
        }

        private static string ResolveMembershipExpiryText(UserProfileResponse user)
        {
            if (string.Equals(user.MembershipPlan, "activation", StringComparison.OrdinalIgnoreCase))
            {
                return "永久";
            }

            if (string.Equals(user.MembershipPlan, "unlicensed", StringComparison.OrdinalIgnoreCase))
            {
                return "--";
            }

            var expiry = user.HasActiveMembership ? user.MembershipExpiresAt : user.TrialExpiresAt;
            return expiry?.ToString("yyyy-MM-dd HH:mm") ?? "--";
        }
    }
}
