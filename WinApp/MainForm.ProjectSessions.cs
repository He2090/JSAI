using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public partial class MainForm
    {
        private readonly Panel _projectTabsHost = new();
        private readonly FlowLayoutPanel _projectTabsPanel = new();
        private readonly Button _projectTabsNewButton = new();
        private readonly ToolTip _projectTabToolTip = new();
        private readonly List<ProjectSession> _projectSessions = new();
        private ProjectSession? _activeSession;
        private bool _switchingSession;

        private sealed class ProjectSession
        {
            public Guid Id { get; } = Guid.NewGuid();

            public WorkflowDocument Document { get; set; } = WorkflowDocument.CreateEmpty();

            public string ProjectFilePath { get; set; } = string.Empty;

            public bool IsDirty { get; set; }

            public string WorkspaceLabel { get; set; } = string.Empty;

            public PointF CanvasPan { get; set; } = PointF.Empty;

            public float CanvasZoom { get; set; } = 1F;

            public string DisplayName =>
                string.IsNullOrWhiteSpace(Document.ProjectName)
                    ? "新项目"
                    : Document.ProjectName.Trim();

            public string FullTitle => $"{WorkspaceLabel} · {DisplayName}";

            public string TabText => IsDirty ? $"{FullTitle} ●" : FullTitle;
        }

        private Control BuildProjectTabsBar()
        {
            _projectTabToolTip.AutoPopDelay = 8000;
            _projectTabToolTip.InitialDelay = 300;
            _projectTabToolTip.ReshowDelay = 120;

            _projectTabsHost.Dock = DockStyle.Fill;
            _projectTabsHost.Padding = new Padding(12, 4, 12, 4);
            _projectTabsHost.BackColor = Color.FromArgb(15, 18, 27);
            _projectTabsHost.Visible = false;

            _projectTabsPanel.Dock = DockStyle.Fill;
            _projectTabsPanel.WrapContents = false;
            _projectTabsPanel.AutoScroll = true;
            _projectTabsPanel.FlowDirection = FlowDirection.LeftToRight;
            _projectTabsPanel.BackColor = Color.Transparent;
            _projectTabsPanel.Margin = Padding.Empty;
            _projectTabsPanel.Padding = Padding.Empty;

            _projectTabsNewButton.Text = "+";
            _projectTabsNewButton.Width = 30;
            _projectTabsNewButton.Height = 26;
            _projectTabsNewButton.FlatStyle = FlatStyle.Flat;
            _projectTabsNewButton.FlatAppearance.BorderSize = 1;
            _projectTabsNewButton.FlatAppearance.BorderColor = Color.FromArgb(66, 80, 108);
            _projectTabsNewButton.BackColor = Color.FromArgb(27, 33, 47);
            _projectTabsNewButton.ForeColor = Color.White;
            _projectTabsNewButton.Margin = new Padding(8, 1, 0, 0);
            _projectTabsNewButton.Cursor = Cursors.Hand;
            _projectTabsNewButton.Click += (_, _) => ShowHomeScreen(force: true);
            _projectTabToolTip.SetToolTip(_projectTabsNewButton, "新建项目");

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.Controls.Add(_projectTabsPanel, 0, 0);
            layout.Controls.Add(_projectTabsNewButton, 1, 0);

            _projectTabsHost.Controls.Add(layout);
            return _projectTabsHost;
        }

        private void HookSessionManagement()
        {
            FormClosing += MainForm_FormClosingPromptSave;
        }

        private void MainForm_FormClosingPromptSave(object? sender, FormClosingEventArgs e)
        {
            foreach (var session in _projectSessions.Where(item => item.IsDirty).ToList())
            {
                if (!TryPromptSaveSession(session, "关闭软件"))
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        private ProjectSession AddProjectSession(
            WorkflowDocument document,
            string projectFilePath = "",
            bool isDirty = true,
            string? workspaceLabel = null)
        {
            var normalizedPath = string.IsNullOrWhiteSpace(projectFilePath)
                ? string.Empty
                : Path.GetFullPath(projectFilePath);

            if (!string.IsNullOrWhiteSpace(normalizedPath))
            {
                var existing = _projectSessions.FirstOrDefault(session =>
                    !string.IsNullOrWhiteSpace(session.ProjectFilePath) &&
                    string.Equals(Path.GetFullPath(session.ProjectFilePath), normalizedPath, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    ActivateProjectSession(existing);
                    return existing;
                }
            }

            var session = new ProjectSession
            {
                Document = document,
                ProjectFilePath = normalizedPath,
                IsDirty = isDirty,
                WorkspaceLabel = ResolveWorkspaceLabel(document, workspaceLabel),
            };
            _projectSessions.Add(session);
            RebuildProjectTabs();
            return session;
        }

        private void ActivateProjectSession(ProjectSession session)
        {
            if (_activeSession == session)
            {
                UpdateSessionWorkspaceLabel(session);
                RebuildProjectTabs();
                HideHomeScreen();
                return;
            }

            SyncActiveSessionSnapshot();

            _activeSession = session;
            UpdateSessionWorkspaceLabel(session);
            _currentProjectFilePath = session.ProjectFilePath;
            _document = session.Document;
            _switchingSession = true;
            try
            {
                _canvas.SetDocument(_document);
                _canvas.SetViewState(session.CanvasPan, session.CanvasZoom);
                _projectNameTextBox.Text = _document.ProjectName;
                SelectNode(null);
                RefreshWorkspaceForCurrentMode();
            }
            finally
            {
                _switchingSession = false;
            }

            RebuildProjectTabs();
            HideHomeScreen();
            UpdateStatus($"已切换到项目：{session.FullTitle}", Color.FromArgb(90, 176, 255));
        }

        private void SyncActiveSessionSnapshot()
        {
            if (_activeSession == null)
            {
                return;
            }

            _activeSession.Document = _document;
            _activeSession.ProjectFilePath = _currentProjectFilePath ?? string.Empty;
            _activeSession.CanvasPan = _canvas.PanOffset;
            _activeSession.CanvasZoom = _canvas.ZoomFactor;
            UpdateSessionWorkspaceLabel(_activeSession);
        }

        private void UpdateSessionWorkspaceLabel(ProjectSession session)
        {
            session.WorkspaceLabel = ResolveWorkspaceLabel(session.Document, session.WorkspaceLabel);
        }

        private void MarkActiveSessionDirty()
        {
            if (_activeSession == null)
            {
                return;
            }

            _activeSession.IsDirty = true;
            SyncActiveSessionSnapshot();
            RebuildProjectTabs();
        }

        private void MarkActiveSessionSaved()
        {
            if (_activeSession == null)
            {
                return;
            }

            _activeSession.IsDirty = false;
            SyncActiveSessionSnapshot();
            RebuildProjectTabs();
        }

        private void RebuildProjectTabs()
        {
            _projectTabsPanel.SuspendLayout();
            _projectTabsPanel.Controls.Clear();

            foreach (var session in _projectSessions)
            {
                _projectTabsPanel.Controls.Add(CreateProjectTab(session));
            }

            _projectTabsPanel.ResumeLayout();
            _projectTabsHost.Visible = _projectSessions.Count > 0;
        }

        private Control CreateProjectTab(ProjectSession session)
        {
            var active = ReferenceEquals(session, _activeSession);
            var titleFont = new Font("Microsoft YaHei UI", active ? 9.5F : 9F, active ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
            var titleText = session.TabText;
            var measuredWidth = TextRenderer.MeasureText(
                titleText,
                titleFont,
                new Size(int.MaxValue, 28),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            var tooltipText = session.IsDirty ? $"{session.FullTitle}（未保存）" : session.FullTitle;

            var tab = new Panel
            {
                Width = Math.Max(260, Math.Min(520, measuredWidth + 86)),
                Height = 28,
                Margin = new Padding(0, 0, 8, 0),
                Padding = new Padding(12, 0, 8, 0),
                BackColor = active ? Color.FromArgb(33, 40, 58) : Color.FromArgb(21, 25, 37),
                Cursor = Cursors.Hand,
                Tag = session,
            };

            var closeButton = new Button
            {
                Dock = DockStyle.Right,
                Width = 20,
                FlatStyle = FlatStyle.Flat,
                Text = "×",
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(170, 180, 205),
                Cursor = Cursors.Hand,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                TabStop = false,
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(64, 36, 36);
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(46, 28, 28);
            closeButton.Click += (_, _) => CloseProjectSession(session);

            var titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = titleFont,
                ForeColor = active ? Color.White : Color.FromArgb(192, 201, 220),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = titleText,
                Cursor = Cursors.Hand,
                Padding = new Padding(0, 0, 8, 0),
            };

            void Activate(object? _, EventArgs __) => ActivateProjectSession(session);

            tab.Click += Activate;
            titleLabel.Click += Activate;

            _projectTabToolTip.SetToolTip(tab, tooltipText);
            _projectTabToolTip.SetToolTip(titleLabel, tooltipText);
            _projectTabToolTip.SetToolTip(closeButton, $"关闭 {session.FullTitle}");

            tab.Controls.Add(titleLabel);
            tab.Controls.Add(closeButton);
            return tab;
        }

        private void CloseProjectSession(ProjectSession session)
        {
            if (_running)
            {
                return;
            }

            if (!TryPromptSaveSession(session, "关闭项目"))
            {
                return;
            }

            var activeIndex = _projectSessions.IndexOf(session);
            _projectSessions.Remove(session);

            if (ReferenceEquals(_activeSession, session))
            {
                if (_projectSessions.Count > 0)
                {
                    var nextIndex = Math.Min(activeIndex, _projectSessions.Count - 1);
                    ActivateProjectSession(_projectSessions[nextIndex]);
                }
                else
                {
                    _activeSession = null;
                    _currentProjectFilePath = string.Empty;
                    _document = WorkflowDocument.CreateEmpty();
                    _switchingSession = true;
                    try
                    {
                        _canvas.SetDocument(_document);
                        _projectNameTextBox.Text = _document.ProjectName;
                        SelectNode(null);
                        RefreshWorkspaceForCurrentMode();
                    }
                    finally
                    {
                        _switchingSession = false;
                    }

                    ShowHomeScreen(force: true);
                }
            }

            RebuildProjectTabs();
        }

        private bool TryPromptSaveSession(ProjectSession session, string actionName)
        {
            if (!session.IsDirty)
            {
                return true;
            }

            var previousSession = _activeSession;
            ActivateProjectSession(session);

            var result = MessageBox.Show(
                this,
                $"项目“{session.FullTitle}”尚未保存。\n\n是否先保存再{actionName}？",
                "未保存的项目",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (result == DialogResult.Cancel)
            {
                if (previousSession != null && !ReferenceEquals(previousSession, session))
                {
                    ActivateProjectSession(previousSession);
                }

                return false;
            }

            if (result == DialogResult.Yes && !SaveProjectFile(showSuccessMessage: false))
            {
                if (previousSession != null && !ReferenceEquals(previousSession, session))
                {
                    ActivateProjectSession(previousSession);
                }

                return false;
            }

            return true;
        }

        private static string ResolveWorkspaceLabel(WorkflowDocument document, string? workspaceLabel)
        {
            return string.IsNullOrWhiteSpace(workspaceLabel)
                ? InferWorkspaceLabel(document)
                : workspaceLabel.Trim();
        }

        private static string GetWorkspaceLabel(ProjectWorkspaceMode mode, string? initialNodeType = null)
        {
            if (mode != ProjectWorkspaceMode.DirectStudio)
            {
                return "AI漫剧";
            }

            var normalizedType = WorkflowNodeCatalog.NormalizeNodeType(initialNodeType ?? string.Empty);
            return normalizedType switch
            {
                var type when string.Equals(type, WorkflowNodeCatalog.TextToImage, StringComparison.Ordinal) => "文生图",
                var type when string.Equals(type, WorkflowNodeCatalog.TextToVideo, StringComparison.Ordinal) => "文生视频",
                var type when string.Equals(type, WorkflowNodeCatalog.TextImageToVideo, StringComparison.Ordinal) => "文图生视频",
                _ => "直出项目",
            };
        }

        private static string InferWorkspaceLabel(WorkflowDocument document)
        {
            if (document.ProjectMode != ProjectWorkspaceMode.DirectStudio)
            {
                return "AI漫剧";
            }

            var directType = document.Nodes
                .Select(node => WorkflowNodeCatalog.NormalizeNodeType(node.Type))
                .FirstOrDefault(type => WorkflowNodeCatalog.IsDirectStudioNodeType(type));

            return directType switch
            {
                var type when string.Equals(type, WorkflowNodeCatalog.TextToImage, StringComparison.Ordinal) => "文生图",
                var type when string.Equals(type, WorkflowNodeCatalog.TextToVideo, StringComparison.Ordinal) => "文生视频",
                var type when string.Equals(type, WorkflowNodeCatalog.TextImageToVideo, StringComparison.Ordinal) => "文图生视频",
                _ => "直出项目",
            };
        }
    }
}
