using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace JSAI.WinApp
{
    public sealed class VideoCollectionPanel : UserControl
    {
        private static readonly string[] TransitionValues = { "none", "fade", "black", "flash" };
        private static readonly string[] TransitionLabels = { "无", "淡入淡出", "黑场切换", "闪白切换" };

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int mciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

        private WorkflowDocument? _document;
        private WorkflowNode? _node;
        private bool _busy;
        private string _pendingDragPath = string.Empty;
        private Point _pendingDragScreenPoint;
        private bool _suppressNextClick;

        private Panel? _previewHost;
        private Label? _previewStatusLabel;
        private string _previewAlias = string.Empty;
        private bool _previewOpened;
        private ElementHost? _inlineMediaHost;
        private System.Windows.Controls.MediaElement? _inlineMediaElement;
        private readonly List<Control> _previewOverlayControls = new();

        public VideoCollectionPanel()
        {
            BackColor = Color.FromArgb(30, 30, 30);
            AutoScaleMode = AutoScaleMode.None;
            Disposed += (_, _) => CloseInlinePreview();
        }

        public event EventHandler? EntryChanged;
        public event EventHandler? InteractionStarted;
        public event EventHandler<string>? ActionRequested;

        public void Bind(WorkflowDocument? document, WorkflowNode node, bool busy)
        {
            _document = document;
            _node = node;
            _busy = busy;

            node.Params ??= new WorkflowNodeParameters();
            node.Params.EnsureDefaults(node.Type);
            if (document != null)
            {
                VideoCollectionSupport.GetSelectedSources(document, node);
            }

            Rebuild();
        }

        private void Rebuild()
        {
            CloseInlinePreview();
            SuspendLayout();
            Controls.Clear();

            if (_document == null || _node?.Params == null)
            {
                ResumeLayout();
                return;
            }

            var allSources = VideoCollectionSupport.CollectSources(_document, _node);
            var timelineSources = VideoCollectionSupport.GetTimelineSources(_document, _node);
            var currentSource = ResolveCurrentSource(timelineSources.Count > 0 ? timelineSources : allSources);
            var previewPath = GetPreviewPath(currentSource);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(14, 10, 14, 14),
                BackColor = BackColor,
                Margin = Padding.Empty,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 330F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 154F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));

            root.Controls.Add(BuildHeaderRow(allSources.Count, timelineSources.Count), 0, 0);
            root.Controls.Add(BuildMainSection(allSources, timelineSources, currentSource, previewPath), 0, 1);
            root.Controls.Add(BuildTimelineSection(timelineSources), 0, 2);
            root.Controls.Add(BuildEditOptionsSection(), 0, 3);
            root.Controls.Add(BuildFooter(timelineSources.Count), 0, 4);

            Controls.Add(root);
            ResumeLayout(true);

            if (!string.IsNullOrWhiteSpace(previewPath) && File.Exists(previewPath))
            {
                BeginInvoke(new Action(() => OpenInlinePreview(previewPath)));
            }
        }

        private Control BuildHeaderRow(int sourceCount, int timelineCount)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));

            row.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = $"素材 {sourceCount} 个 | 时间线 {timelineCount} 段 | 预览 / 剪辑 / 合集",
            }, 0, 0);

            var statusText = _busy
                ? "正在生成合集..."
                : !string.IsNullOrWhiteSpace(_node?.ArtifactPath) && File.Exists(_node.ArtifactPath)
                    ? "合集已生成"
                    : "等待剪辑";

            row.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = _busy ? Color.FromArgb(255, 190, 96) : Color.FromArgb(196, 164, 255),
                TextAlign = ContentAlignment.MiddleRight,
                Text = statusText,
            }, 1, 0);

            return row;
        }

        private Control BuildMainSection(
            IReadOnlyList<VideoCollectionSourceItem> allSources,
            IReadOnlyList<VideoCollectionSourceItem> timelineSources,
            VideoCollectionSourceItem? currentSource,
            string previewPath)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 6, 0, 8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));

            layout.Controls.Add(BuildPreviewSection(currentSource, previewPath), 0, 0);
            layout.Controls.Add(BuildClipListSection(allSources, timelineSources, currentSource), 1, 0);
            return layout;
        }

        private Control BuildPreviewSection(VideoCollectionSourceItem? currentSource, string previewPath)
        {
            var shell = CreatePanelShell(new Padding(12));
            shell.Margin = new Padding(0, 0, 10, 0);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = Padding.Empty,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

            var hasPreview = !string.IsNullOrWhiteSpace(previewPath);
            _previewHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = hasPreview ? Color.Black : Color.FromArgb(24, 26, 34),
                Margin = Padding.Empty,
            };
            _previewHost.Resize += (_, _) => ResizeInlinePreview();

            _previewStatusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(154, 162, 184),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = hasPreview ? "正在加载预览..." : "空剪辑",
            };
            if (hasPreview)
            {
                _previewHost.Controls.Add(_previewStatusLabel);
            }
            else
            {
                _previewHost.Controls.Add(BuildEmptyPreviewCanvas());
            }
            RefreshPreviewOverlays();
            layout.Controls.Add(_previewHost, 0, 0);

            var info = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = currentSource == null
                    ? "当前没有可预览片段"
                    : $"{currentSource.DisplayName} | {currentSource.DurationLabel} | {Path.GetFileName(currentSource.ArtifactPath)}",
            };
            layout.Controls.Add(info, 0, 1);

            layout.Controls.Add(BuildPreviewActions(previewPath), 0, 2);
            shell.Controls.Add(layout);
            return shell;
        }

        private static Control BuildEmptyPreviewCanvas()
        {
            var canvas = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(24, 26, 34),
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(18),
                Margin = Padding.Empty,
            };
            canvas.RowStyles.Add(new RowStyle(SizeType.Percent, 34F));
            canvas.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            canvas.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            canvas.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            canvas.RowStyles.Add(new RowStyle(SizeType.Percent, 66F));

            var title = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                ForeColor = Color.FromArgb(214, 220, 232),
                TextAlign = ContentAlignment.BottomCenter,
                Text = "空剪辑",
                Font = new Font(FontFamily.GenericSansSerif, 13F, FontStyle.Bold),
            };
            canvas.Controls.Add(title, 0, 1);

            var timecode = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                ForeColor = Color.FromArgb(126, 139, 170),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "00:00:00 / 00:00:00",
            };
            canvas.Controls.Add(timecode, 0, 2);

            var rails = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(60, 6, 60, 0),
                BackColor = Color.Transparent,
            };
            rails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            rails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            rails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            for (int index = 0; index < 3; index++)
            {
                rails.Controls.Add(new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(index == 0 ? 0 : 4, 0, index == 2 ? 0 : 4, 0),
                    BackColor = Color.FromArgb(45, 51, 67),
                }, index, 0);
            }

            canvas.Controls.Add(rails, 0, 3);
            return canvas;
        }

        private Control BuildPreviewActions(string previewPath)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                Margin = Padding.Empty,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));

            var hasPreview = !_busy && !string.IsNullOrWhiteSpace(previewPath) && File.Exists(previewPath);

            var playButton = CreateActionButton("播放", Color.FromArgb(74, 161, 255));
            playButton.Enabled = hasPreview;
            playButton.Click += (_, _) => PlayInlinePreview(previewPath);
            row.Controls.Add(playButton, 0, 0);

            var pauseButton = CreateActionButton("暂停", Color.FromArgb(58, 64, 78));
            pauseButton.Enabled = hasPreview;
            pauseButton.Click += (_, _) => SendPreviewCommand("pause");
            row.Controls.Add(pauseButton, 1, 0);

            var stopButton = CreateActionButton("停止", Color.FromArgb(58, 64, 78));
            stopButton.Enabled = hasPreview;
            stopButton.Click += (_, _) =>
            {
                SendPreviewCommand("stop");
                SendPreviewCommand("seek", "to start");
            };
            row.Controls.Add(stopButton, 2, 0);

            var fullButton = CreateActionButton("放大", Color.FromArgb(96, 126, 255));
            fullButton.Enabled = hasPreview;
            fullButton.Click += (_, _) => OpenVideo(previewPath);
            row.Controls.Add(fullButton, 3, 0);

            var folderButton = CreateActionButton("目录", Color.FromArgb(58, 64, 78));
            folderButton.Enabled = hasPreview;
            folderButton.Click += (_, _) => OpenFolder(previewPath);
            row.Controls.Add(folderButton, 4, 0);

            return row;
        }

        private Control BuildClipListSection(
            IReadOnlyList<VideoCollectionSourceItem> allSources,
            IReadOnlyList<VideoCollectionSourceItem> timelineSources,
            VideoCollectionSourceItem? currentSource)
        {
            var shell = CreatePanelShell(new Padding(12));

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = Padding.Empty,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "素材片段（勾选入轨，拖拽排序）",
            }, 0, 0);
            layout.Controls.Add(BuildMaterialActions(), 0, 1);

            var host = CreateScrollableHost();
            host.AllowDrop = true;
            host.DragEnter += (_, e) => SetTimelineDragEffect(e);
            host.DragOver += (_, e) => SetTimelineDragEffect(e);
            host.DragDrop += (_, e) => DropTimelineClipToEnd(e);

            if (allSources.Count == 0)
            {
                host.Controls.Add(new Label
                {
                    Dock = DockStyle.Top,
                    Height = 170,
                    ForeColor = Color.FromArgb(110, 118, 138),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = "等待视频素材...\r\n可导入本地视频，或连接“分镜视频”“视频预览”节点。",
                });
            }
            else
            {
                var timelinePaths = timelineSources
                    .Select((source, index) => new { source.ArtifactPath, Index = index })
                    .ToDictionary(item => item.ArtifactPath, item => item.Index, StringComparer.OrdinalIgnoreCase);

                var orderedSources = timelineSources
                    .Concat(allSources.Where(source => !timelinePaths.ContainsKey(source.ArtifactPath)))
                    .ToList();

                foreach (var source in orderedSources.AsEnumerable().Reverse())
                {
                    var isSelected = timelinePaths.TryGetValue(source.ArtifactPath, out var index);
                    host.Controls.Add(BuildClipCard(source, currentSource, isSelected ? index : -1, timelineSources.Count));
                }
            }

            layout.Controls.Add(host, 0, 2);
            shell.Controls.Add(layout);
            return shell;
        }

        private Control BuildMaterialActions()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 6),
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var importVideoButton = CreateActionButton("导入视频", Color.FromArgb(74, 161, 255));
            importVideoButton.Enabled = !_busy;
            importVideoButton.Click += (_, _) => PickVideoFiles();
            row.Controls.Add(importVideoButton, 0, 0);

            var importAudioButton = CreateActionButton("导入音频", Color.FromArgb(58, 64, 78));
            importAudioButton.Enabled = !_busy;
            importAudioButton.Click += (_, _) => PickAudioTrack();
            row.Controls.Add(importAudioButton, 1, 0);

            row.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(110, 118, 138),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = "本地素材",
            }, 2, 0);
            return row;
        }

        private Control BuildTimelineSection(IReadOnlyList<VideoCollectionSourceItem> timelineSources)
        {
            var shell = CreatePanelShell(new Padding(12));
            shell.Margin = new Padding(0, 0, 0, 8);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "时间线",
            }, 0, 0);

            var tracks = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Margin = Padding.Empty,
                BackColor = Color.FromArgb(28, 29, 34),
                Padding = new Padding(8),
            };
            tracks.AllowDrop = true;
            tracks.DragEnter += (_, e) => SetAudioFileDropEffect(e);
            tracks.DragOver += (_, e) => SetAudioFileDropEffect(e);
            tracks.DragDrop += (_, e) => DropAudioFileToTrack(e);
            tracks.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            tracks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tracks.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));
            tracks.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));
            tracks.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));
            tracks.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));
            tracks.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));

            AddVideoTrackRow(tracks, timelineSources);
            AddTextTrackRow(tracks, 1, "文字", GetOverlaySegments("text"), Color.FromArgb(85, 74, 140));
            AddTextTrackRow(tracks, 2, "图片", GetOverlaySegments("image"), Color.FromArgb(103, 77, 39));
            AddTextTrackRow(tracks, 3, "字幕", GetSubtitleSegments(), Color.FromArgb(78, 85, 120));
            AddTextTrackRow(tracks, 4, "音频", GetAudioSegments(), Color.FromArgb(35, 98, 92));

            layout.Controls.Add(tracks, 0, 1);
            shell.Controls.Add(layout);
            return shell;
        }

        private void AddVideoTrackRow(TableLayoutPanel tracks, IReadOnlyList<VideoCollectionSourceItem> timelineSources)
        {
            tracks.Controls.Add(CreateTrackLabel("视频"), 0, 0);

            var lane = CreateTrackLane();
            lane.AllowDrop = true;
            lane.DragEnter += (_, e) => SetTimelineDragEffect(e);
            lane.DragOver += (_, e) => SetTimelineDragEffect(e);
            lane.DragDrop += (_, e) => DropTimelineClipToEnd(e);

            if (timelineSources.Count == 0)
            {
                lane.Controls.Add(BuildTextChip("拖入或勾选视频片段", string.Empty, Color.FromArgb(50, 54, 68)));
            }
            else
            {
                for (var index = 0; index < timelineSources.Count; index++)
                {
                    var source = timelineSources[index];
                    lane.Controls.Add(BuildTimelineClipChip(source, index));
                }
            }

            tracks.Controls.Add(lane, 1, 0);
        }

        private void AddTextTrackRow(TableLayoutPanel tracks, int row, string label, IEnumerable<string> segments, Color color)
        {
            tracks.Controls.Add(CreateTrackLabel(label), 0, row);

            var lane = CreateTrackLane();
            lane.AllowDrop = row == 2;
            lane.DragEnter += (_, e) => SetAudioFileDropEffect(e);
            lane.DragOver += (_, e) => SetAudioFileDropEffect(e);
            lane.DragDrop += (_, e) => DropAudioFileToTrack(e);

            foreach (var segment in segments.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                lane.Controls.Add(BuildTextChip(segment, string.Empty, color));
            }

            tracks.Controls.Add(lane, 1, row);
        }

        private IEnumerable<string> GetSubtitleSegments()
        {
            var captions = (_node?.Params?.VideoCollectionTimelineClips ?? new List<VideoCollectionTimelineClip>())
                .Select(clip => KeepSimplifiedChineseVisibleTextOnly(clip.Caption))
                .Where(caption => !string.IsNullOrWhiteSpace(caption));
            return captions.DefaultIfEmpty(string.IsNullOrWhiteSpace(_node?.Params?.VideoCollectionSubtitleText) ? "未添加字幕" : "全局字幕");
        }

        private IEnumerable<string> GetAudioSegments()
        {
            return new[]
            {
                string.IsNullOrWhiteSpace(_node?.Params?.VideoCollectionAudioPath)
                    ? "原视频音频"
                    : Path.GetFileName(_node.Params.VideoCollectionAudioPath),
            };
        }

        private IEnumerable<string> GetOverlaySegments(string kind)
        {
            var normalizedKind = WorkflowNodeParameters.NormalizeVideoCollectionOverlayKind(kind);
            var segments = (_node?.Params?.VideoCollectionOverlayItems ?? new List<VideoCollectionOverlayItem>())
                .Where(item => string.Equals(WorkflowNodeParameters.NormalizeVideoCollectionOverlayKind(item.Kind), normalizedKind, StringComparison.Ordinal))
                .Select(item =>
                {
                    var start = item.StartSeconds.ToString("0.#");
                    if (string.Equals(normalizedKind, "image", StringComparison.Ordinal))
                    {
                        var imageName = string.IsNullOrWhiteSpace(item.ImagePath) ? "图片" : Path.GetFileName(item.ImagePath);
                        return $"{start}s  {imageName}";
                    }

                    var text = KeepSimplifiedChineseVisibleTextOnly(item.Text);
                    return $"{start}s  {(string.IsNullOrWhiteSpace(text) ? "文字" : text)}";
                })
                .Where(text => !string.IsNullOrWhiteSpace(text));

            return segments.DefaultIfEmpty(string.Equals(normalizedKind, "image", StringComparison.Ordinal) ? "未添加图片" : "未添加文字");
        }

        private static Label CreateTrackLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(154, 162, 184),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = text,
            };
        }

        private static FlowLayoutPanel CreateTrackLane()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.FromArgb(24, 25, 30),
                Margin = new Padding(0, 2, 0, 2),
                Padding = new Padding(4, 3, 4, 3),
            };
        }

        private Control BuildTimelineClipChip(VideoCollectionSourceItem source, int index)
        {
            var chip = BuildTextChip($"#{index + 1}  {source.DurationLabel}  {source.DisplayName}", source.ArtifactPath, Color.FromArgb(156, 24, 28));
            AttachTimelineInteraction(chip, source, true);
            return chip;
        }

        private static Label BuildTextChip(string text, string tag, Color backColor)
        {
            return new Label
            {
                Width = Math.Max(160, Math.Min(360, 120 + text.Length * 8)),
                Height = 30,
                Margin = new Padding(4, 2, 4, 2),
                Padding = new Padding(10, 0, 10, 0),
                BackColor = backColor,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true,
                Text = text,
                Tag = tag,
                Cursor = string.IsNullOrWhiteSpace(tag) ? Cursors.Default : Cursors.SizeAll,
            };
        }

        private Control BuildClipCard(
            VideoCollectionSourceItem source,
            VideoCollectionSourceItem? currentSource,
            int timelineIndex,
            int timelineCount)
        {
            var isCurrent = string.Equals(currentSource?.ArtifactPath, source.ArtifactPath, StringComparison.OrdinalIgnoreCase);
            var isSelectedForMerge = timelineIndex >= 0;

            var card = new Panel
            {
                Dock = DockStyle.Top,
                Height = 112,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(8),
                BackColor = isCurrent ? Color.FromArgb(34, 40, 56) : Color.FromArgb(24, 25, 30),
                Cursor = Cursors.Hand,
                Tag = source.ArtifactPath,
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));

            var selector = new CheckBox
            {
                Dock = DockStyle.Fill,
                Checked = isSelectedForMerge,
                CheckAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Margin = Padding.Empty,
            };
            selector.CheckedChanged += (_, _) => ToggleSelection(source.ArtifactPath, selector.Checked);
            layout.Controls.Add(selector, 0, 0);

            var thumbnailShell = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 10, 0),
                BackColor = Color.FromArgb(18, 19, 24),
            };
            if (!string.IsNullOrWhiteSpace(source.ThumbnailPath) && File.Exists(source.ThumbnailPath))
            {
                thumbnailShell.Controls.Add(CreatePreviewPictureBox(source.ThumbnailPath));
            }
            else
            {
                thumbnailShell.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    ForeColor = Color.FromArgb(110, 118, 138),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Text = "视频",
                });
            }
            layout.Controls.Add(thumbnailShell, 1, 0);

            var info = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = Padding.Empty,
            };
            info.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            info.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            info.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var orderText = isSelectedForMerge ? $"#{timelineIndex + 1} " : "未入轨 ";
            info.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = orderText + source.DisplayName,
            }, 0, 0);

            info.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(196, 164, 255),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = $"{source.SourceNodeType} | {source.DurationLabel}",
            }, 0, 1);

            info.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                TextAlign = ContentAlignment.TopLeft,
                AutoEllipsis = true,
                Text = source.Summary,
            }, 0, 2);

            layout.Controls.Add(info, 2, 0);
            layout.Controls.Add(BuildClipCardActions(source, isSelectedForMerge, timelineIndex, timelineCount), 3, 0);

            card.Controls.Add(layout);
            AttachTimelineInteraction(card, source, isSelectedForMerge);
            return card;
        }

        private Control BuildClipCardActions(VideoCollectionSourceItem source, bool isSelectedForMerge, int timelineIndex, int timelineCount)
        {
            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Margin = Padding.Empty,
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 34F));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));

            var previewButton = CreateActionButton("预览", Color.FromArgb(74, 161, 255));
            previewButton.Margin = new Padding(0, 0, 3, 3);
            previewButton.Click += (_, _) => SetCurrentArtifact(source.ArtifactPath);
            actions.Controls.Add(previewButton, 0, 0);

            var playButton = CreateActionButton("播放", Color.FromArgb(58, 64, 78));
            playButton.Margin = new Padding(3, 0, 0, 3);
            playButton.Enabled = !_busy && File.Exists(source.ArtifactPath);
            playButton.Click += (_, _) => OpenVideo(source.ArtifactPath);
            actions.Controls.Add(playButton, 1, 0);

            var upButton = CreateActionButton("前移", Color.FromArgb(58, 64, 78));
            upButton.Margin = new Padding(0, 3, 3, 0);
            upButton.Enabled = !_busy && isSelectedForMerge && timelineIndex > 0;
            upButton.Click += (_, _) => MoveTimelineClip(source.ArtifactPath, -1);
            actions.Controls.Add(upButton, 0, 1);

            var downButton = CreateActionButton("后移", Color.FromArgb(58, 64, 78));
            downButton.Margin = new Padding(3, 3, 0, 0);
            downButton.Enabled = !_busy && isSelectedForMerge && timelineIndex >= 0 && timelineIndex < timelineCount - 1;
            downButton.Click += (_, _) => MoveTimelineClip(source.ArtifactPath, 1);
            actions.Controls.Add(downButton, 1, 1);

            var removeButton = CreateActionButton("删除", Color.FromArgb(126, 30, 34));
            removeButton.Margin = new Padding(0, 4, 0, 0);
            removeButton.Enabled = !_busy;
            removeButton.Click += (_, _) => RemoveSourceFromCollection(source.ArtifactPath);
            actions.Controls.Add(removeButton, 0, 2);
            actions.SetColumnSpan(removeButton, 2);
            return actions;
        }

        private Control BuildEditOptionsSection()
        {
            var shell = CreatePanelShell(new Padding(12));
            shell.Margin = new Padding(0, 0, 0, 8);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Margin = Padding.Empty,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "剪辑设置",
            }, 0, 0);
            layout.Controls.Add(BuildAudioRow(), 0, 1);
            layout.Controls.Add(BuildTransitionRow(), 0, 2);
            layout.Controls.Add(BuildOverlayRow(), 0, 3);
            layout.Controls.Add(BuildSubtitleRow(), 0, 4);

            shell.Controls.Add(layout);
            return shell;
        }

        private Control BuildAudioRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 1,
                Margin = new Padding(0, 2, 0, 2),
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));

            row.Controls.Add(CreateSmallLabel("音轨"), 0, 0);
            row.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(214, 220, 232),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = string.IsNullOrWhiteSpace(_node?.Params?.VideoCollectionAudioPath)
                    ? "原视频音频"
                    : Path.GetFileName(_node.Params.VideoCollectionAudioPath),
            }, 1, 0);

            row.Controls.Add(CreateSmallLabel("音量"), 2, 0);

            var volume = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                DecimalPlaces = 1,
                Increment = 0.1M,
                Minimum = 0.1M,
                Maximum = 2M,
                Value = Math.Clamp(_node?.Params?.VideoCollectionAudioVolume ?? 1M, 0.1M, 2M),
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.WhiteSmoke,
                Enabled = !_busy,
            };
            volume.ValueChanged += (_, _) =>
            {
                if (_node?.Params == null)
                {
                    return;
                }

                InteractionStarted?.Invoke(this, EventArgs.Empty);
                _node.Params.VideoCollectionAudioVolume = volume.Value;
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };
            row.Controls.Add(volume, 3, 0);

            var detachButton = CreateActionButton("分离原音", Color.FromArgb(96, 126, 255));
            detachButton.Margin = new Padding(0, 0, 6, 0);
            detachButton.Enabled = !_busy;
            detachButton.Click += (_, _) =>
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                ActionRequested?.Invoke(this, "video-collection.extract-audio");
            };
            row.Controls.Add(detachButton, 4, 0);

            var chooseButton = CreateActionButton("添加", Color.FromArgb(74, 161, 255));
            chooseButton.Margin = new Padding(0, 0, 6, 0);
            chooseButton.Enabled = !_busy;
            chooseButton.Click += (_, _) => PickAudioTrack();
            row.Controls.Add(chooseButton, 5, 0);

            var clearButton = CreateActionButton("清除", Color.FromArgb(58, 64, 78));
            clearButton.Enabled = !_busy && !string.IsNullOrWhiteSpace(_node?.Params?.VideoCollectionAudioPath);
            clearButton.Click += (_, _) =>
            {
                if (_node?.Params == null)
                {
                    return;
                }

                InteractionStarted?.Invoke(this, EventArgs.Empty);
                _node.Params.VideoCollectionAudioPath = string.Empty;
                EntryChanged?.Invoke(this, EventArgs.Empty);
                Rebuild();
            };
            row.Controls.Add(clearButton, 6, 0);
            return row;
        }

        private Control BuildTransitionRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0, 2, 0, 2),
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));

            row.Controls.Add(CreateSmallLabel("转场"), 0, 0);

            var combo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.WhiteSmoke,
                Enabled = !_busy,
            };
            combo.Items.AddRange(TransitionLabels.Cast<object>().ToArray());
            var activeTransition = WorkflowNodeParameters.NormalizeVideoCollectionTransitionType(_node?.Params?.VideoCollectionTransitionType);
            combo.SelectedIndex = Math.Max(0, Array.IndexOf(TransitionValues, activeTransition));
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (_node?.Params == null || combo.SelectedIndex < 0)
                {
                    return;
                }

                InteractionStarted?.Invoke(this, EventArgs.Empty);
                _node.Params.VideoCollectionTransitionType = TransitionValues[Math.Clamp(combo.SelectedIndex, 0, TransitionValues.Length - 1)];
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };
            row.Controls.Add(combo, 1, 0);

            row.Controls.Add(CreateSmallLabel("时长"), 2, 0);

            var seconds = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                DecimalPlaces = 1,
                Increment = 0.1M,
                Minimum = 0.2M,
                Maximum = 2M,
                Value = Math.Clamp(_node?.Params?.VideoCollectionTransitionSeconds ?? 0.4M, 0.2M, 2M),
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.WhiteSmoke,
                Enabled = !_busy,
            };
            seconds.ValueChanged += (_, _) =>
            {
                if (_node?.Params == null)
                {
                    return;
                }

                InteractionStarted?.Invoke(this, EventArgs.Empty);
                _node.Params.VideoCollectionTransitionSeconds = seconds.Value;
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };
            row.Controls.Add(seconds, 3, 0);
            return row;
        }

        private Control BuildOverlayRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 9,
                RowCount = 2,
                Margin = new Padding(0, 2, 0, 2),
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            var start = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                DecimalPlaces = 1,
                Increment = 0.5M,
                Minimum = 0M,
                Maximum = 3600M,
                Value = 0M,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.WhiteSmoke,
                Enabled = !_busy,
            };

            var duration = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                DecimalPlaces = 1,
                Increment = 0.5M,
                Minimum = 0.5M,
                Maximum = 3600M,
                Value = 3M,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.WhiteSmoke,
                Enabled = !_busy,
            };

            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.WhiteSmoke,
                Enabled = !_busy,
            };
            textBox.TextChanged += (_, _) =>
            {
                var sanitized = KeepSimplifiedChineseVisibleTextOnly(textBox.Text);
                if (string.Equals(textBox.Text, sanitized, StringComparison.Ordinal))
                {
                    return;
                }

                var selectionStart = Math.Min(sanitized.Length, textBox.SelectionStart);
                textBox.Text = sanitized;
                textBox.SelectionStart = selectionStart;
            };

            row.Controls.Add(CreateSmallLabel("文字"), 0, 0);
            row.Controls.Add(textBox, 1, 0);
            row.Controls.Add(CreateSmallLabel("开始"), 2, 0);
            row.Controls.Add(start, 3, 0);
            row.Controls.Add(CreateSmallLabel("时长"), 4, 0);
            row.Controls.Add(duration, 5, 0);

            var addTextButton = CreateActionButton("添加文字", Color.FromArgb(96, 126, 255));
            addTextButton.Enabled = !_busy;
            addTextButton.Click += (_, _) => AddTextOverlay(textBox.Text, start.Value, duration.Value);
            row.Controls.Add(addTextButton, 6, 0);

            row.Controls.Add(CreateSmallLabel("图片"), 0, 1);
            row.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(154, 162, 184),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = $"叠加 {(_node?.Params?.VideoCollectionOverlayItems?.Count ?? 0)} 项",
            }, 1, 1);
            row.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 2, 1);
            row.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 3, 1);
            row.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 4, 1);
            row.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 5, 1);

            var addImageButton = CreateActionButton("添加图片", Color.FromArgb(103, 77, 39));
            addImageButton.Enabled = !_busy;
            addImageButton.Click += (_, _) => PickOverlayImage(start.Value, duration.Value);
            row.Controls.Add(addImageButton, 7, 1);

            var clearButton = CreateActionButton("清空", Color.FromArgb(58, 64, 78));
            clearButton.Enabled = !_busy && (_node?.Params?.VideoCollectionOverlayItems?.Count ?? 0) > 0;
            clearButton.Click += (_, _) => ClearOverlays();
            row.Controls.Add(clearButton, 8, 1);
            return row;
        }

        private Control BuildSubtitleRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 0),
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.Controls.Add(CreateSmallLabel("字幕", ContentAlignment.TopLeft), 0, 0);

            var subtitleBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.WhiteSmoke,
                Text = KeepSimplifiedChineseVisibleTextBlock(_node?.Params?.VideoCollectionSubtitleText ?? string.Empty),
            };
            subtitleBox.TextChanged += (_, _) =>
            {
                if (_node?.Params == null)
                {
                    return;
                }

                InteractionStarted?.Invoke(this, EventArgs.Empty);
                var sanitized = KeepSimplifiedChineseVisibleTextBlock(subtitleBox.Text);
                if (!string.Equals(subtitleBox.Text, sanitized, StringComparison.Ordinal))
                {
                    var selectionStart = Math.Min(sanitized.Length, subtitleBox.SelectionStart);
                    subtitleBox.Text = sanitized;
                    subtitleBox.SelectionStart = selectionStart;
                }

                _node.Params.VideoCollectionSubtitleText = sanitized;
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };
            row.Controls.Add(subtitleBox, 1, 0);
            return row;
        }


        private static string KeepSimplifiedChineseVisibleTextOnly(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var filtered = Regex.Replace(text, @"\[[^\]]*\]", string.Empty);
            filtered = Regex.Replace(filtered, @"[A-Za-z0-9_./\\:@#%&+=*<>|~^$]+", string.Empty);
            filtered = Regex.Replace(filtered, @"[^\u3400-\u4DBF\u4E00-\u9FFF，。！？、：；“”‘’（）《》【】—…\s]", string.Empty);
            filtered = Regex.Replace(filtered, @"\s+", " ").Trim();
            return Regex.IsMatch(filtered, @"[\u3400-\u4DBF\u4E00-\u9FFF]") ? filtered : string.Empty;
        }

        private static string KeepSimplifiedChineseVisibleTextBlock(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return string.Join(
                Environment.NewLine,
                text.Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace("\r", "\n", StringComparison.Ordinal)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(KeepSimplifiedChineseVisibleTextOnly)
                    .Where(line => !string.IsNullOrWhiteSpace(line)));
        }

        private Control BuildFooter(int timelineCount)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 8, 0, 0),
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));

            var footerText = !string.IsNullOrWhiteSpace(_node?.ArtifactPath) && File.Exists(_node.ArtifactPath)
                ? $"合集结果：{Path.GetFileName(_node.ArtifactPath)}"
                : "按时间线顺序生成合集；支持预览、排序、字幕、音频、转场。";

            row.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(154, 162, 184),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = footerText,
            }, 0, 0);

            var mergeButton = CreateActionButton(_busy ? "合集中..." : "生成合集视频", Color.FromArgb(255, 122, 0));
            mergeButton.Enabled = !_busy && timelineCount > 0;
            mergeButton.Click += (_, _) =>
            {
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                ActionRequested?.Invoke(this, "video-collection.generate-video");
            };
            row.Controls.Add(mergeButton, 1, 0);
            return row;
        }

        private static Label CreateSmallLabel(string text, ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(154, 162, 184),
                TextAlign = align,
                Text = text,
            };
        }

        private string GetPreviewPath(VideoCollectionSourceItem? currentSource)
        {
            return !string.IsNullOrWhiteSpace(_node?.ArtifactPath) && File.Exists(_node.ArtifactPath)
                ? _node.ArtifactPath
                : currentSource?.ArtifactPath ?? string.Empty;
        }

        private VideoCollectionSourceItem? ResolveCurrentSource(IReadOnlyList<VideoCollectionSourceItem> sources)
        {
            if (_node?.Params == null)
            {
                return null;
            }

            return sources.FirstOrDefault(item =>
                       string.Equals(item.ArtifactPath, _node.Params.VideoCollectionCurrentArtifactPath, StringComparison.OrdinalIgnoreCase))
                   ?? sources.FirstOrDefault();
        }

        private void ToggleSelection(string artifactPath, bool selected)
        {
            if (_node?.Params == null || string.IsNullOrWhiteSpace(artifactPath))
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.VideoCollectionTimelineClips ??= new List<VideoCollectionTimelineClip>();

            var selectedPaths = new HashSet<string>(
                _node.Params.VideoCollectionSelectedArtifactPaths ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (selected)
            {
                selectedPaths.Add(artifactPath);
                if (!_node.Params.VideoCollectionTimelineClips
                    .Any(clip => string.Equals(clip.ArtifactPath, artifactPath, StringComparison.OrdinalIgnoreCase)))
                {
                    _node.Params.VideoCollectionTimelineClips.Add(new VideoCollectionTimelineClip
                    {
                        ArtifactPath = artifactPath,
                        Enabled = true,
                    });
                }
            }
            else
            {
                selectedPaths.RemoveWhere(path => string.Equals(path, artifactPath, StringComparison.OrdinalIgnoreCase));
                _node.Params.VideoCollectionTimelineClips.RemoveAll(clip =>
                    string.Equals(clip.ArtifactPath, artifactPath, StringComparison.OrdinalIgnoreCase));
            }

            _node.Params.VideoCollectionSelectedArtifactPaths = selectedPaths.ToList();
            _node.Params.VideoCollectionSelectionInitialized = true;
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void MoveTimelineClip(string artifactPath, int delta)
        {
            if (_node?.Params == null || string.IsNullOrWhiteSpace(artifactPath) || delta == 0)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            var clips = _node.Params.VideoCollectionTimelineClips ?? new List<VideoCollectionTimelineClip>();
            var index = clips.FindIndex(clip => string.Equals(clip.ArtifactPath, artifactPath, StringComparison.OrdinalIgnoreCase));
            var nextIndex = index + delta;
            if (index < 0 || nextIndex < 0 || nextIndex >= clips.Count)
            {
                return;
            }

            (clips[index], clips[nextIndex]) = (clips[nextIndex], clips[index]);
            _node.Params.VideoCollectionTimelineClips = clips;
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void MoveTimelineClipToEnd(string artifactPath)
        {
            if (_node?.Params == null || string.IsNullOrWhiteSpace(artifactPath))
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            var clips = _node.Params.VideoCollectionTimelineClips ?? new List<VideoCollectionTimelineClip>();
            var index = clips.FindIndex(clip => string.Equals(clip.ArtifactPath, artifactPath, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index == clips.Count - 1)
            {
                return;
            }

            var clip = clips[index];
            clips.RemoveAt(index);
            clips.Add(clip);
            _node.Params.VideoCollectionTimelineClips = clips;
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void ReorderTimelineClip(string draggedPath, string targetPath, bool insertAfter)
        {
            if (_node?.Params == null ||
                string.IsNullOrWhiteSpace(draggedPath) ||
                string.IsNullOrWhiteSpace(targetPath) ||
                string.Equals(draggedPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            var clips = _node.Params.VideoCollectionTimelineClips ?? new List<VideoCollectionTimelineClip>();
            var draggedIndex = clips.FindIndex(clip => string.Equals(clip.ArtifactPath, draggedPath, StringComparison.OrdinalIgnoreCase));
            var targetIndex = clips.FindIndex(clip => string.Equals(clip.ArtifactPath, targetPath, StringComparison.OrdinalIgnoreCase));
            if (draggedIndex < 0 || targetIndex < 0)
            {
                return;
            }

            var clip = clips[draggedIndex];
            clips.RemoveAt(draggedIndex);
            if (draggedIndex < targetIndex)
            {
                targetIndex--;
            }

            var insertIndex = insertAfter ? targetIndex + 1 : targetIndex;
            insertIndex = Math.Clamp(insertIndex, 0, clips.Count);
            clips.Insert(insertIndex, clip);
            _node.Params.VideoCollectionTimelineClips = clips;
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void PickVideoFiles()
        {
            if (_node?.Params == null)
            {
                return;
            }

            using var dialog = new OpenFileDialog
            {
                Title = "导入视频素材",
                Filter = "视频文件|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.wmv;*.m4v|所有文件|*.*",
                CheckFileExists = true,
                Multiselect = true,
            };

            if (dialog.ShowDialog(FindForm()) != DialogResult.OK || dialog.FileNames.Length == 0)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.VideoCollectionImportedAssets ??= new List<VideoCollectionImportedAsset>();
            _node.Params.VideoCollectionSelectedArtifactPaths ??= new List<string>();
            _node.Params.VideoCollectionTimelineClips ??= new List<VideoCollectionTimelineClip>();

            foreach (var fileName in dialog.FileNames.Where(IsSupportedVideoFile))
            {
                var normalizedPath = Path.GetFullPath(fileName);
                if (!_node.Params.VideoCollectionImportedAssets.Any(asset =>
                        string.Equals(NormalizeFilePath(asset.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    _node.Params.VideoCollectionImportedAssets.Add(new VideoCollectionImportedAsset
                    {
                        FilePath = normalizedPath,
                        Kind = "video",
                        DisplayName = Path.GetFileNameWithoutExtension(normalizedPath),
                        DurationSeconds = EstimateMediaDurationSeconds(normalizedPath),
                    });
                }

                if (!_node.Params.VideoCollectionSelectedArtifactPaths.Any(path =>
                        string.Equals(NormalizeFilePath(path), normalizedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    _node.Params.VideoCollectionSelectedArtifactPaths.Add(normalizedPath);
                }

                if (!_node.Params.VideoCollectionTimelineClips.Any(clip =>
                        string.Equals(NormalizeFilePath(clip.ArtifactPath), normalizedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    _node.Params.VideoCollectionTimelineClips.Add(new VideoCollectionTimelineClip
                    {
                        ArtifactPath = normalizedPath,
                        Enabled = true,
                        Caption = "导入视频",
                    });
                }

                _node.Params.VideoCollectionCurrentArtifactPath = normalizedPath;
            }

            _node.Params.VideoCollectionSelectionInitialized = true;
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void RemoveSourceFromCollection(string artifactPath)
        {
            if (_node?.Params == null || string.IsNullOrWhiteSpace(artifactPath))
            {
                return;
            }

            var normalizedPath = NormalizeFilePath(artifactPath);
            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.VideoCollectionImportedAssets ??= new List<VideoCollectionImportedAsset>();
            _node.Params.VideoCollectionSelectedArtifactPaths ??= new List<string>();
            _node.Params.VideoCollectionTimelineClips ??= new List<VideoCollectionTimelineClip>();

            _node.Params.VideoCollectionImportedAssets.RemoveAll(asset =>
                string.Equals(NormalizeFilePath(asset.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase));
            _node.Params.VideoCollectionSelectedArtifactPaths.RemoveAll(path =>
                string.Equals(NormalizeFilePath(path), normalizedPath, StringComparison.OrdinalIgnoreCase));
            _node.Params.VideoCollectionTimelineClips.RemoveAll(clip =>
                string.Equals(NormalizeFilePath(clip.ArtifactPath), normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (string.Equals(NormalizeFilePath(_node.Params.VideoCollectionCurrentArtifactPath), normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                _node.Params.VideoCollectionCurrentArtifactPath = string.Empty;
            }

            _node.Params.VideoCollectionSelectionInitialized = true;
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void AddTextOverlay(string text, decimal startSeconds, decimal durationSeconds)
        {
            if (_node?.Params == null)
            {
                return;
            }

            var sanitized = KeepSimplifiedChineseVisibleTextOnly(text);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                MessageBox.Show(FindForm(), "请输入简体中文文字。", "添加文字", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.VideoCollectionOverlayItems ??= new List<VideoCollectionOverlayItem>();
            _node.Params.VideoCollectionOverlayItems.Add(new VideoCollectionOverlayItem
            {
                Kind = "text",
                Text = sanitized,
                StartSeconds = Math.Max(0M, startSeconds),
                DurationSeconds = Math.Max(0.5M, durationSeconds),
                X = 0.5M,
                Y = 0.82M,
                FontSize = 44,
                ForeColor = "#FFFFFF",
            });
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void PickOverlayImage(decimal startSeconds, decimal durationSeconds)
        {
            if (_node?.Params == null)
            {
                return;
            }

            using var dialog = new OpenFileDialog
            {
                Title = "添加叠加图片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp|所有文件|*.*",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.VideoCollectionOverlayItems ??= new List<VideoCollectionOverlayItem>();
            _node.Params.VideoCollectionOverlayItems.Add(new VideoCollectionOverlayItem
            {
                Kind = "image",
                ImagePath = Path.GetFullPath(dialog.FileName),
                StartSeconds = Math.Max(0M, startSeconds),
                DurationSeconds = Math.Max(0.5M, durationSeconds),
                X = 0.72M,
                Y = 0.12M,
                WidthRatio = 0.24M,
            });
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void ClearOverlays()
        {
            if (_node?.Params == null)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.VideoCollectionOverlayItems = new List<VideoCollectionOverlayItem>();
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void PickAudioTrack()
        {
            if (_node?.Params == null)
            {
                return;
            }

            using var dialog = new OpenFileDialog
            {
                Title = "选择音轨",
                Filter = "音频文件|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|所有文件|*.*",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.VideoCollectionAudioPath = dialog.FileName;
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void SetCurrentArtifact(string artifactPath)
        {
            if (_node?.Params == null || string.IsNullOrWhiteSpace(artifactPath))
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.VideoCollectionCurrentArtifactPath = artifactPath;
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private void SetAudioFileDropEffect(DragEventArgs e)
        {
            e.Effect = !_busy && TryGetDroppedAudioFile(e, out _)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void DropAudioFileToTrack(DragEventArgs e)
        {
            if (_node?.Params == null || _busy || !TryGetDroppedAudioFile(e, out var filePath))
            {
                return;
            }

            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _node.Params.VideoCollectionAudioPath = filePath;
            EntryChanged?.Invoke(this, EventArgs.Empty);
            Rebuild();
        }

        private static bool TryGetDroppedAudioFile(DragEventArgs e, out string filePath)
        {
            filePath = string.Empty;
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return false;
            }

            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            {
                return false;
            }

            filePath = files.FirstOrDefault(file =>
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                return File.Exists(file) && (extension == ".mp3" || extension == ".wav" || extension == ".m4a" || extension == ".aac" || extension == ".flac" || extension == ".ogg");
            }) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(filePath);
        }

        private static bool IsSupportedVideoFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension == ".mp4" ||
                   extension == ".mov" ||
                   extension == ".mkv" ||
                   extension == ".webm" ||
                   extension == ".avi" ||
                   extension == ".wmv" ||
                   extension == ".m4v";
        }

        private static string NormalizeFilePath(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(filePath);
            }
            catch
            {
                return filePath.Trim();
            }
        }

        private static int EstimateMediaDurationSeconds(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return 5;
            }

            var alias = "jsai_duration_" + Guid.NewGuid().ToString("N");
            try
            {
                if (mciSendString($"open \"{filePath}\" type mpegvideo alias {alias}", null, 0, IntPtr.Zero) != 0)
                {
                    return 5;
                }

                var buffer = new StringBuilder(64);
                if (mciSendString($"status {alias} length", buffer, buffer.Capacity, IntPtr.Zero) == 0 &&
                    int.TryParse(buffer.ToString(), out var milliseconds) &&
                    milliseconds > 0)
                {
                    return Math.Max(1, (int)Math.Ceiling(milliseconds / 1000.0));
                }
            }
            catch
            {
            }
            finally
            {
                mciSendString($"close {alias}", null, 0, IntPtr.Zero);
            }

            return 5;
        }

        private void AttachTimelineInteraction(Control control, VideoCollectionSourceItem source, bool isSelectedForMerge)
        {
            if (control is Button || control is CheckBox || control is TextBox || control is ComboBox || control is NumericUpDown)
            {
                return;
            }

            control.Click += (_, _) =>
            {
                if (_suppressNextClick)
                {
                    _suppressNextClick = false;
                    return;
                }

                SetCurrentArtifact(source.ArtifactPath);
            };

            if (isSelectedForMerge)
            {
                control.MouseDown += (_, e) => CaptureTimelineDragStart(e, source.ArtifactPath);
                control.MouseMove += (sender, e) => StartTimelineDrag(sender as Control, e, source.ArtifactPath);
                control.AllowDrop = true;
                control.DragEnter += (_, e) => SetTimelineDragEffect(e, source.ArtifactPath);
                control.DragOver += (_, e) => SetTimelineDragEffect(e, source.ArtifactPath);
                control.DragDrop += (sender, e) => DropTimelineClipOnTarget(sender as Control, e, source.ArtifactPath);
            }

            foreach (Control child in control.Controls)
            {
                AttachTimelineInteraction(child, source, isSelectedForMerge);
            }
        }

        private void CaptureTimelineDragStart(MouseEventArgs e, string artifactPath)
        {
            if (_busy || e.Button != MouseButtons.Left || string.IsNullOrWhiteSpace(artifactPath) || !ContainsTimelineClip(artifactPath))
            {
                _pendingDragPath = string.Empty;
                return;
            }

            _pendingDragPath = artifactPath;
            _pendingDragScreenPoint = Control.MousePosition;
        }

        private void StartTimelineDrag(Control? sourceControl, MouseEventArgs e, string artifactPath)
        {
            if (_busy ||
                sourceControl == null ||
                e.Button != MouseButtons.Left ||
                string.IsNullOrWhiteSpace(_pendingDragPath) ||
                !string.Equals(_pendingDragPath, artifactPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Size dragSize = SystemInformation.DragSize;
            var dragBounds = new Rectangle(
                _pendingDragScreenPoint.X - dragSize.Width / 2,
                _pendingDragScreenPoint.Y - dragSize.Height / 2,
                dragSize.Width,
                dragSize.Height);
            if (dragBounds.Contains(Control.MousePosition))
            {
                return;
            }

            _suppressNextClick = true;
            sourceControl.DoDragDrop(artifactPath, DragDropEffects.Move);
            _pendingDragPath = string.Empty;
        }

        private void SetTimelineDragEffect(DragEventArgs e, string? targetPath = null)
        {
            string draggedPath = GetDraggedArtifactPath(e);
            e.Effect = !_busy &&
                       ContainsTimelineClip(draggedPath) &&
                       (string.IsNullOrWhiteSpace(targetPath) || ContainsTimelineClip(targetPath))
                ? DragDropEffects.Move
                : DragDropEffects.None;
        }

        private void DropTimelineClipOnTarget(Control? targetControl, DragEventArgs e, string targetPath)
        {
            string draggedPath = GetDraggedArtifactPath(e);
            if (!ContainsTimelineClip(draggedPath) || !ContainsTimelineClip(targetPath))
            {
                return;
            }

            Control? card = ResolveTimelineCard(targetControl, targetPath);
            var cursor = new Point(e.X, e.Y);
            Point clientPoint = card == null ? Point.Empty : card.PointToClient(cursor);
            bool insertAfter = card != null && (card.Width > card.Height
                ? clientPoint.X > card.Width / 2
                : clientPoint.Y > card.Height / 2);
            ReorderTimelineClip(draggedPath, targetPath, insertAfter);
        }

        private void DropTimelineClipToEnd(DragEventArgs e)
        {
            string draggedPath = GetDraggedArtifactPath(e);
            if (!ContainsTimelineClip(draggedPath))
            {
                return;
            }

            MoveTimelineClipToEnd(draggedPath);
        }

        private bool ContainsTimelineClip(string? artifactPath)
        {
            if (_node?.Params?.VideoCollectionTimelineClips == null || string.IsNullOrWhiteSpace(artifactPath))
            {
                return false;
            }

            return _node.Params.VideoCollectionTimelineClips
                .Any(clip => string.Equals(clip.ArtifactPath, artifactPath, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetDraggedArtifactPath(DragEventArgs e)
        {
            if (e.Data == null)
            {
                return string.Empty;
            }

            if (e.Data.GetDataPresent(DataFormats.UnicodeText))
            {
                return e.Data.GetData(DataFormats.UnicodeText)?.ToString() ?? string.Empty;
            }

            if (e.Data.GetDataPresent(DataFormats.Text))
            {
                return e.Data.GetData(DataFormats.Text)?.ToString() ?? string.Empty;
            }

            if (e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                return e.Data.GetData(DataFormats.StringFormat)?.ToString() ?? string.Empty;
            }

            return e.Data.GetData(typeof(string))?.ToString() ?? string.Empty;
        }

        private static Control? ResolveTimelineCard(Control? control, string targetPath)
        {
            while (control != null)
            {
                if (control.Tag is string path && string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return control;
                }

                control = control.Parent;
            }

            return null;
        }

        private void RefreshPreviewOverlays()
        {
            if (_previewHost == null || _previewHost.IsDisposed)
            {
                return;
            }

            foreach (var control in _previewOverlayControls.ToList())
            {
                _previewHost.Controls.Remove(control);
                control.Dispose();
            }
            _previewOverlayControls.Clear();

            foreach (var overlay in _node?.Params?.VideoCollectionOverlayItems ?? new List<VideoCollectionOverlayItem>())
            {
                var control = CreatePreviewOverlayControl(overlay);
                if (control == null)
                {
                    continue;
                }

                _previewOverlayControls.Add(control);
                _previewHost.Controls.Add(control);
                control.BringToFront();
            }
        }

        private Control? CreatePreviewOverlayControl(VideoCollectionOverlayItem overlay)
        {
            if (_previewHost == null || _previewHost.Width <= 0 || _previewHost.Height <= 0)
            {
                return null;
            }

            var kind = WorkflowNodeParameters.NormalizeVideoCollectionOverlayKind(overlay.Kind);
            Control? control = null;
            if (string.Equals(kind, "image", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(overlay.ImagePath) || !File.Exists(overlay.ImagePath))
                {
                    return null;
                }

                var pictureBox = new PictureBox
                {
                    BackColor = Color.Transparent,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Cursor = Cursors.SizeAll,
                };
                try
                {
                    using var image = Image.FromFile(overlay.ImagePath);
                    pictureBox.Image = new Bitmap(image);
                    var width = Math.Clamp((int)Math.Round(_previewHost.ClientSize.Width * decimal.ToDouble(Math.Clamp(overlay.WidthRatio, 0.05M, 0.9M))), 36, Math.Max(36, _previewHost.ClientSize.Width));
                    var height = image.Width <= 0
                        ? width
                        : Math.Clamp((int)Math.Round(width * (image.Height / (double)image.Width)), 28, Math.Max(28, _previewHost.ClientSize.Height));
                    pictureBox.Size = new Size(width, height);
                }
                catch
                {
                    pictureBox.Dispose();
                    return null;
                }

                control = pictureBox;
            }
            else
            {
                var text = KeepSimplifiedChineseVisibleTextOnly(overlay.Text);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                var label = new Label
                {
                    BackColor = Color.FromArgb(144, 0, 0, 0),
                    ForeColor = Color.White,
                    Font = new Font("Microsoft YaHei UI", Math.Clamp(overlay.FontSize / 3F, 10F, 24F), FontStyle.Bold, GraphicsUnit.Point),
                    TextAlign = ContentAlignment.MiddleCenter,
                    AutoEllipsis = true,
                    Text = text,
                    Cursor = Cursors.SizeAll,
                };
                label.Size = new Size(Math.Max(120, (int)Math.Round(_previewHost.ClientSize.Width * 0.72)), 42);
                control = label;
            }

            PositionPreviewOverlay(control, overlay);
            AttachPreviewOverlayDrag(control, overlay);
            return control;
        }

        private void PositionPreviewOverlay(Control control, VideoCollectionOverlayItem overlay)
        {
            if (_previewHost == null || _previewHost.ClientSize.Width <= 0 || _previewHost.ClientSize.Height <= 0)
            {
                return;
            }

            var maxLeft = Math.Max(0, _previewHost.ClientSize.Width - control.Width);
            var maxTop = Math.Max(0, _previewHost.ClientSize.Height - control.Height);
            control.Left = (int)Math.Round(maxLeft * decimal.ToDouble(Math.Clamp(overlay.X, 0M, 1M)));
            control.Top = (int)Math.Round(maxTop * decimal.ToDouble(Math.Clamp(overlay.Y, 0M, 1M)));
        }

        private void AttachPreviewOverlayDrag(Control control, VideoCollectionOverlayItem overlay)
        {
            var dragging = false;
            var dragStart = Point.Empty;
            control.MouseDown += (_, e) =>
            {
                if (_busy || e.Button != MouseButtons.Left)
                {
                    return;
                }

                dragging = true;
                dragStart = e.Location;
                control.Capture = true;
            };
            control.MouseMove += (_, e) =>
            {
                if (!dragging || _previewHost == null)
                {
                    return;
                }

                var nextLeft = Math.Clamp(control.Left + e.X - dragStart.X, 0, Math.Max(0, _previewHost.ClientSize.Width - control.Width));
                var nextTop = Math.Clamp(control.Top + e.Y - dragStart.Y, 0, Math.Max(0, _previewHost.ClientSize.Height - control.Height));
                control.Left = nextLeft;
                control.Top = nextTop;
            };
            control.MouseUp += (_, _) =>
            {
                if (!dragging || _previewHost == null)
                {
                    return;
                }

                dragging = false;
                control.Capture = false;
                var maxLeft = Math.Max(1, _previewHost.ClientSize.Width - control.Width);
                var maxTop = Math.Max(1, _previewHost.ClientSize.Height - control.Height);
                overlay.X = Math.Clamp((decimal)control.Left / maxLeft, 0M, 1M);
                overlay.Y = Math.Clamp((decimal)control.Top / maxTop, 0M, 1M);
                InteractionStarted?.Invoke(this, EventArgs.Empty);
                EntryChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        private void OpenInlinePreview(string filePath)
        {
            if (_previewHost == null || _previewHost.IsDisposed || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            CloseInlinePreview();
            try
            {
                var media = new System.Windows.Controls.MediaElement
                {
                    LoadedBehavior = System.Windows.Controls.MediaState.Manual,
                    UnloadedBehavior = System.Windows.Controls.MediaState.Manual,
                    ScrubbingEnabled = true,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Source = new Uri(filePath, UriKind.Absolute),
                };

                media.MediaOpened += (_, _) =>
                {
                    if (_previewStatusLabel != null)
                    {
                        _previewStatusLabel.Visible = false;
                    }

                    try
                    {
                        media.Position = TimeSpan.Zero;
                        media.Play();
                        media.Pause();
                    }
                    catch
                    {
                    }
                };
                media.MediaFailed += (_, _) => ShowInlinePreviewUnavailable();

                var host = new ElementHost
                {
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    BackColor = Color.Black,
                    Child = media,
                };

                _inlineMediaElement = media;
                _inlineMediaHost = host;
                _previewOpened = true;
                _previewHost.Controls.Add(host);
                host.SendToBack();
                if (_previewStatusLabel != null)
                {
                    _previewStatusLabel.Visible = true;
                    _previewStatusLabel.BringToFront();
                }

                RefreshPreviewOverlays();
                return;
            }
            catch
            {
                ShowInlinePreviewUnavailable();
                return;
            }

        }

        private void PlayInlinePreview(string filePath)
        {
            if (!_previewOpened)
            {
                OpenInlinePreview(filePath);
            }

            SendPreviewCommand("play");
        }

        private void ResizeInlinePreview()
        {
            if (!_previewOpened || _previewHost == null || _previewHost.IsDisposed || _previewHost.Width <= 0 || _previewHost.Height <= 0)
            {
                return;
            }

            if (_inlineMediaHost != null)
            {
                RefreshPreviewOverlays();
                return;
            }

            mciSendString($"put {_previewAlias} window at 0 0 {_previewHost.ClientSize.Width} {_previewHost.ClientSize.Height}", null, 0, Handle);
            RefreshPreviewOverlays();
        }

        private void CloseInlinePreview()
        {
            if (_inlineMediaElement != null || _inlineMediaHost != null)
            {
                if (_inlineMediaElement != null)
                {
                    try
                    {
                        _inlineMediaElement.Stop();
                        _inlineMediaElement.Source = null;
                    }
                    catch
                    {
                    }

                    _inlineMediaElement = null;
                }

                if (_inlineMediaHost != null)
                {
                    if (_previewHost != null && !_previewHost.IsDisposed)
                    {
                        _previewHost.Controls.Remove(_inlineMediaHost);
                    }

                    _inlineMediaHost.Dispose();
                    _inlineMediaHost = null;
                }

                _previewOpened = false;
                _previewAlias = string.Empty;
                return;
            }

            if (!_previewOpened || string.IsNullOrWhiteSpace(_previewAlias))
            {
                _previewOpened = false;
                _previewAlias = string.Empty;
                return;
            }

            mciSendString($"stop {_previewAlias}", null, 0, Handle);
            mciSendString($"close {_previewAlias}", null, 0, Handle);
            _previewOpened = false;
            _previewAlias = string.Empty;
        }

        private void SendPreviewCommand(string command, string? suffix = null)
        {
            if (!_previewOpened)
            {
                return;
            }

            if (_inlineMediaElement != null)
            {
                try
                {
                    switch ((command ?? string.Empty).Trim().ToLowerInvariant())
                    {
                        case "play":
                            _inlineMediaElement.Play();
                            break;
                        case "pause":
                            _inlineMediaElement.Pause();
                            break;
                        case "stop":
                            _inlineMediaElement.Stop();
                            break;
                        case "seek":
                            if (string.Equals(suffix, "to start", StringComparison.OrdinalIgnoreCase))
                            {
                                _inlineMediaElement.Position = TimeSpan.Zero;
                            }
                            break;
                    }
                }
                catch
                {
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(_previewAlias))
            {
                return;
            }

            var fullCommand = string.IsNullOrWhiteSpace(suffix)
                ? $"{command} {_previewAlias}"
                : $"{command} {_previewAlias} {suffix}";
            mciSendString(fullCommand, null, 0, Handle);
        }

        private void ShowInlinePreviewUnavailable()
        {
            CloseInlinePreview();
            if (_previewStatusLabel != null)
            {
                _previewStatusLabel.Text = "内嵌预览不可用，可点击“放大”播放。";
                _previewStatusLabel.Visible = true;
                _previewStatusLabel.BringToFront();
            }
        }

        private static Panel CreatePanelShell(Padding padding)
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(34, 35, 40),
                Padding = padding,
                Margin = Padding.Empty,
            };
        }

        private static Panel CreateScrollableHost()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Margin = Padding.Empty,
                BackColor = Color.FromArgb(34, 35, 40),
            };
        }

        private static PictureBox CreatePreviewPictureBox(string imagePath)
        {
            var pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 19, 24),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = Padding.Empty,
            };

            try
            {
                using var image = Image.FromFile(imagePath);
                pictureBox.Image = new Bitmap(image);
            }
            catch
            {
            }

            return pictureBox;
        }

        private static Button CreateActionButton(string text, Color backColor)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                Text = text,
                BackColor = backColor,
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 6, 0),
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static void OpenFolder(string filePath)
        {
            try
            {
                var folder = File.Exists(filePath) ? Path.GetDirectoryName(filePath) : filePath;
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                }
            }
            catch
            {
            }
        }

        private void OpenVideo(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return;
            }

            try
            {
                using var form = new VideoPlaybackForm(filePath, "视频播放 - " + Path.GetFileName(filePath));
                form.ShowDialog(FindForm());
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                catch
                {
                }
            }
        }
    }
}
