using System;
using System.Drawing;

namespace JSAI.WinApp
{
    public partial class MainForm
    {
        private bool IsDirectStudioMode => _document.ProjectMode == ProjectWorkspaceMode.DirectStudio;

        private void RefreshWorkspaceForCurrentMode()
        {
            RefreshWorkspaceModeUi();
            RefreshStats();
            RefreshAssets();
            RefreshConnections();
            RefreshDirectImageHistory();

            if (_selectedNode != null &&
                !_document.Nodes.Exists(node => string.Equals(node.Id, _selectedNode.Id, StringComparison.Ordinal)))
            {
                SelectNode(null);
            }
            else
            {
                SelectNode(_selectedNode);
            }
        }

        private void RefreshWorkspaceModeUi()
        {
            if (_nodeLibraryTitleLabel != null)
            {
                _nodeLibraryTitleLabel.Text = IsDirectStudioMode ? "模组库" : "节点库";
            }

            RebuildNodeLibraryButtons();

            if (_directHistoryCard != null)
            {
                _directHistoryCard.Visible = IsDirectStudioMode;
            }

            if (_assetCard != null)
            {
                _assetCard.Visible = !IsDirectStudioMode;
            }

            if (_connectionCard != null)
            {
                _connectionCard.Visible = true;
            }

            if (_hintCard != null)
            {
                _hintCard.Visible = !IsDirectStudioMode;
            }

            _runWorkflowButton.Visible = !IsDirectStudioMode;
            _runWorkflowButton.Enabled = !IsDirectStudioMode && !_running;

            _inspectorActionCard.Visible = !IsDirectStudioMode;
            _inspectorOutputCard.Visible = !IsDirectStudioMode;

            ConfigureToolbarRuntimeLayout();
            _toolbarActionsPanel.PerformLayout();
            _workspaceSplit.PerformLayout();

            UpdateStatus(
                IsDirectStudioMode ? "已进入直出模式工作区。" : "已进入 AI 漫剧项目工作区。",
                Color.FromArgb(90, 176, 255));
        }
    }
}
