using System;
using System.Drawing;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public static class PromptDialog
    {
        public static string? Show(IWin32Window owner, string title, string prompt, string initialValue = "")
        {
            using var form = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(420, 150),
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point),
            };

            var label = new Label
            {
                AutoSize = false,
                Location = new Point(16, 16),
                Size = new Size(388, 24),
                Text = prompt,
            };

            var textBox = new TextBox
            {
                Location = new Point(16, 48),
                Size = new Size(388, 27),
                Text = initialValue,
            };

            var okButton = new Button
            {
                DialogResult = DialogResult.OK,
                Location = new Point(214, 96),
                Size = new Size(90, 32),
                Text = "确定",
            };

            var cancelButton = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(314, 96),
                Size = new Size(90, 32),
                Text = "取消",
            };

            form.Controls.Add(label);
            form.Controls.Add(textBox);
            form.Controls.Add(okButton);
            form.Controls.Add(cancelButton);
            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;

            return form.ShowDialog(owner) == DialogResult.OK
                ? textBox.Text.Trim()
                : null;
        }
    }
}
