using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class VideoPlaybackForm : Form
    {
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int mciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

        private readonly string _filePath;
        private readonly string _alias;
        private readonly Panel _videoHost;
        private bool _opened;

        public VideoPlaybackForm(string filePath, string? title = null)
        {
            _filePath = filePath ?? string.Empty;
            _alias = "jsai_video_" + Guid.NewGuid().ToString("N");

            Text = string.IsNullOrWhiteSpace(title) ? "视频播放" : title.Trim();
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(980, 680);
            MinimumSize = new Size(760, 520);
            BackColor = Color.FromArgb(20, 20, 24);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(16),
                BackColor = BackColor,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));

            var titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold, GraphicsUnit.Point),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = Path.GetFileName(_filePath),
            };
            root.Controls.Add(titleLabel, 0, 0);

            _videoHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                Margin = new Padding(0, 8, 0, 8),
            };
            root.Controls.Add(_videoHost, 0, 1);

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = Padding.Empty,
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

            actions.Controls.Add(CreateButton("播放", (_, _) => Send("play " + _alias)), 0, 0);
            actions.Controls.Add(CreateButton("暂停", (_, _) => Send("pause " + _alias)), 1, 0);
            actions.Controls.Add(CreateButton("停止", (_, _) => Send("stop " + _alias)), 2, 0);
            actions.Controls.Add(CreateButton("打开目录", (_, _) => OpenFolder()), 3, 0);
            root.Controls.Add(actions, 0, 2);

            Controls.Add(root);

            Shown += (_, _) => OpenVideo();
            Resize += (_, _) => ResizeVideoWindow();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            CloseVideo();
            base.OnClosing(e);
        }

        private static Button CreateButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(56, 60, 74),
                ForeColor = Color.White,
                Margin = new Padding(0, 0, 8, 0),
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += onClick;
            return button;
        }

        private void OpenVideo()
        {
            if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
            {
                return;
            }

            CloseVideo();

            var command = $"open \"{_filePath}\" type mpegvideo alias {_alias} parent {_videoHost.Handle} style child";
            var error = mciSendString(command, null, 0, Handle);
            if (error != 0)
            {
                TryOpenExternalPlayer();
                return;
            }

            _opened = true;
            ResizeVideoWindow();
            Send("play " + _alias);
        }

        private void ResizeVideoWindow()
        {
            if (!_opened || _videoHost.IsDisposed || _videoHost.Width <= 0 || _videoHost.Height <= 0)
            {
                return;
            }

            Send($"put {_alias} window at 0 0 {_videoHost.ClientSize.Width} {_videoHost.ClientSize.Height}");
        }

        private void CloseVideo()
        {
            if (!_opened)
            {
                return;
            }

            Send("stop " + _alias);
            Send("close " + _alias);
            _opened = false;
        }

        private void OpenFolder()
        {
            try
            {
                var folder = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                }
            }
            catch
            {
            }
        }

        private void TryOpenExternalPlayer()
        {
            try
            {
                Process.Start(new ProcessStartInfo(_filePath) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        private void Send(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            mciSendString(command, null, 0, Handle);
        }
    }
}
