using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public sealed class NodeDefaultModelSettingsForm : Form
    {
        private readonly ModelSettings _settings;
        private readonly TableLayoutPanel _layout = new();
        private bool _syncing;

        public NodeDefaultModelSettingsForm(ModelSettings settings)
        {
            _settings = settings;
            Text = "节点默认模型设置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(760, 560);
            Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.White;

            BuildLayout();
            LoadSelections();
        }

        private sealed class NodeModelPickerItem
        {
            public NodeModelPickerItem(string modelId, string displayName)
            {
                ModelId = modelId;
                DisplayName = displayName;
            }

            public string ModelId { get; }
            public string DisplayName { get; }
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(18),
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "给每类节点指定默认模型。需要两个模型的节点，会在节点详情里继续显示对应的专用模型设置。",
                ForeColor = Color.FromArgb(64, 64, 64),
                TextAlign = ContentAlignment.MiddleLeft,
            }, 0, 0);

            _layout.Dock = DockStyle.Fill;
            _layout.AutoScroll = true;
            _layout.ColumnCount = 2;
            _layout.RowCount = WorkflowNodeCatalog.ConfigurableNodeTypes.Count;
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.Controls.Add(_layout, 0, 1);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
            };
            var okButton = new Button { Text = "确定", Width = 96, Height = 32, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "取消", Width = 96, Height = 32, DialogResult = DialogResult.Cancel };
            actions.Controls.Add(okButton);
            actions.Controls.Add(cancelButton);
            root.Controls.Add(actions, 0, 2);

            Controls.Add(root);
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void LoadSelections()
        {
            _syncing = true;
            _layout.Controls.Clear();
            _layout.RowStyles.Clear();

            var row = 0;
            foreach (var nodeType in WorkflowNodeCatalog.ConfigurableNodeTypes)
            {
                _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                _layout.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill,
                    Text = nodeType,
                    TextAlign = ContentAlignment.MiddleLeft,
                }, 0, row);

                var combo = new ComboBox
                {
                    Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    DisplayMember = nameof(NodeModelPickerItem.DisplayName),
                    ValueMember = nameof(NodeModelPickerItem.ModelId),
                    DataSource = new[]
                    {
                        new NodeModelPickerItem(string.Empty, "跟随分类默认模型"),
                    }
                    .Concat(ModelConfig.GetModelsForNodeType(_settings, nodeType)
                        .Select(model => new NodeModelPickerItem(
                            ModelConfig.GetModelSelector(model),
                            $"[{ModelConfig.GetModelSourceDisplayName(model)}] {model.Name} ({model.Id})")))
                    .ToList(),
                };

                var selected = ModelConfig.GetDefaultModelForNodeType(_settings, nodeType);
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    combo.SelectedValue = selected;
                }

                combo.SelectedIndexChanged += (_, _) =>
                {
                    if (_syncing)
                    {
                        return;
                    }

                    ModelConfig.SetDefaultModelForNodeType(_settings, nodeType, combo.SelectedValue?.ToString() ?? string.Empty);
                };
                _layout.Controls.Add(combo, 1, row);
                row++;
            }

            _syncing = false;
        }
    }
}
