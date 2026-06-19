using System;
using System.Drawing;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class TaskProcessingForm : Form
    {
        private readonly Label _titleLabel;
        private readonly Label _detailLabel;
        private readonly Panel _pulsePanel;
        private readonly System.Windows.Forms.Timer _timer;
        private int _tick;

        public TaskProcessingForm(string detail)
        {
            Text = "任务处理中";
            ClientSize = new Size(420, 190);
            MinimumSize = SizeFromClientSize(ClientSize);
            MaximumSize = SizeFromClientSize(ClientSize);
            BackColor = Color.FromArgb(28, 29, 34);
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(24, 20, 24, 22),
                BackColor = BackColor,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var pulseHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
            };
            _pulsePanel = new Panel
            {
                Width = 18,
                Height = 18,
                BackColor = Color.FromArgb(114, 78, 255),
            };
            _pulsePanel.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var brush = new SolidBrush(_pulsePanel.BackColor);
                e.Graphics.FillEllipse(brush, 0, 0, _pulsePanel.Width - 1, _pulsePanel.Height - 1);
            };
            pulseHost.Resize += (_, _) => CenterPulse();
            pulseHost.Controls.Add(_pulsePanel);
            root.Controls.Add(pulseHost, 0, 0);

            _titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold, GraphicsUnit.Point),
                Text = "任务处理中，请稍后",
            };
            root.Controls.Add(_titleLabel, 0, 1);

            _detailLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(170, 178, 196),
                TextAlign = ContentAlignment.TopCenter,
                AutoEllipsis = true,
                Text = detail,
            };
            root.Controls.Add(_detailLabel, 0, 2);

            Controls.Add(root);

            _timer = new System.Windows.Forms.Timer { Interval = 180 };
            _timer.Tick += (_, _) => Pulse();
        }

        public static TaskProcessingForm ShowFor(IWin32Window owner, string detail)
        {
            var form = new TaskProcessingForm(detail);
            form.CenterOnWorkingArea(owner);
            form.Show(owner);
            form.CenterOnWorkingArea(owner);
            form.Activate();
            form._timer.Start();
            form.Update();
            return form;
        }

        public void SetDetail(string detail)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetDetail), detail);
                return;
            }

            _detailLabel.Text = detail;
            _detailLabel.Update();
        }

        public void CloseSafely()
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(CloseSafely));
                return;
            }

            _timer.Stop();
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Pulse()
        {
            _tick++;
            var wave = (Math.Sin(_tick * 0.7) + 1D) / 2D;
            var size = 14 + (int)Math.Round(wave * 12D);
            _pulsePanel.Width = size;
            _pulsePanel.Height = size;
            _pulsePanel.BackColor = Color.FromArgb(
                120 + (int)Math.Round(wave * 80D),
                114,
                78,
                255);
            CenterPulse();
            _pulsePanel.Invalidate();
            _titleLabel.Text = "任务处理中，请稍后" + new string('.', _tick % 4);
            _titleLabel.Update();
        }

        private void CenterPulse()
        {
            if (_pulsePanel.Parent == null)
            {
                return;
            }

            _pulsePanel.Left = Math.Max(0, (_pulsePanel.Parent.ClientSize.Width - _pulsePanel.Width) / 2);
            _pulsePanel.Top = Math.Max(0, (_pulsePanel.Parent.ClientSize.Height - _pulsePanel.Height) / 2);
        }

        private void CenterOnWorkingArea(IWin32Window owner)
        {
            var screen = owner is Control ownerControl
                ? Screen.FromControl(ownerControl)
                : Screen.FromPoint(Cursor.Position);
            var area = screen.WorkingArea;
            Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
            Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
        }
    }
}
