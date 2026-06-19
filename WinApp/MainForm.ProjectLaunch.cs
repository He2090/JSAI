using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public partial class MainForm
    {
        private bool _launcherShown;
        private string _currentProjectFilePath = string.Empty;

        private void HookProjectLaunchers()
        {
            Shown += (_, _) => ShowHomeScreenIfNeeded();
        }

        private void ShowHomeScreenIfNeeded()
        {
            if (_launcherShown)
            {
                return;
            }

            _launcherShown = true;
            ShowHomeScreen(force: false);
        }

        private void ShowHomeScreen(bool force)
        {
            if (_running && !force)
            {
                return;
            }

            _homePanel.Visible = true;
            _homePanel.BringToFront();
            _workspaceSplit.Visible = false;
            UpdateStatus("请选择一个创作类型。", Color.FromArgb(90, 176, 255));
        }

        private void HideHomeScreen()
        {
            _homePanel.Visible = false;
            _workspaceSplit.Visible = true;
            _workspaceSplit.BringToFront();
        }

        private void HandleProjectLaunchMode(ProjectLaunchMode mode)
        {
            switch (mode)
            {
                case ProjectLaunchMode.AiAnimeProject:
                    CreateNewProject(ProjectWorkspaceMode.AiAnimeProject);
                    ProjectStoragePaths.EnsureProjectRootPath(ProjectWorkspaceMode.AiAnimeProject, _document.ProjectName);
                    HideHomeScreen();
                    break;
                case ProjectLaunchMode.LoadProject:
                    LoadProjectFile();
                    break;
                case ProjectLaunchMode.TextToImage:
                    CreateNewProject(ProjectWorkspaceMode.DirectStudio, WorkflowNodeCatalog.TextToImage);
                    DirectStudioImageLibraryService.EnsureTodayDirectory(_document.ProjectName, WorkflowNodeCatalog.TextToImage);
                    HideHomeScreen();
                    break;
                case ProjectLaunchMode.TextToVideo:
                    CreateNewProject(ProjectWorkspaceMode.DirectStudio, WorkflowNodeCatalog.TextToVideo);
                    ProjectStoragePaths.EnsureProjectRootPath(ProjectWorkspaceMode.DirectStudio, _document.ProjectName, WorkflowNodeCatalog.TextToVideo);
                    HideHomeScreen();
                    break;
                case ProjectLaunchMode.TextImageToVideo:
                    CreateNewProject(ProjectWorkspaceMode.DirectStudio, WorkflowNodeCatalog.TextImageToVideo);
                    ProjectStoragePaths.EnsureProjectRootPath(ProjectWorkspaceMode.DirectStudio, _document.ProjectName, WorkflowNodeCatalog.TextImageToVideo);
                    HideHomeScreen();
                    break;
            }
        }

        private void LoadProjectFile()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = $"MyAI 项目|*{WorkflowStore.ProjectFileExtension}|全部文件|*.*",
                Title = "载入项目",
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var document = WorkflowStore.LoadProjectFile(dialog.FileName, throwOnFailure: false);
            if (document == null)
            {
                MessageBox.Show(this, "项目读取失败或文件格式无效。", "载入项目", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var session = AddProjectSession(document, dialog.FileName, isDirty: false);
            ActivateProjectSession(session);
            SaveWorkingCopy();
            HideHomeScreen();
            UpdateStatus($"项目已载入：{Path.GetFileName(dialog.FileName)}", Color.FromArgb(90, 176, 255));
        }

        private bool SaveProjectFile(bool showSuccessMessage = true, bool forceChoosePath = false)
        {
            if (_activeSession == null)
            {
                return false;
            }

            if (!EnsureProjectSaveAllowed(showSuccessMessage))
            {
                return false;
            }

            SaveWorkingCopy();

            var targetPath = forceChoosePath ? string.Empty : _currentProjectFilePath;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                using var dialog = new SaveFileDialog
                {
                    Filter = $"MyAI 项目|*{WorkflowStore.ProjectFileExtension}",
                    FileName = $"{WorkflowStore.BuildProjectDirectoryName(_document.ProjectName)}{WorkflowStore.ProjectFileExtension}",
                    Title = forceChoosePath ? "另存项目" : "保存项目",
                };

                if (!string.IsNullOrWhiteSpace(_currentProjectFilePath))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(_currentProjectFilePath);
                }

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                targetPath = dialog.FileName;
            }

            try
            {
                var result = WorkflowStore.SaveProjectFile(_document, targetPath);
                _currentProjectFilePath = result.FilePath;
                _activeSession.ProjectFilePath = result.FilePath;
                MarkActiveSessionSaved();
                UpdateStatus($"项目已保存：{result.FilePath}", Color.FromArgb(90, 176, 255));

                if (showSuccessMessage)
                {
                    MessageBox.Show(
                        this,
                        $"项目已保存完成。\n\n文件：{result.FilePath}\n节点文本：{result.OutputFileCount} 个\n复制文件：{result.CopiedFileCount} 个",
                        "保存成功",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                UpdateStatus($"项目保存失败：{ex.Message}", Color.DarkOrange);
                if (showSuccessMessage)
                {
                    MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                return false;
            }
        }

        private bool SaveProjectFileAs(bool showSuccessMessage = true)
        {
            return SaveProjectFile(showSuccessMessage, forceChoosePath: true);
        }

        private bool EnsureProjectSaveAllowed(bool showMessage = true)
        {
            if (MembershipContext.CurrentSession?.User?.CanSaveProjects == true)
            {
                return true;
            }

            const string message = "当前机器尚未使用注册码激活，暂时不能保存项目。请复制授权窗口里的机器码，到服务端后台生成注册码并激活后再保存。";
            UpdateStatus(message, Color.DarkOrange);
            if (showMessage)
            {
                MessageBox.Show(this, message, "保存项目", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return false;
        }
    }
}
