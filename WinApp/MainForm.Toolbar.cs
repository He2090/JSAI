using System;
using System.Drawing;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public partial class MainForm
    {
        private void ConfigureToolbarRuntimeLayout()
        {
            ConfigureToolbarButton(_modelCallLogButton, "模型调用日志", (_, _) => OpenModelCallLog(), Color.FromArgb(40, 50, 72), Color.White);
            _loadProjectButton.Width = 104;
            _saveProjectButton.Width = 104;
            _saveProjectAsButton.Width = 104;
            _modelCallLogButton.Width = 118;
            _modelSettingsButton.Width = 104;

            if (_runWorkflowButton.Parent is not FlowLayoutPanel actions)
            {
                return;
            }

            if (!actions.Controls.Contains(_modelCallLogButton))
            {
                actions.Controls.Add(_modelCallLogButton);
            }

            if (!actions.Controls.Contains(_saveProjectAsButton))
            {
                actions.Controls.Add(_saveProjectAsButton);
            }

            actions.WrapContents = false;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            actions.Margin = new Padding(24, 0, 0, 0);
            actions.Padding = new Padding(12, 5, 12, 5);

            actions.Controls.SetChildIndex(_newProjectButton, 0);
            actions.Controls.SetChildIndex(_loadProjectButton, 1);
            actions.Controls.SetChildIndex(_saveProjectButton, 2);
            actions.Controls.SetChildIndex(_saveProjectAsButton, 3);
            actions.Controls.SetChildIndex(_importWorkflowButton, 4);
            actions.Controls.SetChildIndex(_exportWorkflowButton, 5);
            actions.Controls.SetChildIndex(_modelCallLogButton, 6);
            actions.Controls.SetChildIndex(_runWorkflowButton, 7);
            actions.Controls.SetChildIndex(_modelSettingsButton, 8);
        }

        private void OpenModelCallLog()
        {
            try
            {
                using var form = new ModelCallLogForm();
                form.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"打开模型调用日志失败：{ex.Message}", "模型调用日志", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
