# JSAI Changelog

## v1.2.4 (2026-05-28) — 本地版角色名写回修复

### 概述
本次更新修复角色九宫格生成成功但未出现在对应角色表情板里的问题。原因是文本模型返回的角色 `name` 可能把“苏家主（苏父）”“林越的岳母（王氏）”简化成“苏家主”“王氏”，旧代码会把这个返回名覆盖为角色主键，刷新角色列表时生成结果被丢失。

### 主要变更
- `WorkflowRuntimeService.cs`：角色档案解析时不再用模型返回的 `name` 覆盖原始角色名；返回名不同则写入别名。
- `WorkflowRuntimeService.cs`：保留大纲/角色设计节点里的规范角色名作为资产写回主键，避免九宫格、三视图路径挂到临时简名上。

### 版本变更
- WinApp: `1.2.3` -> `1.2.4`

## v1.2.3 (2026-05-28) — 本地版视频合集内嵌预览

### 概述
本次更新修复“视频合集”节点内嵌预览不可用的问题，让本地生成的 H.264/AAC MP4 能在节点小窗中直接播放。

### 主要变更
- `VideoCollectionPanel.cs`：内嵌预览从旧版 Windows MCI `mpegvideo` 切换为 WPF `MediaElement`，保留“放大”外部播放作为兜底。
- `VideoCollectionPanel.cs`：播放、暂停、停止和回到开头按钮优先控制内嵌播放器，预览区域继续支持文字/图片叠加层刷新。
- `WinApp.csproj`：启用 WPF 支持，并补充 `GlobalUsings.cs` 以兼容原有文件/路径 API 使用方式。

### 版本变更
- WinApp: `1.2.2` -> `1.2.3`

## v1.2.2 (2026-05-28) — 本地版视频提示词精简

### 概述
本次更新优化分镜视频和直出视频提示词，减少重复文字禁令和负面提示词堆叠，让本地视频模型更集中处理主体、动作、运镜、光影和音效。

### 主要变更
- `WorkflowExecutor.cs`：分镜视频英文执行提示词改为精简全局规则，文字约束只保留一条紧凑规则；对白改为后期参考，不再每个镜头重复禁止字幕。
- `WorkflowExecutor.cs`：分镜视频负面提示词压缩为关键项，保留 `subtitles`、`captions`、`visible text`、`watermark`、`logo`、`text artifacts` 等高价值词。
- `WorkflowRuntimeService.cs`：直出视频 prompt guard 和负面提示词同样精简，避免长 prompt attention 占用和同义禁词堆叠。

### 版本变更
- WinApp: `1.2.1` -> `1.2.2`

## v1.2.1 (2026-05-27) — 本地版分镜视频声音提示与空剪辑初始化

### 概述
本次更新修复分镜视频单镜头提交时声音提示被泛化的问题，阻止 LTX 图生视频工作流中间节点把视频降到低清尺寸，并优化视频合集首次加载时的空剪辑预览状态。

### 主要变更
- `WorkflowExecutor.cs`：分镜视频英文执行提示词保留具体音效语义，将“暴雨、雷声、脚步、风声、鬼哭、呼吸、骨骼摩擦”等中文声音字段转成明确英文 SFX，并禁止未请求的背景音乐。
- `WorkflowRuntimeService.cs`：ComfyUI 图生视频工作流同步写入 `TextGenerateLTX2Prompt.prompt`，避免 LTX 工作流内部空提示或模板提示带偏生成结果；同时拦截 `ImageScaleBy.scale_by < 1` 的降采样设置，避免生成片段变成 640x352 等低清尺寸。
- `Workflowsapi/LTX-2_I2V.json`：默认 `ImageScaleBy.scale_by` 从 `0.5` 调整为 `1.0`。
- `VideoCollectionPanel.cs`：视频合集无片段/未选片段时显示“空剪辑”初始化画布和时间码，不再是一块纯黑预览框。

### 版本变更
- WinApp: `1.2.0` -> `1.2.1`

## v1.2.0 (2026-05-27) — 本地版视频合集基础剪辑

### 概述
本次更新增强“视频合集”节点，补齐基础剪辑工作流：本地素材导入、素材移出、小窗预览、文字/图片叠加轨道、音频轨道和项目保存打包。

### 主要变更
- `VideoCollectionPanel.cs`：素材列表新增“导入视频”“导入音频”和删除按钮；视频素材可自动入轨并在小窗预览。
- `VideoCollectionPanel.cs`：时间线扩展为视频、文字、图片、字幕、音频五条轨道；预览窗口支持文字/图片叠加，叠加层可拖拽调整位置。
- `WorkflowRuntimeService.cs`：生成合集时通过 ffmpeg 渲染简体中文文字叠加、图片叠加、字幕、音轨和转场。
- `WorkflowModels.cs` / `WorkflowStore.cs`：新增导入素材与叠加轨道的保存、载入、项目包资源复制。

### 版本变更
- WinApp: `1.1.0` → `1.2.0`

## v1.1.0 (2026-05-10) — Prompt 英文化 & 角色表情数据重构

