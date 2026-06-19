using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public partial class MainForm
    {
        private readonly ListView _directImageHistoryListView = new();
        private readonly Button _openDirectImageHistoryButton = new();
        private readonly Button _deleteDirectImageHistoryButton = new();
        private Control? _directHistoryCard;

        private Control BuildDirectImageHistoryCard()
        {
            var panel = CreateCard("图片列表");
            var body = CreateCardBody();

            ConfigureActionButton(
                _openDirectImageHistoryButton,
                "放大查看",
                (_, _) => OpenSelectedDirectImageHistory(),
                Color.FromArgb(36, 56, 94),
                Color.White);
            _openDirectImageHistoryButton.Dock = DockStyle.Top;
            _openDirectImageHistoryButton.Enabled = false;

            ConfigureActionButton(
                _deleteDirectImageHistoryButton,
                "删除图片",
                (_, _) => DeleteSelectedDirectImageHistory(),
                Color.FromArgb(91, 46, 46),
                Color.White);
            _deleteDirectImageHistoryButton.Dock = DockStyle.Top;
            _deleteDirectImageHistoryButton.Enabled = false;

            _directImageHistoryListView.Dock = DockStyle.Top;
            _directImageHistoryListView.Height = 220;
            _directImageHistoryListView.View = View.Details;
            _directImageHistoryListView.FullRowSelect = true;
            _directImageHistoryListView.HideSelection = false;
            _directImageHistoryListView.MultiSelect = false;
            _directImageHistoryListView.BorderStyle = BorderStyle.FixedSingle;
            _directImageHistoryListView.BackColor = Color.FromArgb(20, 24, 34);
            _directImageHistoryListView.ForeColor = Color.White;
            _directImageHistoryListView.Columns.Add("图片名称", 180);
            _directImageHistoryListView.Columns.Add("日期", 74);
            _directImageHistoryListView.DoubleClick += (_, _) => OpenSelectedDirectImageHistory();
            _directImageHistoryListView.SelectedIndexChanged += (_, _) =>
            {
                var hasSelection = _directImageHistoryListView.SelectedItems.Count > 0;
                _openDirectImageHistoryButton.Enabled = hasSelection;
                _deleteDirectImageHistoryButton.Enabled = hasSelection;
            };

            body.Controls.Add(CreateHintLabel("这里只显示当前项目“文生图”目录下已经落盘的图片。双击可放大查看，也可以删除。"));
            body.Controls.Add(_deleteDirectImageHistoryButton);
            body.Controls.Add(_openDirectImageHistoryButton);
            body.Controls.Add(_directImageHistoryListView);
            panel.Controls.Add(body);
            return panel;
        }

        private void EnsureDirectImageHistoryFolder()
        {
            if (_document.ProjectMode != ProjectWorkspaceMode.DirectStudio)
            {
                return;
            }

            if (_document.Nodes.Any(node => string.Equals(node.Type, WorkflowNodeCatalog.TextToImage, StringComparison.Ordinal)))
            {
                DirectStudioImageLibraryService.EnsureTodayDirectory(_document.ProjectName, WorkflowNodeCatalog.TextToImage);
            }
        }

        private void RefreshDirectImageHistory()
        {
            if (_directImageHistoryListView.IsDisposed)
            {
                return;
            }

            EnsureDirectImageHistoryFolder();

            _directImageHistoryListView.BeginUpdate();
            _directImageHistoryListView.Items.Clear();
            foreach (var item in DirectStudioImageLibraryService.LoadHistory(_document.ProjectName))
            {
                var viewItem = new ListViewItem(new[]
                {
                    item.FileName,
                    item.CreatedAt.ToString("MM-dd"),
                })
                {
                    Tag = item,
                };
                _directImageHistoryListView.Items.Add(viewItem);
            }

            _directImageHistoryListView.EndUpdate();
            var hasSelection = _directImageHistoryListView.SelectedItems.Count > 0;
            _openDirectImageHistoryButton.Enabled = hasSelection;
            _deleteDirectImageHistoryButton.Enabled = hasSelection;
        }

        private void OpenSelectedDirectImageHistory()
        {
            if (_directImageHistoryListView.SelectedItems.Count == 0 ||
                _directImageHistoryListView.SelectedItems[0].Tag is not DirectImageHistoryItem selected)
            {
                return;
            }

            var history = DirectStudioImageLibraryService.LoadHistory(_document.ProjectName).ToList();
            var startIndex = history.FindIndex(item =>
                string.Equals(item.FullPath, selected.FullPath, StringComparison.OrdinalIgnoreCase));
            using var preview = new ImageGalleryForm(
                history.Select(item => item.FullPath),
                Math.Max(0, startIndex),
                "图片预览");
            preview.ShowDialog(this);
        }

        private void DeleteSelectedDirectImageHistory()
        {
            if (_directImageHistoryListView.SelectedItems.Count == 0 ||
                _directImageHistoryListView.SelectedItems[0].Tag is not DirectImageHistoryItem selected)
            {
                return;
            }

            var confirm = MessageBox.Show(
                this,
                $"确认删除图片“{selected.FileName}”吗？",
                "删除图片",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            DirectStudioImageLibraryService.DeleteImage(selected.FullPath);
            RefreshDirectImageHistory();
            UpdateStatus($"已删除图片：{selected.FileName}", Color.DarkOrange);
        }

        private void ArchiveDirectStudioArtifacts(WorkflowNode node)
        {
            if (node == null || !WorkflowNodeCatalog.IsDirectStudioNodeType(node.Type))
            {
                return;
            }

            var archivedPath = DirectStudioImageLibraryService.ArchiveGeneratedArtifact(node.ArtifactPath, _document.ProjectName, node.Type);
            if (!string.IsNullOrWhiteSpace(archivedPath))
            {
                node.ArtifactPath = archivedPath;
            }

            if (string.Equals(node.Type, WorkflowNodeCatalog.TextToImage, StringComparison.Ordinal))
            {
                RefreshDirectImageHistory();
            }
        }
    }
}
