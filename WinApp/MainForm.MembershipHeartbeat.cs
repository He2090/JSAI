using System;
using System.Drawing;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public partial class MainForm
    {
        private readonly MembershipApiClient _membershipHeartbeatClient = new(AppServerConfig.Load().ApiBaseUrl);
        private readonly System.Windows.Forms.Timer _membershipHeartbeatTimer = new();
        private bool _membershipHeartbeatRunning;

        private void HookMembershipHeartbeat()
        {
            _membershipHeartbeatTimer.Interval = 15 * 60 * 1000;
            _membershipHeartbeatTimer.Tick += async (_, _) => await RefreshMembershipSessionAsync();

            Shown += async (_, _) =>
            {
                _membershipHeartbeatTimer.Start();
                await RefreshMembershipSessionAsync(silent: true);
            };

            FormClosed += (_, _) =>
            {
                _membershipHeartbeatTimer.Stop();
                _membershipHeartbeatTimer.Dispose();
            };
        }

        private async Task RefreshMembershipSessionAsync(bool silent = false)
        {
            if (_membershipHeartbeatRunning)
            {
                return;
            }

            var currentSession = MembershipContext.CurrentSession;
            if (currentSession == null || string.IsNullOrWhiteSpace(currentSession.Token))
            {
                return;
            }

            _membershipHeartbeatRunning = true;
            try
            {
                var result = await _membershipHeartbeatClient.ValidateSessionAsync(currentSession.Token);
                if (result.success && result.session != null)
                {
                    MembershipContext.CurrentSession = result.session;
                    MembershipSessionStore.Save(result.session);
                    if (!IsDisposed && !Disposing)
                    {
                        RefreshStats();
                        if (!silent)
                        {
                            UpdateStatus("会员状态已同步。", Color.FromArgb(90, 176, 255));
                        }
                    }

                    return;
                }

                if (result.serverUnavailable)
                {
                    return;
                }

                MembershipSessionStore.Clear();
                MembershipContext.CurrentSession = null;
                if (!IsDisposed && !Disposing)
                {
                    RefreshStats();
                    BeginInvoke(new Action(() =>
                    {
                        MessageBox.Show(
                            this,
                            string.IsNullOrWhiteSpace(result.message) ? "会员状态已失效，请重新登录。" : result.message,
                            "会员验证",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        Close();
                    }));
                }
            }
            catch
            {
            }
            finally
            {
                _membershipHeartbeatRunning = false;
            }
        }
    }
}
