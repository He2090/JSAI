using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class WorkflowCanvasControl : Panel
    {
        private const float EdgeHitTolerance = 10F;
        private const float DeleteHandleRadius = 8F;
        private const float GridSize = 32F;
        private const float MinZoom = 0.9F;
        private const float MaxZoom = 1.6F;
        private readonly Dictionary<string, WorkflowNodeCard> _cards = new();
        private WorkflowDocument? _document;
        private string? _selectedNodeId;
        private WorkflowEdge? _hoveredEdge;
        private WorkflowEdge? _selectedEdge;
        private (string NodeId, WorkflowPortKind PortKind)? _pendingPort;
        private Point? _pendingMouse;
        private PointF _pan;
        private PointF _panOrigin;
        private Point _canvasDragOrigin;
        private bool _draggingCanvas;
        private float _zoom = 1F;

        public WorkflowCanvasControl()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(17, 17, 17);
            TabStop = true;
            SetStyle(
                ControlStyles.ResizeRedraw |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);
            UpdateStyles();
        }

        public event EventHandler? DocumentChanged;
        public event EventHandler<WorkflowNodeEventArgs>? NodeRunRequested;
        public event EventHandler<WorkflowCharacterActionEventArgs>? NodeCharacterActionRequested;
        public event EventHandler<WorkflowNodeActionEventArgs>? NodeActionRequested;
        public event EventHandler<WorkflowNodeEventArgs>? SelectedNodeChanged;
        public event EventHandler<WorkflowStatusEventArgs>? StatusChanged;

        public float ZoomFactor => _zoom;

        public PointF PanOffset => _pan;

        public void SetDocument(WorkflowDocument document)
        {
            _document = document;
            _pendingMouse = null;
            if (_pendingPort.HasValue && _document.Nodes.All(node => node.Id != _pendingPort.Value.NodeId))
            {
                _pendingPort = null;
            }

            if (_hoveredEdge != null && !_document.Edges.Contains(_hoveredEdge))
            {
                _hoveredEdge = null;
            }

            if (_selectedEdge != null && !_document.Edges.Contains(_selectedEdge))
            {
                _selectedEdge = null;
            }

            RebuildCards();
        }

        public void SetViewState(PointF pan, float zoom)
        {
            _pan = pan;
            _zoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));
            RefreshViewport();
        }

        public Point GetSuggestedNodeLocation(int index, string? nodeType = null)
        {
            var cardSize = WorkflowNodeCard.GetCardSize(nodeType ?? string.Empty);
            var width = Math.Max(ClientSize.Width, 900);
            var height = Math.Max(ClientSize.Height, 600);
            var stagger = index % 6;
            var centerWorldX = ((width / 2F) - _pan.X) / _zoom;
            var centerWorldY = ((height / 2F) - _pan.Y) / _zoom;
            return new Point(
                (int)Math.Round(centerWorldX - (cardSize.Width / 2F) + (stagger * 28F)),
                (int)Math.Round(centerWorldY - (cardSize.Height / 2F) + (stagger * 20F)));
        }

        public void CancelPendingConnection(string? message = null)
        {
            if (!_pendingPort.HasValue)
            {
                return;
            }

            _pendingPort = null;
            _pendingMouse = null;
            RefreshCardStates();
            RaiseStatus(message ?? "已取消当前连线选择。", Color.DarkOrange);
        }

        public void RefreshNode(string nodeId)
        {
            if (_cards.TryGetValue(nodeId, out var card))
            {
                SyncCardBindings(card);
                card.SyncFromNode();
                RefreshViewport();
                Invalidate();
            }
        }

        public void SetNodeBusy(string nodeId, bool busy)
        {
            if (_cards.TryGetValue(nodeId, out var card))
            {
                card.SetBusy(busy);
                Invalidate();
            }
        }

        public void RefreshAllNodes()
        {
            var changed = false;
            foreach (var card in _cards.Values)
            {
                changed |= SyncCardBindings(card);
                card.SyncFromNode();
            }

            RefreshViewport();
            Invalidate();
            if (changed)
            {
                DocumentChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void ZoomAt(Point clientLocation, int mouseWheelDelta)
        {
            var nextZoom = _zoom + (mouseWheelDelta > 0 ? 0.1F : -0.1F);
            nextZoom = Math.Max(MinZoom, Math.Min(MaxZoom, (float)Math.Round(nextZoom, 2)));
            if (Math.Abs(nextZoom - _zoom) < 0.001F)
            {
                return;
            }

            var worldX = (clientLocation.X - _pan.X) / _zoom;
            var worldY = (clientLocation.Y - _pan.Y) / _zoom;
            _zoom = nextZoom;
            _pan = new PointF(
                clientLocation.X - (worldX * _zoom),
                clientLocation.Y - (worldY * _zoom));

            RefreshViewport();
            Invalidate();
            RaiseStatus($"画布缩放：{Math.Round(_zoom * 100)}%", Color.FromArgb(74, 161, 255));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (_pendingPort.HasValue)
            {
                CancelPendingConnection();
                return;
            }

            if (TryRemoveConnectionFromHandles(e.Location))
            {
                return;
            }

            var hitEdge = HitTestEdge(e.Location);
            if (!ReferenceEquals(hitEdge, _selectedEdge))
            {
                _selectedEdge = hitEdge;
                Invalidate();
            }

            if (hitEdge != null)
            {
                RaiseStatus("已选中连线，点击连线两端的红色删除点即可移除。", Color.DarkOrange);
                return;
            }

            if (_selectedNodeId != null)
            {
                _selectedNodeId = null;
                RefreshCardStates();
                SelectedNodeChanged?.Invoke(this, new WorkflowNodeEventArgs(null));
            }

            _selectedEdge = null;
            _draggingCanvas = true;
            _canvasDragOrigin = e.Location;
            _panOrigin = _pan;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_draggingCanvas)
            {
                _pan = new PointF(
                    _panOrigin.X + (e.X - _canvasDragOrigin.X),
                    _panOrigin.Y + (e.Y - _canvasDragOrigin.Y));
                RefreshViewport();
                Invalidate();
                return;
            }

            var hoveredEdge = HitTestEdge(e.Location);
            if (!ReferenceEquals(hoveredEdge, _hoveredEdge))
            {
                _hoveredEdge = hoveredEdge;
                Invalidate();
            }

            if (!_pendingPort.HasValue)
            {
                return;
            }

            _pendingMouse = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_draggingCanvas)
            {
                return;
            }

            _draggingCanvas = false;
            Capture = false;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_draggingCanvas)
            {
                return;
            }

            if (_hoveredEdge != null)
            {
                _hoveredEdge = null;
                Invalidate();
            }

            if (_pendingPort.HasValue)
            {
                _pendingMouse = null;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ZoomAt(e.Location, e.Delta);
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var gridPen = new Pen(Color.FromArgb(28, 28, 28), 1F);
            var grid = Math.Max(14F, GridSize * _zoom);
            var startX = _pan.X % grid;
            var startY = _pan.Y % grid;
            if (startX < 0F)
            {
                startX += grid;
            }

            if (startY < 0F)
            {
                startY += grid;
            }

            for (var x = startX; x < Width; x += grid)
            {
                e.Graphics.DrawLine(gridPen, x, 0, x, Height);
            }

            for (var y = startY; y < Height; y += grid)
            {
                e.Graphics.DrawLine(gridPen, 0, y, Width, y);
            }

            if (_document == null || _document.Nodes.Count == 0)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    "现在可以从左侧“节点列表”创建节点。\r\n按住节点左键拖动节点，按住画布空白处左键拖动画布，滚轮可缩放画布。",
                    Font,
                    new Rectangle(24, 24, Width - 48, 72),
                    Color.FromArgb(180, 180, 180),
                    TextFormatFlags.Left | TextFormatFlags.Top);
                return;
            }

            foreach (var edge in _document.Edges)
            {
                if (!_cards.TryGetValue(edge.From, out var fromCard) || !_cards.TryGetValue(edge.To, out var toCard))
                {
                    continue;
                }

                var geometry = BuildConnectionGeometry(
                    fromCard.GetPortCenter(WorkflowPortKind.Output),
                    toCard.GetPortCenter(WorkflowPortKind.Input));
                var isActive = ReferenceEquals(edge, _hoveredEdge) || ReferenceEquals(edge, _selectedEdge);
                using var edgePen = new Pen(
                    isActive ? Color.FromArgb(255, 170, 46) : Color.FromArgb(74, 161, 255),
                    isActive ? 2.8F : 2.2F);
                DrawConnection(e.Graphics, edgePen, geometry);

                if (isActive)
                {
                    DrawDeleteHandle(e.Graphics, geometry.DeleteHandleStart);
                    DrawDeleteHandle(e.Graphics, geometry.DeleteHandleEnd);
                }
            }

            if (_pendingPort.HasValue && _cards.TryGetValue(_pendingPort.Value.NodeId, out var pendingCard))
            {
                using var pendingPen = new Pen(Color.FromArgb(255, 170, 46), 2F) { DashStyle = DashStyle.Dash };
                var start = pendingCard.GetPortCenter(_pendingPort.Value.PortKind);
                var end = _pendingMouse ?? (_pendingPort.Value.PortKind == WorkflowPortKind.Output
                    ? new Point(Math.Min(Width - 60, start.X + 160), start.Y)
                    : new Point(Math.Max(60, start.X - 160), start.Y));
                DrawConnection(e.Graphics, pendingPen, BuildConnectionGeometry(start, end));
            }
        }

        private void RebuildCards()
        {
            SuspendLayout();
            Controls.Clear();
            _cards.Clear();

            if (_document != null)
            {
                foreach (var node in _document.Nodes)
                {
                    var card = new WorkflowNodeCard(node);
                    card.ProjectName = _document.ProjectName;
                    card.Document = _document;
                    card.SelectRequested += Card_SelectRequested;
                    card.RunRequested += Card_RunRequested;
                    card.DeleteRequested += Card_DeleteRequested;
                    card.PositionChanged += Card_PositionChanged;
                    card.MoveCompleted += (_, _) => DocumentChanged?.Invoke(this, EventArgs.Empty);
                    card.NodeChanged += Card_NodeChanged;
                    card.PortClicked += Card_PortClicked;
                    card.CharacterActionRequested += Card_CharacterActionRequested;
                    card.NodeActionRequested += Card_NodeActionRequested;
                    _cards[node.Id] = card;
                    SyncCardBindings(card);
                    Controls.Add(card);
                }
            }

            ResumeLayout();
            RefreshViewport();
            RefreshCardStates();
            Invalidate();
        }

        private void Card_SelectRequested(object? sender, EventArgs e)
        {
            if (sender is not WorkflowNodeCard card)
            {
                return;
            }

            _selectedNodeId = card.Node.Id;
            _selectedEdge = null;
            RefreshCardStates();
            SelectedNodeChanged?.Invoke(this, new WorkflowNodeEventArgs(card.Node));
        }

        private void Card_RunRequested(object? sender, EventArgs e)
        {
            if (sender is WorkflowNodeCard card)
            {
                NodeRunRequested?.Invoke(this, new WorkflowNodeEventArgs(card.Node));
            }
        }

        private void Card_CharacterActionRequested(object? sender, WorkflowCharacterActionEventArgs e)
        {
            _selectedNodeId = e.Node.Id;
            _selectedEdge = null;
            RefreshCardStates();
            SelectedNodeChanged?.Invoke(this, new WorkflowNodeEventArgs(e.Node));
            NodeCharacterActionRequested?.Invoke(this, e);
        }

        private void Card_NodeActionRequested(object? sender, WorkflowNodeActionEventArgs e)
        {
            _selectedNodeId = e.Node.Id;
            _selectedEdge = null;
            RefreshCardStates();
            SelectedNodeChanged?.Invoke(this, new WorkflowNodeEventArgs(e.Node));
            NodeActionRequested?.Invoke(this, e);
        }

        private void Card_PositionChanged(object? sender, EventArgs e)
        {
            RefreshViewport();
            Invalidate();

            if (sender is WorkflowNodeCard card && card.Node.Id == _selectedNodeId)
            {
                SelectedNodeChanged?.Invoke(this, new WorkflowNodeEventArgs(card.Node));
            }
        }

        private void Card_NodeChanged(object? sender, EventArgs e)
        {
            if (sender is WorkflowNodeCard card && card.Node.Id == _selectedNodeId)
            {
                SelectedNodeChanged?.Invoke(this, new WorkflowNodeEventArgs(card.Node));
            }

            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Card_DeleteRequested(object? sender, EventArgs e)
        {
            if (_document == null || sender is not WorkflowNodeCard card)
            {
                return;
            }

            var result = MessageBox.Show(this, $"确认删除节点“{card.Node.Id} ({card.Node.Type})”吗？", "删除节点", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
            {
                return;
            }

            _document.Nodes.Remove(card.Node);
            _document.Edges.RemoveAll(edge => edge.From == card.Node.Id || edge.To == card.Node.Id);
            if (_selectedNodeId == card.Node.Id)
            {
                _selectedNodeId = null;
                SelectedNodeChanged?.Invoke(this, new WorkflowNodeEventArgs(null));
            }

            if (_pendingPort.HasValue && _pendingPort.Value.NodeId == card.Node.Id)
            {
                _pendingPort = null;
            }

            if (_selectedEdge != null && (_selectedEdge.From == card.Node.Id || _selectedEdge.To == card.Node.Id))
            {
                _selectedEdge = null;
            }

            if (_hoveredEdge != null && (_hoveredEdge.From == card.Node.Id || _hoveredEdge.To == card.Node.Id))
            {
                _hoveredEdge = null;
            }

            RebuildCards();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            RaiseStatus("已删除节点，相关连线已自动清理。", Color.DarkOrange);
        }

        private void Card_PortClicked(object? sender, WorkflowPortEventArgs e)
        {
            if (_document == null)
            {
                return;
            }

            _selectedNodeId = e.Node.Id;
            _selectedEdge = null;
            RefreshCardStates();
            SelectedNodeChanged?.Invoke(this, new WorkflowNodeEventArgs(e.Node));

            if (!_pendingPort.HasValue)
            {
                _pendingPort = (e.Node.Id, e.PortKind);
                _pendingMouse = null;
                RefreshCardStates();
                RaiseStatus(BuildPendingMessage(e.Node, e.PortKind), Color.FromArgb(74, 161, 255));
                return;
            }

            var pending = _pendingPort.Value;
            if (pending.NodeId == e.Node.Id && pending.PortKind == e.PortKind)
            {
                CancelPendingConnection();
                return;
            }

            if (pending.PortKind == e.PortKind)
            {
                _pendingPort = (e.Node.Id, e.PortKind);
                _pendingMouse = null;
                RefreshCardStates();
                RaiseStatus(BuildPendingMessage(e.Node, e.PortKind), Color.DarkOrange);
                return;
            }

            var fromId = pending.PortKind == WorkflowPortKind.Output ? pending.NodeId : e.Node.Id;
            var toId = pending.PortKind == WorkflowPortKind.Input ? pending.NodeId : e.Node.Id;
            var fromNode = _document.Nodes.FirstOrDefault(node => string.Equals(node.Id, fromId, StringComparison.OrdinalIgnoreCase));
            var toNode = _document.Nodes.FirstOrDefault(node => string.Equals(node.Id, toId, StringComparison.OrdinalIgnoreCase));

            if (fromId == toId)
            {
                RaiseStatus("同一个节点不能连接到自己。", Color.IndianRed);
                return;
            }

            if (fromNode == null || toNode == null)
            {
                RaiseStatus("当前连线目标节点不存在，请重试。", Color.IndianRed);
                CancelPendingConnection();
                return;
            }

            if (!WorkflowNodeCatalog.IsAllowedConnection(fromNode.Type, toNode.Type))
            {
                _pendingPort = null;
                _pendingMouse = null;
                RefreshCardStates();
                RaiseStatus(WorkflowNodeCatalog.DescribeAllowedTargets(fromNode.Type), Color.IndianRed);
                return;
            }

            if (_document.Edges.Any(edge => edge.From == fromId && edge.To == toId))
            {
                _pendingPort = null;
                _pendingMouse = null;
                RefreshCardStates();
                RaiseStatus("这条连线已经存在。", Color.DarkOrange);
                return;
            }

            _document.Edges.Add(new WorkflowEdge
            {
                From = fromId,
                To = toId,
            });

            _pendingPort = null;
            _pendingMouse = null;
            RefreshCardStates();
            Invalidate();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            RaiseStatus($"已创建连线：{DescribeNode(fromId)} -> {DescribeNode(toId)}。", Color.FromArgb(74, 161, 255));
        }

        private void RefreshCardStates()
        {
            foreach (var pair in _cards)
            {
                pair.Value.Selected = pair.Key == _selectedNodeId;
                pair.Value.PendingPortKind = _pendingPort.HasValue && _pendingPort.Value.NodeId == pair.Key
                    ? _pendingPort.Value.PortKind
                    : null;
            }
        }

        private bool SyncCardBindings(WorkflowNodeCard card)
        {
            if (_document == null)
            {
                return false;
            }

            if (card.Node.Type == WorkflowNodeCatalog.Script)
            {
                card.ProjectName = _document.ProjectName;
                card.Document = _document;
                var outlineNodes = WorkflowExecutor.CollectUpstreamNodes(_document, card.Node)
                    .Where(node => node.Type == WorkflowNodeCatalog.Outline && !string.IsNullOrWhiteSpace(node.Output))
                    .ToList();
                var outlineText = string.Join(
                    Environment.NewLine + Environment.NewLine,
                    outlineNodes.Select(node => node.Output.Trim()));
                var totalEpisodes = outlineNodes
                    .Select(node => node.Params?.Episodes ?? 0)
                    .DefaultIfEmpty(0)
                    .Max();

                var chapters = WorkflowExecutor.ExtractOutlineChapters(outlineText, 24, totalEpisodes);
                card.SetScriptChapterOptions(chapters);
                return false;
            }

            if (card.Node.Type == WorkflowNodeCatalog.StoryboardImage)
            {
                card.ProjectName = _document.ProjectName;
                card.Document = _document;
                var connectionCount = _document.Edges.Count(edge => string.Equals(edge.To, card.Node.Id, StringComparison.Ordinal));
                card.SetStoryboardConnectionCount(connectionCount);
                return false;
            }

            if (card.Node.Type == WorkflowNodeCatalog.StoryboardBreakdown)
            {
                card.ProjectName = _document.ProjectName;
                card.Document = _document;
                card.Node.Params ??= new WorkflowNodeParameters();
                card.Node.Params.EnsureDefaults(card.Node.Type);
                if (card.Node.Params.StoryboardShots.Count == 0 && !string.IsNullOrWhiteSpace(card.Node.Output))
                {
                    card.Node.Params.StoryboardShots = WorkflowExecutor.ParseStoryboardShots(card.Node.Output);
                    return card.Node.Params.StoryboardShots.Count > 0;
                }

                return false;
            }

            if (card.Node.Type == WorkflowNodeCatalog.CharacterView ||
                card.Node.Type == WorkflowNodeCatalog.CharacterDescription)
            {
                card.ProjectName = _document.ProjectName;
                card.Document = _document;
                var upstreamNodes = WorkflowExecutor.CollectUpstreamNodes(_document, card.Node);
                var outlineText = string.Join(
                    Environment.NewLine + Environment.NewLine,
                    upstreamNodes
                        .Where(node => node.Type == WorkflowNodeCatalog.Outline && !string.IsNullOrWhiteSpace(node.Output))
                        .Select(node => node.Output.Trim()));
                var characterText = string.Join(
                    Environment.NewLine + Environment.NewLine,
                    upstreamNodes
                        .Where(node => node.Type == WorkflowNodeCatalog.CharacterDescription && !string.IsNullOrWhiteSpace(node.Output))
                        .Select(node => node.Output.Trim()));
                return WorkflowExecutor.SyncCharacterDesignEntries(card.Node, outlineText, characterText, 16);
            }

            card.ProjectName = _document.ProjectName;
            card.Document = _document;
            return false;
        }

        private void RefreshViewport()
        {
            SuspendLayout();
            try
            {
                foreach (var card in _cards.Values)
                {
                    card.ApplyZoom(_zoom);
                    var nextLocation = new Point(
                        (int)Math.Round(_pan.X + (card.Node.X * _zoom)),
                        (int)Math.Round(_pan.Y + (card.Node.Y * _zoom)));

                    if (card.Location != nextLocation)
                    {
                        card.Location = nextLocation;
                    }
                }
            }
            finally
            {
                ResumeLayout();
            }

            Invalidate();
        }

        private static void DrawConnection(Graphics graphics, Pen pen, ConnectionGeometry geometry)
        {
            graphics.DrawBezier(pen, geometry.Start, geometry.Control1, geometry.Control2, geometry.End);
        }

        private static void DrawDeleteHandle(Graphics graphics, PointF center)
        {
            var rect = new RectangleF(
                center.X - DeleteHandleRadius,
                center.Y - DeleteHandleRadius,
                DeleteHandleRadius * 2,
                DeleteHandleRadius * 2);
            using var fillBrush = new SolidBrush(Color.FromArgb(226, 168, 48, 56));
            using var borderPen = new Pen(Color.FromArgb(255, 248, 113, 113), 1.2F);
            using var linePen = new Pen(Color.WhiteSmoke, 1.5F);

            graphics.FillEllipse(fillBrush, rect);
            graphics.DrawEllipse(borderPen, rect);
            graphics.DrawLine(linePen, center.X - 3, center.Y - 3, center.X + 3, center.Y + 3);
            graphics.DrawLine(linePen, center.X - 3, center.Y + 3, center.X + 3, center.Y - 3);
        }

        private ConnectionGeometry BuildConnectionGeometry(Point start, Point end)
        {
            var controlOffset = Math.Max(60, Math.Abs(end.X - start.X) / 2);
            var control1 = new Point(start.X + controlOffset, start.Y);
            var control2 = new Point(end.X - controlOffset, end.Y);
            return new ConnectionGeometry(
                start,
                control1,
                control2,
                end,
                EvaluateBezier(start, control1, control2, end, 0.12F),
                EvaluateBezier(start, control1, control2, end, 0.88F));
        }

        private WorkflowEdge? HitTestEdge(Point location)
        {
            if (_document == null)
            {
                return null;
            }

            WorkflowEdge? bestEdge = null;
            var bestDistance = float.MaxValue;

            foreach (var edge in _document.Edges)
            {
                if (!TryBuildConnectionGeometry(edge, out var geometry))
                {
                    continue;
                }

                var distance = GetDistanceToBezier(location, geometry);
                if (distance <= EdgeHitTolerance && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestEdge = edge;
                }
            }

            return bestEdge;
        }

        private bool TryRemoveConnectionFromHandles(Point location)
        {
            var targetEdge = _selectedEdge ?? _hoveredEdge;
            if (targetEdge == null || !TryBuildConnectionGeometry(targetEdge, out var geometry))
            {
                return false;
            }

            if (!IsPointInsideHandle(location, geometry.DeleteHandleStart) &&
                !IsPointInsideHandle(location, geometry.DeleteHandleEnd))
            {
                return false;
            }

            if (_document == null)
            {
                return false;
            }

            _document.Edges.Remove(targetEdge);
            _selectedEdge = null;
            _hoveredEdge = null;
            Invalidate();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            RaiseStatus($"已删除连线：{DescribeNode(targetEdge.From)} -> {DescribeNode(targetEdge.To)}。", Color.DarkOrange);
            return true;
        }

        private bool TryBuildConnectionGeometry(WorkflowEdge edge, out ConnectionGeometry geometry)
        {
            geometry = default;
            if (!_cards.TryGetValue(edge.From, out var fromCard) || !_cards.TryGetValue(edge.To, out var toCard))
            {
                return false;
            }

            geometry = BuildConnectionGeometry(
                fromCard.GetPortCenter(WorkflowPortKind.Output),
                toCard.GetPortCenter(WorkflowPortKind.Input));
            return true;
        }

        private static bool IsPointInsideHandle(Point location, PointF center)
        {
            var dx = location.X - center.X;
            var dy = location.Y - center.Y;
            return (dx * dx) + (dy * dy) <= DeleteHandleRadius * DeleteHandleRadius;
        }

        private static float GetDistanceToBezier(Point location, ConnectionGeometry geometry)
        {
            using var path = new GraphicsPath();
            path.AddBezier(geometry.Start, geometry.Control1, geometry.Control2, geometry.End);
            path.Flatten();

            var points = path.PathPoints;
            if (points.Length == 0)
            {
                return float.MaxValue;
            }

            var bestDistance = float.MaxValue;
            for (var index = 1; index < points.Length; index++)
            {
                var distance = DistanceToSegment(location, points[index - 1], points[index]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                }
            }

            return bestDistance;
        }

        private static float DistanceToSegment(Point point, PointF segmentStart, PointF segmentEnd)
        {
            var dx = segmentEnd.X - segmentStart.X;
            var dy = segmentEnd.Y - segmentStart.Y;
            if (Math.Abs(dx) < 0.001F && Math.Abs(dy) < 0.001F)
            {
                return Distance(point, segmentStart);
            }

            var t = ((point.X - segmentStart.X) * dx + (point.Y - segmentStart.Y) * dy) / ((dx * dx) + (dy * dy));
            t = Math.Max(0F, Math.Min(1F, t));
            var projection = new PointF(segmentStart.X + (t * dx), segmentStart.Y + (t * dy));
            return Distance(point, projection);
        }

        private static float Distance(Point point, PointF target)
        {
            var dx = point.X - target.X;
            var dy = point.Y - target.Y;
            return (float)Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static PointF EvaluateBezier(Point start, Point control1, Point control2, Point end, float t)
        {
            var u = 1F - t;
            var x =
                (u * u * u * start.X) +
                (3F * u * u * t * control1.X) +
                (3F * u * t * t * control2.X) +
                (t * t * t * end.X);
            var y =
                (u * u * u * start.Y) +
                (3F * u * u * t * control1.Y) +
                (3F * u * t * t * control2.Y) +
                (t * t * t * end.Y);
            return new PointF(x, y);
        }

        private string BuildPendingMessage(WorkflowNode node, WorkflowPortKind portKind)
        {
            return portKind == WorkflowPortKind.Output
                ? $"已选择输出点：{node.Id} ({node.Type})，请点击目标节点左侧输入点完成连线。"
                : $"已选择输入点：{node.Id} ({node.Type})，请点击来源节点右侧输出点完成连线。";
        }

        private string DescribeNode(string nodeId)
        {
            if (_document == null)
            {
                return nodeId;
            }

            var node = _document.Nodes.FirstOrDefault(item => item.Id == nodeId);
            return node == null ? nodeId : $"{node.Id} ({node.Type})";
        }

        private void RaiseStatus(string message, Color color)
        {
            StatusChanged?.Invoke(this, new WorkflowStatusEventArgs(message, color));
        }

        private readonly struct ConnectionGeometry
        {
            public ConnectionGeometry(
                Point start,
                Point control1,
                Point control2,
                Point end,
                PointF deleteHandleStart,
                PointF deleteHandleEnd)
            {
                Start = start;
                Control1 = control1;
                Control2 = control2;
                End = end;
                DeleteHandleStart = deleteHandleStart;
                DeleteHandleEnd = deleteHandleEnd;
            }

            public Point Start { get; }

            public Point Control1 { get; }

            public Point Control2 { get; }

            public Point End { get; }

            public PointF DeleteHandleStart { get; }

            public PointF DeleteHandleEnd { get; }
        }
    }
}
