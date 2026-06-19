using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class ImageGalleryForm : Form
    {
        private readonly List<string> _imagePaths;
        private readonly PictureBox _pictureBox;
        private readonly Label _pageLabel;
        private readonly Button _prevButton;
        private readonly Button _nextButton;
        private readonly Button _openButton;
        private int _index;

        public ImageGalleryForm(IEnumerable<string> imagePaths, int startIndex, string title)
        {
            _imagePaths = imagePaths?
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            _index = _imagePaths.Count == 0 ? 0 : Math.Max(0, Math.Min(startIndex, _imagePaths.Count - 1));

            Text = string.IsNullOrWhiteSpace(title) ? "图片预览" : title;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(960, 720);
            ClientSize = new Size(1180, 860);
            BackColor = Color.FromArgb(24, 25, 30);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            _pageLabel = new Label { Dock = DockStyle.Fill, ForeColor = Color.Gainsboro, TextAlign = ContentAlignment.MiddleCenter };
            _prevButton = CreateToolbarButton("上一张");
            _nextButton = CreateToolbarButton("下一张");
            _openButton = CreateToolbarButton("打开文件");
            _prevButton.Click += (_, _) => ChangePage(-1);
            _nextButton.Click += (_, _) => ChangePage(1);
            _openButton.Click += (_, _) => OpenCurrentFile();

            var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 48, ColumnCount = 4, Padding = new Padding(12, 8, 12, 8), BackColor = Color.FromArgb(30, 31, 36) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            header.Controls.Add(_prevButton, 0, 0);
            header.Controls.Add(_pageLabel, 1, 0);
            header.Controls.Add(_nextButton, 2, 0);
            header.Controls.Add(_openButton, 3, 0);

            _pictureBox = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 19, 24), SizeMode = PictureBoxSizeMode.Zoom };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14), BackColor = Color.FromArgb(24, 25, 30) };
            body.Controls.Add(_pictureBox);

            Controls.Add(body);
            Controls.Add(header);
            LoadCurrentImage();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pictureBox.Image?.Dispose();
            }

            base.Dispose(disposing);
        }

        private static Button CreateToolbarButton(string text)
        {
            var button = new Button { Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Text = text, BackColor = Color.FromArgb(62, 69, 84), ForeColor = Color.White, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private void ChangePage(int delta)
        {
            if (_imagePaths.Count == 0)
            {
                return;
            }

            _index = Math.Max(0, Math.Min(_index + delta, _imagePaths.Count - 1));
            LoadCurrentImage();
        }

        private void OpenCurrentFile()
        {
            if (_imagePaths.Count == 0)
            {
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = _imagePaths[_index], UseShellExecute = true });
        }

        private void LoadCurrentImage()
        {
            _pictureBox.Image?.Dispose();
            _pictureBox.Image = null;

            if (_imagePaths.Count == 0)
            {
                _pageLabel.Text = "没有可预览的图片";
                _prevButton.Enabled = false;
                _nextButton.Enabled = false;
                _openButton.Enabled = false;
                return;
            }

            _pageLabel.Text = $"第 {_index + 1} / {_imagePaths.Count} 张";
            _prevButton.Enabled = _index > 0;
            _nextButton.Enabled = _index < _imagePaths.Count - 1;
            _openButton.Enabled = true;

            using var stream = new FileStream(_imagePaths[_index], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var image = Image.FromStream(stream);
            _pictureBox.Image = new Bitmap(image);
        }
    }
}
