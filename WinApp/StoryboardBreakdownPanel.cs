using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class StoryboardBreakdownPanel : UserControl
    {
        private WorkflowDocument? _document;
        private WorkflowNode? _node;
        private bool _busy;

        public StoryboardBreakdownPanel()
        {
            BackColor = Color.FromArgb(30, 30, 30);
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        public event EventHandler? SplitRequested;
        public event EventHandler? EntryChanged;
        public event EventHandler? InteractionStarted;
        public event EventHandler<string>? ShotActionRequested;

        public void Bind(WorkflowDocument? document, WorkflowNode node, bool busy)
        {
            _document = document;
            _node = node;
            _busy = busy;
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);
            EnsureSelectedSources();
            Rebuild();
        }

        private void EnsureSelectedSources()
        {
            if (_node?.Params == null)
            {
                return;
            }

            var upstreamIds = GetConnectedStoryboardNodes()
                .Select(node => node.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _node.Params.SelectedStoryboardSourceNodeIds = (_node.Params.SelectedStoryboardSourceNodeIds ?? new List<string>())
                .Where(id => upstreamIds.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (_node.Params.SelectedStoryboardSourceNodeIds.Count == 0 && upstreamIds.Count > 0)
            {
                _node.Params.SelectedStoryboardSourceNodeIds = upstreamIds.ToList();
            }
        }

        private void Rebuild()
        {
            Controls.Clear();
            if (_node?.Params == null)
            {
                return;
            }

            Controls.Add((_node.Params.StoryboardShots?.Count ?? 0) > 0 ? BuildShotsView() : BuildSourceView());
        }

        private bool ShowSplitImagePreview =>
            _node != null &&
            _node.Type == WorkflowNodeCatalog.StoryboardBreakdown &&
            (_node.Params?.StoryboardShots?.Any(shot =>
                !string.IsNullOrWhiteSpace(shot.SplitImagePath) &&
                File.Exists(shot.SplitImagePath)) ?? false);

        private Control BuildSourceView()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(16, 12, 16, 14),
                BackColor = BackColor,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 238F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));

            var preview = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 21, 26),
                Margin = new Padding(0, 0, 0, 12),
            };
            preview.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(108, 144, 220),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = _busy
                    ? "正在拆分分镜图..."
                    : "等待拆分分镜图...\r\n\r\n连接“分镜图片”节点后点击“开始拆分”",
            });
            root.Controls.Add(preview, 0, 0);

            var sourceShell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.FromArgb(34, 35, 40),
                Padding = new Padding(12),
                Margin = Padding.Empty,
            };
            sourceShell.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            sourceShell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));

            var sources = GetConnectedStoryboardNodes();
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(126, 184, 255),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = $"已连接的分镜图片节点 ({sources.Count})",
            }, 0, 0);

            var toggleAll = new LinkLabel
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                LinkColor = Color.FromArgb(114, 168, 255),
                ActiveLinkColor = Color.FromArgb(164, 206, 255),
                VisitedLinkColor = Color.FromArgb(114, 168, 255),
                Text = AreAllSourcesSelected() ? "取消全选" : "全选",
            };
            toggleAll.LinkClicked += (_, _) =>
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                _node!.Params!.SelectedStoryboardSourceNodeIds = AreAllSourcesSelected()
                    ? new List<string>()
                    : sources.Select(node => node.Id).ToList();
                EntryChanged?.Invoke(this, EventArgs.Empty);
                Rebuild();
            };
            header.Controls.Add(toggleAll, 1, 0);
            sourceShell.Controls.Add(header, 0, 0);

            if (sources.Count == 0)
            {
                sourceShell.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    ForeColor = Color.FromArgb(132, 142, 162),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = "未检测到已连接的分镜图片节点。\r\n请先把“分镜图片”连接到当前节点。",
                }, 0, 1);
            }
            else
            {
                var list = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    WrapContents = false,
                    FlowDirection = FlowDirection.TopDown,
                    Margin = new Padding(0, 8, 0, 0),
                    BackColor = Color.FromArgb(34, 35, 40),
                };

                foreach (var source in sources)
                {
                    list.Controls.Add(BuildSourceCard(source, list));
                }

                sourceShell.Controls.Add(list, 0, 1);
            }

            root.Controls.Add(sourceShell, 0, 1);
            root.Controls.Add(BuildSplitButton(false), 0, 2);
            return root;
        }

        private Control BuildShotsView()
        {
            var shots = _node!.Params!.StoryboardShots ?? new List<StoryboardShot>();
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(16, 12, 16, 14),
                BackColor = BackColor,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(142, 156, 180),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = _busy
                    ? "正在刷新分镜镜头..."
                    : $"已拆分 {shots.Count} 个分镜镜头，点击“重新获取”可单独回收图片，点击“编辑”可逐条调整。",
            }, 0, 0);

            var list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                Margin = new Padding(0, 8, 0, 8),
                BackColor = BackColor,
            };

            foreach (var shot in shots.Select((value, index) => (value, index)))
            {
                var shouldShowPreview = ShowSplitImagePreview &&
                    !string.IsNullOrWhiteSpace(shot.value.SplitImagePath) &&
                    File.Exists(shot.value.SplitImagePath);

                list.Controls.Add(shouldShowPreview
                    ? BuildVerticalShotCard(shot.value, shot.index, list)
                    : BuildCompactShotCard(shot.value, shot.index, list));
            }

            root.Controls.Add(list, 0, 1);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.FromArgb(34, 35, 40),
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
            footer.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(154, 164, 182),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = $"总分镜: {shots.Count}    总时长: {shots.Sum(shot => Math.Max(1, shot.DurationSeconds))}秒",
            }, 0, 0);
            footer.Controls.Add(BuildSplitButton(true), 1, 0);
            root.Controls.Add(footer, 0, 2);
            return root;
        }

        private Control BuildCompactShotCard(StoryboardShot shot, int index, FlowLayoutPanel host)
        {
            var shell = new Panel
            {
                Width = Math.Max(320, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12),
                Height = 154,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(12, 10, 12, 10),
                BackColor = Color.FromArgb(24, 25, 30),
                Cursor = Cursors.Hand,
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));

            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = (index + 1).ToString(),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(70, 76, 196),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(10, 0, 8, 0),
                AutoEllipsis = true,
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = shot.DisplayTitle,
            }, 1, 0);
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(154, 164, 182),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = $"{Math.Max(1, shot.DurationSeconds)}秒",
            }, 2, 0);

            var refetchButton = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(66, 104, 182),
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point),
                Text = "重新获取",
                Cursor = _busy ? Cursors.Default : Cursors.Hand,
                Enabled = !_busy,
            };
            refetchButton.FlatAppearance.BorderSize = 0;
            refetchButton.Click += (_, _) => RequestShotAction(shot, "refetch-shot");
            header.Controls.Add(refetchButton, 3, 0);

            var editButton = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(74, 82, 104),
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                Text = "编辑",
                Cursor = Cursors.Hand,
            };
            editButton.FlatAppearance.BorderSize = 0;
            editButton.Click += (_, _) => OpenEditor(shot);
            header.Controls.Add(editButton, 4, 0);

            var descriptionShell = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 19, 24),
                Padding = new Padding(12, 8, 12, 8),
                Margin = new Padding(0, 8, 0, 8),
            };
            descriptionShell.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(212, 218, 228),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                Text = shot.VisualPreview,
            });

            var tagRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoScroll = false,
            };
            tagRow.Controls.Add(CreateTagLabel(shot.ShotSize, Color.FromArgb(58, 92, 152)));
            tagRow.Controls.Add(CreateTagLabel(shot.CameraAngle, Color.FromArgb(84, 96, 164)));
            tagRow.Controls.Add(CreateTagLabel(shot.CameraMovement, Color.FromArgb(80, 114, 84)));
            if (!string.IsNullOrWhiteSpace(shot.CharactersDisplay) && !string.Equals(shot.CharactersDisplay, "无角色出镜", StringComparison.Ordinal))
            {
                tagRow.Controls.Add(CreateTagLabel(shot.CharactersDisplay, Color.FromArgb(118, 80, 44)));
            }

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(descriptionShell, 0, 1);
            root.Controls.Add(tagRow, 0, 2);
            shell.Controls.Add(root);

            shell.Click += (_, _) => OpenEditor(shot);
            foreach (Control child in root.Controls)
            {
                child.Click += (_, _) => OpenEditor(shot);
            }

            return shell;
        }

        private Control BuildSourceCard(WorkflowNode sourceNode, FlowLayoutPanel host)
        {
            var pagePaths = GetSourcePagePaths(sourceNode);
            var shell = new Panel
            {
                Width = Math.Max(320, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12),
                Height = pagePaths.Count > 0 ? 132 : 90,
                Margin = new Padding(0, 0, 0, 10),
                Padding = new Padding(12, 10, 12, 10),
                BackColor = IsSourceSelected(sourceNode.Id) ? Color.FromArgb(46, 60, 94) : Color.FromArgb(24, 25, 30),
            };

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 1,
                Height = 26,
                Margin = Padding.Empty,
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28F));
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.WhiteSmoke,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = $"{sourceNode.Id} · {pagePaths.Count} 页 · {(string.Equals(sourceNode.Params?.StoryboardGridLayout, "2x3", StringComparison.Ordinal) ? "六宫格" : "九宫格")}",
            }, 0, 0);

            var checkBox = new CheckBox
            {
                Dock = DockStyle.Fill,
                Checked = IsSourceSelected(sourceNode.Id),
                Margin = Padding.Empty,
            };
            checkBox.CheckedChanged += (_, _) =>
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                SetSourceSelected(sourceNode.Id, checkBox.Checked);
                EntryChanged?.Invoke(this, EventArgs.Empty);
                Rebuild();
            };
            header.Controls.Add(checkBox, 1, 0);
            shell.Controls.Add(header);

            if (pagePaths.Count > 0)
            {
                var thumbs = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 56,
                    WrapContents = false,
                    FlowDirection = FlowDirection.LeftToRight,
                    BackColor = Color.Transparent,
                };

                for (var index = 0; index < Math.Min(4, pagePaths.Count); index++)
                {
                    var picture = new PictureBox
                    {
                        Width = 72,
                        Height = 48,
                        Margin = new Padding(0, 0, 8, 0),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        BackColor = Color.FromArgb(16, 17, 22),
                        Cursor = Cursors.Hand,
                        Image = LoadImageCopy(pagePaths[index]),
                    };
                    picture.Disposed += (_, _) => picture.Image?.Dispose();
                    var pageIndex = index;
                    picture.Click += (_, _) => OpenGallery(pagePaths, pageIndex, $"{sourceNode.Id} - 分镜页");
                    thumbs.Controls.Add(picture);
                }

                shell.Controls.Add(thumbs);
            }

            return shell;
        }

        private Control BuildVerticalShotCard(StoryboardShot shot, int index, FlowLayoutPanel host)
        {
            var shell = new Panel
            {
                Width = Math.Max(320, host.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12),
                Height = 348,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(12),
                BackColor = Color.FromArgb(24, 25, 30),
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 156F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var previewShell = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 0, 0, 10),
                BackColor = Color.Transparent,
            };
            if (!string.IsNullOrWhiteSpace(shot.SplitImagePath) && File.Exists(shot.SplitImagePath))
            {
                var picture = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(16, 17, 22),
                    Cursor = Cursors.Hand,
                    Image = LoadImageCopy(shot.SplitImagePath),
                };
                picture.Disposed += (_, _) => picture.Image?.Dispose();
                picture.Click += (_, _) => OpenGallery(
                    (_node?.Params?.StoryboardShots ?? new List<StoryboardShot>())
                        .Select(item => item.SplitImagePath)
                        .Where(File.Exists)
                        .ToList(),
                    index,
                    $"{_node?.Id} - 拆解分镜图");
                previewShell.Controls.Add(picture);
            }
            else
            {
                previewShell.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.FromArgb(16, 17, 22),
                    ForeColor = Color.FromArgb(118, 128, 146),
                    Text = "暂无拆解图预览",
                });
            }
            root.Controls.Add(previewShell, 0, 0);

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = Padding.Empty,
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(58, 104, 180),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = Math.Max(1, shot.ShotNumber).ToString(),
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(10, 0, 8, 0),
                ForeColor = Color.WhiteSmoke,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = $"分镜 {Math.Max(1, shot.ShotNumber)}",
            }, 1, 0);

            var refetch = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(66, 104, 182),
                ForeColor = Color.WhiteSmoke,
                Text = "重新获取",
                Cursor = _busy ? Cursors.Default : Cursors.Hand,
                Enabled = !_busy,
            };
            refetch.FlatAppearance.BorderSize = 0;
            refetch.Click += (_, _) => RequestShotAction(shot, "refetch-shot");
            header.Controls.Add(refetch, 2, 0);

            var edit = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(74, 82, 104),
                ForeColor = Color.WhiteSmoke,
                Text = "编辑",
                Cursor = Cursors.Hand,
            };
            edit.FlatAppearance.BorderSize = 0;
            edit.Click += (_, _) => OpenEditor(shot);
            header.Controls.Add(edit, 3, 0);
            root.Controls.Add(header, 0, 1);

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = $"场景: {ValueOrDefault(shot.Scene)}",
            }, 0, 2);

            var visualShell = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 19, 24),
                Padding = new Padding(10, 8, 10, 8),
                Margin = new Padding(0, 2, 0, 6),
            };
            visualShell.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                Text = $"画面: {ValueOrDefault(shot.VisualDescription)}",
            });
            root.Controls.Add(visualShell, 0, 3);

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = $"对白: {ValueOrDefault(shot.Dialogue)}",
            }, 0, 4);

            var meta = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Margin = Padding.Empty,
            };
            meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            meta.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            meta.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            meta.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(196, 204, 220), Text = $"景别: {ValueOrDefault(shot.ShotSize)}" }, 0, 0);
            meta.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(196, 204, 220), Text = $"拍摄角度: {ValueOrDefault(shot.CameraAngle)}" }, 1, 0);
            meta.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(196, 204, 220), Text = $"运镜方式: {ValueOrDefault(shot.CameraMovement)}" }, 0, 1);
            meta.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(196, 204, 220), Text = $"时长: {Math.Max(1, shot.DurationSeconds)}秒" }, 1, 1);
            root.Controls.Add(meta, 0, 5);

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(154, 164, 182),
                TextAlign = ContentAlignment.TopLeft,
                AutoEllipsis = true,
                Text = BuildShotFooter(shot),
            }, 0, 6);

            shell.Controls.Add(root);
            return shell;
        }

        private Button BuildSplitButton(bool hasShots)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 8, 8, 8),
                FlatStyle = FlatStyle.Flat,
                BackColor = _busy ? Color.FromArgb(70, 108, 180) : Color.FromArgb(44, 150, 255),
                ForeColor = Color.White,
                Text = _busy ? "正在拆分..." : hasShots ? "重新拆分" : "开始拆分",
                Cursor = _busy ? Cursors.Default : Cursors.Hand,
                Enabled = !_busy && GetSelectedSourceIds().Count > 0,
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, _) =>
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                SplitRequested?.Invoke(this, EventArgs.Empty);
            };
            return button;
        }

        private void OpenEditor(StoryboardShot shot)
        {
            if (_node?.Params?.StoryboardShots == null)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            using var form = new StoryboardShotEditForm(shot);
            if (form.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }

            var index = _node.Params.StoryboardShots.FindIndex(candidate => string.Equals(candidate.Id, shot.Id, StringComparison.Ordinal));
            if (index < 0)
            {
                return;
            }

            _node.Params.StoryboardShots[index] = form.Result;
            _node.Output = WorkflowExecutor.BuildStoryboardBreakdownOutput(_node);
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void RequestShotAction(StoryboardShot shot, string action)
        {
            if (_busy || _node == null || string.IsNullOrWhiteSpace(shot?.Id))
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            ShotActionRequested?.Invoke(this, $"storyboard-breakdown.{action}:{shot.Id}");
        }

        private List<WorkflowNode> GetConnectedStoryboardNodes()
        {
            return _document == null || _node == null
                ? new List<WorkflowNode>()
                : WorkflowExecutor.CollectUpstreamNodes(_document, _node)
                    .Where(node => node.Type == WorkflowNodeCatalog.StoryboardImage)
                    .ToList();
        }

        private List<string> GetSelectedSourceIds()
        {
            return (_node?.Params?.SelectedStoryboardSourceNodeIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool IsSourceSelected(string nodeId) => GetSelectedSourceIds().Contains(nodeId, StringComparer.OrdinalIgnoreCase);

        private bool AreAllSourcesSelected()
        {
            var sources = GetConnectedStoryboardNodes().Select(node => node.Id).ToList();
            var selected = GetSelectedSourceIds();
            return sources.Count > 0 && sources.All(id => selected.Contains(id, StringComparer.OrdinalIgnoreCase));
        }

        private void SetSourceSelected(string nodeId, bool selected)
        {
            if (_node?.Params == null)
            {
                return;
            }

            var sourceIds = GetSelectedSourceIds();
            if (selected)
            {
                if (!sourceIds.Contains(nodeId, StringComparer.OrdinalIgnoreCase))
                {
                    sourceIds.Add(nodeId);
                }
            }
            else
            {
                sourceIds.RemoveAll(id => string.Equals(id, nodeId, StringComparison.OrdinalIgnoreCase));
            }

            _node.Params.SelectedStoryboardSourceNodeIds = sourceIds;
        }

        private static List<string> GetSourcePagePaths(WorkflowNode node)
        {
            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);
            var paths = (node.Params.StoryboardGridPagePaths ?? new List<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .ToList();

            if (paths.Count == 0 && !string.IsNullOrWhiteSpace(node.ArtifactPath) && File.Exists(node.ArtifactPath))
            {
                paths.Add(node.ArtifactPath);
            }

            return paths;
        }

        private static void OpenGallery(IReadOnlyList<string> paths, int index, string title)
        {
            if (paths == null || paths.Count == 0)
            {
                return;
            }

            using var form = new ImageGalleryForm(paths.ToList(), Math.Max(0, Math.Min(index, paths.Count - 1)), title);
            form.ShowDialog();
        }

        private static Image? LoadImageCopy(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var image = Image.FromStream(stream);
                return new Bitmap(image);
            }
            catch
            {
                return null;
            }
        }

        private static string ValueOrDefault(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "无" : value.Trim();
        }

        private static string BuildShotFooter(StoryboardShot shot)
        {
            var pieces = new List<string>();
            if (!string.IsNullOrWhiteSpace(shot.CharactersDisplay) && !string.Equals(shot.CharactersDisplay, "无角色出镜", StringComparison.Ordinal))
            {
                pieces.Add($"角色: {shot.CharactersDisplay}");
            }

            if (!string.IsNullOrWhiteSpace(shot.VisualEffects) && !string.Equals(shot.VisualEffects, "无", StringComparison.OrdinalIgnoreCase))
            {
                pieces.Add($"视觉特效: {shot.VisualEffects.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(shot.AudioEffects) && !string.Equals(shot.AudioEffects, "无", StringComparison.OrdinalIgnoreCase))
            {
                pieces.Add($"音效: {shot.AudioEffects.Trim()}");
            }

            return pieces.Count == 0 ? "无额外说明" : string.Join("    ", pieces);
        }

        private static Control CreateTagLabel(string text, Color backColor)
        {
            return new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 0, 8, 0),
                Padding = new Padding(8, 4, 8, 4),
                BackColor = backColor,
                ForeColor = Color.WhiteSmoke,
                Text = string.IsNullOrWhiteSpace(text) ? "无" : text,
            };
        }
    }
}