### 概述
本次更新对 prompt 构建指令进行了系统性英文化，同时将分散在四个图像生成路径中的男女九宫格表情数据统一为单一数据源，消除硬编码重复。

---

### 一、Prompt 指令英文化

#### WorkflowPromptBuilder.cs
- `BuildOutlinePrompt` 头部系统指令 → 英文
- `BuildScriptPrompt` 头部系统指令 → 英文
- `BuildCharacterDescriptionPrompt` 指令 → 英文
- `BuildStoryboardBreakdownPrompt` 指令 + shotSize/cameraAngle/cameraMovement 枚举值 → 英文
- `BuildCreativeDescriptionStoryboardPrompt` 指令 → 英文
- `BuildScriptReadableOutlineContext` 章节剧情指令文本 → 英文
- **保留中文**：format template 中的中文格式标记（下游解析器依赖双语标记，如 `# 剧名 (Title)`、`## 主要人物小传` 等）

#### WorkflowExecutor.cs
- `BuildStoryboardFallbackPrompt` 完整英文化（指令、示例值、规则、标签）
- 10 个 UI 状态/摘要消息翻译为英文（Outline/Script/CharacterDescription/StoryboardBreakdown/CharacterView/StoryboardImage/StoryboardVideo/VideoCollection/LocalAsset/DirectStudio）
- `shotSize`/`cameraAngle`/`cameraMovement` 翻译表 → 英文键值
- **保留中文**：正则匹配模式（`手机界面`、`写着`、`二维码` 等中文字符集）、LLM 输出解析标记（`已生成短剧故事大纲` 等下游依赖双语标记）

#### WorkflowRuntimeService.cs
- `BuildDirectImagePromptRequest` — 直出工作区文生图 prompt 模板 → 英文
- `BuildDirectImageNegativePromptRequest` — 直出工作区文生图负向提示词 → 英文
- `BuildDirectVideoPromptRequest` — 直出工作区文生视频 prompt 模板 → 英文
- `BuildDirectVideoNegativePromptRequest` — 直出工作区文生视频负向提示词 → 英文
- 13 个短标签翻译：`文生图→Image Generation`、`正向提示词→Positive Prompt` 等
- **保留中文**：内容生成模板中的角色名/剧情段落（如 `沈知微` 等中文短剧输出）、狗的品种检测关键字（`狗`/`犬`/`黑背`）、LLM 输出双语标记

---

### 二、角色表情数据统一重构

#### 新建文件：`CharacterPromptTextBuilder.cs`
```
WinApp/CharacterPromptTextBuilder.cs
```

| 组件 | 说明 |
|---|---|
| `CharacterGenderHint` 枚举 | Male / Female / Unknown |
| `FemaleCells[9]` | 平静/微笑/生气/惊讶/伤心/大笑/思考/闭眼微笑/鼓嘴 |
| `MaleCells[9]` | 平静/微笑/生气/惊讶/伤感/大笑/思考/闭眼浅笑/不屑 |
| `DetectGender(entry)` | 基于中英文关键字（男/女/male/female/他/她/prince/princess 等）自动识别角色性别 |
| `GetDetailedExpressionPrompts(entry)` | → ComfyUI / SD 用 9 条详细 img2img prompt |
| `BuildGptImageExpressionList(entry)` | → GPT-image 用短英文逗号列表 |
| `BuildChineseExpressionPrompt(entry)` | → Gemini 用中文 3×3 九宫格描述 |
| `BuildChineseThreeViewPrompt(entry)` | → Gemini 用中文三视图描述 |
| `BuildChinesePromptBundle(entry)` | → 资产导出用「表情九宫格 + 三视图」中文合集 |

#### 修改的方法（WorkflowRuntimeService.cs）
- `GenerateComfyUiCharacterExpressionSheetAsync` — 硬编码 `string[9]` → `GetDetailedExpressionPrompts(entry)`
- `GenerateStableDiffusionCharacterExpressionSheetAsync` — 同上
- `BuildGptImageExpressionSheetPrompt` — 硬编码英文列表 → `BuildGptImageExpressionList(entry)`

#### 四路径对齐
| 路径 | 数据源 | 形式 |
|---|---|---|
| ComfyUI | `GetDetailedExpressionPrompts` | 9 个独立图片，逐格 img2img |
| Stable Diffusion | 同上 | 同上 |
| Gemini (云端) | `BuildChineseExpressionPrompt` | 单图复合 3×3 九宫格 |
| GPT-image | `BuildGptImageExpressionList` | 英文单图复合 3×3 九宫格 |

---

### 三、版本变更
- WinApp: `1.0.0` → `1.1.0`
- 其他项目（UpdateServer/Updater/ClientConfigurator 等）无版本号，未变更

---

### 四、已知技术债（未处理）
参见 `docs/项目维护说明.md` 中的技术债清单：
- ComfyUI workflow 映射可配置化
- 云雾适配器独立
- `WorkflowRuntimeService.cs` 拆分（文件过大）
- `.myai` 文件增加版本字段
- 视频合集轨道编辑器升级
