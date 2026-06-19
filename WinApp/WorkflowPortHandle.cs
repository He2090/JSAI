using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class WorkflowPortHandle : Control
    {
        private bool _hovered;
        private bool _selected;
        private bool _pending;

        public WorkflowPortHandle(WorkflowPortKind portKind)
        {
            PortKind = portKind;
            Size = new Size(18, 18);
            Cursor = Cursors.Cross;
            BackColor = Color.FromArgb(28, 28, 30);
            Margin = Padding.Empty;
            TabStop = false;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);

            MouseEnter += (_, _) =>
            {
                _hovered = true;
                Invalidate();
            };

            MouseLeave += (_, _) =>
            {
                _hovered = false;
                Invalidate();
            };
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            using var path = new GraphicsPath();
            path.AddEllipse(new Rectangle(0, 0, Width, Height));
            Region = new Region(path);
        }

        public WorkflowPortKind PortKind { get; }

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                {
                    return;
                }

                _selected = value;
                Invalidate();
            }
        }

        public bool Pending
        {
            get => _pending;
            set
            {
                if (_pending == value)
                {
                    return;
                }

                _pending = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var accent = PortKind == WorkflowPortKind.Input
                ? Color.FromArgb(34, 211, 238)
                : Color.FromArgb(168, 85, 247);

            if (Pending)
            {
                accent = Color.FromArgb(255, 149, 0);
            }

            var outerRect = new Rectangle(1, 1, Width - 3, Height - 3);
            var coreRect = new Rectangle(4, 4, Width - 9, Height - 9);

            var ringAlpha = Pending ? 210 : _hovered ? 170 : _selected ? 130 : 70;
            using var ringPen = new Pen(Color.FromArgb(ringAlpha, accent), Pending ? 2.2F : 1.6F);
            using var outerBrush = new SolidBrush(Color.FromArgb(220, 28, 28, 30));
            using var coreBrush = new SolidBrush(Color.FromArgb(245, 250, 252));
            using var plusPen = new Pen(Color.FromArgb(26, 31, 44), 2F);

            e.Graphics.FillEllipse(outerBrush, outerRect);
            e.Graphics.DrawEllipse(ringPen, outerRect);
            e.Graphics.FillEllipse(coreBrush, coreRect);

            var midX = Width / 2;
            var midY = Height / 2;
            e.Graphics.DrawLine(plusPen, midX - 2, midY, midX + 2, midY);
            e.Graphics.DrawLine(plusPen, midX, midY - 2, midX, midY + 2);

            if (_hovered || Pending)
            {
                using var glowPen = new Pen(Color.FromArgb(Pending ? 120 : 80, accent), 4F);
                e.Graphics.DrawEllipse(glowPen, outerRect);
            }
        }
    }
}
