using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public partial class MainForm
    {
        private async Task RunCharacterDesignActionWorkflowAsync(WorkflowNode node, string characterName, CharacterDesignActionType action)
        {
            if (_running)
            {
                return;
            }

            _canvas.SetNodeBusy(node.Id, true);
            using var processingScope = BeginProcessingScope($"正在生成角色内容：{characterName}");
            try
            {
                var entry = node.Params?.CharacterEntries?.FirstOrDefault(item =>
                    string.Equals(item.Name, characterName, StringComparison.OrdinalIgnoreCase));
                if (entry != null)
                {
                    entry.LastError = string.Empty;
                    switch (action)
                    {
                        case CharacterDesignActionType.GenerateProfile:
                            entry.ProfileStatus = CharacterAssetStatus.Generating;
                            break;
                        case CharacterDesignActionType.GenerateExpression:
                            entry.ExpressionStatus = CharacterAssetStatus.Generating;
                            break;
                        case CharacterDesignActionType.GenerateThreeView:
                            entry.ThreeViewStatus = CharacterAssetStatus.Generating;
                            break;
                    }

                    node.Output = WorkflowExecutor.BuildCharacterDesignOutput(node);
                    _canvas.RefreshNode(node.Id);
                    await Task.Yield();
                }

                var actionLabel = action switch
                {
                    CharacterDesignActionType.GenerateProfile => "角色档案",
                    CharacterDesignActionType.GenerateExpression => "九宫格",
                    CharacterDesignActionType.GenerateThreeView => "三视图",
                    _ => "角色内容",
                };

                UpdateStatus($"正在生成角色{actionLabel}：{characterName}", Color.FromArgb(90, 176, 255));
                SetProcessingDetail($"正在生成角色{actionLabel}：{characterName}");
                await _runtimeService.ExecuteCharacterDesignActionAsync(_document, node, characterName, action, CancellationToken.None);
                _canvas.RefreshNode(node.Id);
                if (_selectedNode?.Id == node.Id)
                {
                    SelectNode(node);
                }

                SaveWorkingCopy();
                UpdateStatus($"角色{actionLabel}已完成：{characterName}", Color.FromArgb(90, 176, 255));
            }
            catch (Exception ex)
            {
                _canvas.RefreshNode(node.Id);
                if (_selectedNode?.Id == node.Id)
                {
                    SelectNode(node);
                }

                UpdateStatus($"角色设计执行失败：{ex.Message}", Color.DarkOrange);
                MessageBox.Show(this, ex.Message, "执行失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _canvas.SetNodeBusy(node.Id, false);
            }
        }
    }
}
