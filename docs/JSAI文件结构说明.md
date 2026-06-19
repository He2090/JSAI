# JSAI 极速漫剧生成器 — 文件夹文件结构说明

> 自动生成于 2026-05-08  
> 项目根目录：E:\myJSAI\JSAI\

---

## 一、项目总览

JSAI（极速漫剧生成器）是一个 AI 驱动的影视内容生成桌面应用。基于 .NET 8 开发，采用 WinForms 桌面客户端 + ASP.NET 服务端架构，集成 ComfyUI 等本地/云端 AI 模型接口。

### 解决方案项目构成

| 项目 | 路径 | 描述 |
|------|------|------|
| WinApp | WinApp\WinApp.csproj | 桌面客户端主程序（WinForms） |
| UpdateServer | UpdateServer\UpdateServer.csproj | 服务端（会员/版本/后台管理） |
| Updater | Updater\Updater.csproj | 客户端自动更新器 |
| ClientConfigurator | ClientConfigurator\ClientConfigurator.csproj | 发布前服务器地址配置工具 |
| ClientInstaller | ClientInstaller\ClientInstaller.csproj | 客户端安装器 |
| ServerInstaller | ServerInstaller\ServerInstaller.csproj | 服务端安装器（仅项目文件） |

---

## 二、根目录文件

| 文件 | 说明 |
|------|------|
| JSAI.sln | Visual Studio 解决方案文件 |
| dmin-config.json | 管理员默认账号配置 |
| index.html | 旧版 WebView 入口（已废弃，保留作参考） |
| Get_imageAI.json | ComfyUI 文生图 workflow 模板副本 |
| 	emp-keygen.cs | 临时密钥生成测试代码 |
| 	emp_comfy_test.png | ComfyUI 测试输出图片 |
| _tmp_projectlaunch_view.txt | 临时调试日志 |
| _updater_test_run.ps1 | 更新器测试脚本 |

---

## 三、WinApp（桌面客户端主项目）

### 3.1 程序入口与主窗体

| 文件 | 说明 |
|------|------|
| Program.cs | 应用程序入口点 |
| MainForm.cs | 主窗体核心逻辑（构造函数、初始化） |
| MainForm.Designer.cs | 主窗体设计器自动生成代码 |
| MainForm.Layout.cs | 主布局：左侧栏、画布、右侧详情面板 |
| MainForm.Toolbar.cs | 顶部工具栏按钮（新建/保存/导入/导出/模型设置） |
| MainForm.ProjectLaunch.cs | 项目启动、新建、载入入口逻辑 |
| MainForm.ProjectSessions.cs | 多项目标签页管理 |
| MainForm.WorkspaceModes.cs | AI 漫剧 / 直出工作区模式切换 |
| MainForm.Workflow.cs | 节点运行、选择、右侧检查器刷新 |
| MainForm.CharacterDesign.cs | 角色设计节点事件处理 |
| MainForm.ScriptCreativeNodes.cs | 剧本生成→创意描述节点同步 |
| MainForm.StoryboardVideoPreviewNodes.cs | 分镜视频生成→预览节点同步 |
| MainForm.DirectImageHistory.cs | 文生图历史库界面 |
| MainForm.MembershipHeartbeat.cs | 会员在线心跳检测 |
| MainForm.MembershipInspector.cs | 右侧会员信息与改密区域 |

### 3.2 工作流画布系统

| 文件 | 说明 |
|------|------|
| WorkflowCanvasControl.cs | 画布渲染：缩放、拖拽、连线、节点卡片 |
| WorkflowNodeCard.cs | 节点卡片 UI：最小化、参数区域 |
| WorkflowPortHandle.cs | 节点端口（输入/输出连接点） |
| WorkflowModels.cs | 节点类型定义、参数模型、连线规则、文档模型 |
| WorkflowStore.cs | .myai 项目保存/载入、项目包导出 |
| WorkflowExecutor.cs | 各节点提示词构建、结构化解析、结果落盘 |

### 3.3 AI 漫剧节点解析器

| 文件 | 说明 |
|------|------|
| WorkflowCharacterParser.cs | 角色清单解析 |
| WorkflowScriptParser.cs | 剧本/章节/创意描述解析 |
| WorkflowStoryboardParser.cs | 分镜描述解析 |
| WorkflowParseHelpers.cs | 解析公共辅助函数 |
| WorkflowResultParser.cs | 节点输出结果解析 |
| WorkflowPromptBuilder.cs | 节点提示词构建 |

### 3.4 模型调用服务

| 文件 | 说明 |
|------|------|
| WorkflowRuntimeService.cs | **核心**：本地/云端模型调用、ComfyUI、云雾视频任务、视频下载回收 |
| WorkflowModelResolver.cs | 模型解析与路由选择 |
| WorkflowRuntimeService.cs.bak_corrupt | 旧版损坏备份（910KB，已弃用） |
| ModelConfig.cs | 模型配置数据模型 |
| ModelSettingsForm.cs | 模型设置管理窗口 |
| ModelSettingsForm.Designer.cs | 模型设置窗口设计器代码 |
| AddModelForm.cs | 添加新模型窗口 |
| AddModelForm.Designer.cs | 添加模型窗口设计器代码 |
| ModelCallLogForm.cs | 模型调用日志查看窗口 |
| ModelCallLogService.cs | 模型调用日志服务（成功/失败日志写入） |

### 3.5 会员系统

| 文件 | 说明 |
|------|------|
| MembershipApiClient.cs | 会员 API 客户端（与服务端通信） |
| MembershipModels.cs | 会员数据模型 |
| MembershipContext.cs | 会员上下文/状态管理 |
| MembershipAuthForm.cs | 登录/注册窗口 |
| MembershipCredentialStore.cs | 凭据安全存储（DPAPI 加密） |
| MembershipSessionStore.cs | 会话/登录态缓存 |
| membership-config.json | 会员服务器地址配置 |

### 3.6 直出工作区（文生图/文生视频/文图生视频）

| 文件 | 说明 |
|------|------|
| StudioModes.cs | 工作区模式枚举定义 |
| QuickStudioForm.cs | 首页入口窗口（四类模式选择） |
| ProjectLauncherForm.cs | 项目启动器窗口 |
| DirectStudioNodePanel.cs | 直出节点面板 UI 与逻辑 |
| DirectStudioImageLibraryService.cs | 文生图历史库服务 |

### 3.7 角色设计相关

| 文件 | 说明 |
|------|------|
| CharacterDesignPanel.cs | 角色设计节点面板 |
| CharacterAssetExportService.cs | 角色资产导出服务 |
| CharacterDetailForm.cs | 角色详情查看窗口 |

### 3.8 分镜与视频相关

| 文件 | 说明 |
|------|------|
| CreativeDescriptionPanel.cs | 创意描述节点面板 |
| ScriptEpisodePanel.cs | 剧本/剧集节点面板 |
| StoryboardBreakdownPanel.cs | 分镜图拆解节点面板 |
| StoryboardPageEditForm.cs | 分镜页编辑窗口 |
| StoryboardShotEditForm.cs | 分镜镜头编辑窗口 |
| StoryboardVideoPanel.cs | 分镜视频节点面板 |
| VideoCollectionPanel.cs | 视频合集节点面板 |
| VideoCollectionSupport.cs | 视频合集支持/辅助 |
| VideoPlaybackForm.cs | 视频播放窗口 |
| ImageGalleryForm.cs | 图片画廊查看窗口 |

### 3.9 基础设施与工具

| 文件 | 说明 |
|------|------|
| AppServerConfig.cs | 服务端地址配置 |
| InitialServerSetupForm.cs | 首次启动服务器配置窗口 |
| ProjectStoragePaths.cs | 项目存储/产物路径管理 |
| ProjectLibraryExportService.cs | 项目库导出服务 |
| StringExtensions.cs | 字符串扩展方法 |
| PromptDialog.cs | 通用弹窗对话框 |
| WinApp.csproj | 项目文件（含 NuGet 依赖、构建配置） |

---

## 四、UpdateServer（服务端项目）

| 文件 | 说明 |
|------|------|
| Program.cs | ASP.NET 服务端入口 |
| ServerTrayHost.cs | Windows 系统托盘托管 |
| AdminPortalPage.cs | 后台管理页面 |
| AdminSecurity.cs | 管理员认证与安全 |
| MembershipStore.cs | 会员数据存储（SQLite） |
| MembershipModels.cs | 会员数据模型 |
| PasswordHasher.cs | 密码哈希 |
| SmtpMailSender.cs | 邮件发送服务 |
| ServerMailConfig.cs | 邮件配置模型 |
| ServerHostConfig.cs | 服务端主机配置 |
| LocalHttpsCertificateManager.cs | HTTPS 证书管理 |
| server-config.json | 服务端配置文件 |
| mail-config.json | 邮箱配置文件 |
| UpdateServer.csproj | 项目文件 |
| downloads\.keep | 下载目录占位 |
| manifests\stable.json | 版本清单（stable 通道） |
| Properties\launchSettings.json | VS 启动配置 |

---

## 五、Updater（自动更新器）

| 文件 | 说明 |
|------|------|
| Program.cs | 更新器入口：下载 ZIP → 解压 → 覆盖 → 启动主程序 |
| update-config.json | 更新服务器地址配置 |
| Updater.csproj | 项目文件 |

---

## 六、ClientConfigurator（客户端配置工具）

| 文件 | 说明 |
|------|------|
| Program.cs | 配置工具入口 |
| ConfiguratorForm.cs | 配置窗口 UI |
| ClientPackageConfig.cs | 配置模型（会员服务器 + 更新服务器地址） |
| ClientConfigurator.csproj | 项目文件 |

---

## 七、ClientInstaller（客户端安装器）

| 文件 | 说明 |
|------|------|
| Program.cs | 安装器入口 |
| InstallerForm.cs | 安装窗口 UI |
| InstallerProfile.cs | 安装配置模型 |
| ClientInstaller.csproj | 项目文件 |

---

## 八、ServerInstaller（服务端安装器）

| 文件 | 说明 |
|------|------|
| ServerInstaller.csproj | 项目文件（仅框架，未完整开发） |

---

## 九、docs（文档目录）

| 文件 | 说明 |
|------|------|
| 客户使用帮助.md | 面向最终客户的完整使用指南（安装、登录、AI漫剧/文生图/视频流程） |
| 项目维护说明.md | 面向开发者的维护文档（架构、节点流程、ComfyUI/云雾排障） |
| 系统维护记录.md | 系统功能基线、版本迭代和 BUG 修复台账；每次修 BUG 后必须追加记录 |
| JSAI文件结构说明.md | **本文档** |

---

## 十、scripts（构建脚本）

| 文件 | 说明 |
|------|------|
| Build-Installers.ps1 | 安装包构建 PowerShell 脚本 |

---

## 十一、data（运行时数据）

| 文件 | 说明 |
|------|------|
| membership.db | SQLite 会员数据库 |
| jsai-local-server.pfx | HTTPS 证书文件（本地开发用） |

---

## 十二、其他目录

| 目录 | 说明 |
|------|------|
| .vscode\ | VS Code 配置 |
| BUG文件\ | BUG 记录（BUG.txt 日志 + UI 截图若干） |
| downloads\ | 客户端更新包下载缓存 |
| external\ | 旧版 React UI 组件（BottomPanel / CanvasBoard / Node 等 .tsx 文件，已废弃） |
| manifests\ | 版本发布清单（stable.json） |
| 	mp\ | 临时文件（UI 测试截图） |
| rtifacts\installers\publish\ | **发布产物**（见下方） |

---

## 十三、artifacts/installers/publish（发布产物）

### client（客户端主程序 self-contained 发布）

| 说明 |
|------|
| WinApp.exe, WinApp.dll, WinApp.pdb — 客户端主程序 |
| Updater.exe — 更新器 |
| ClientConfigurator.exe — 配置工具 |
| *.dll (~400个) — .NET 8 运行时 + WPF/WinForms 框架 DLL |
| Workflowsapi\ — ComfyUI workflow JSON（5个模板） |
| zh-Hans\, zh-Hant\, cs\, de\, es\, r\, it\, ja\, ko\, pl\, pt-BR\, 
u\, 	r\ — 多语言资源 |
| membership-config.json, update-config.json — 默认配置 |
| Get_imageAI.json — ComfyUI 模板 |

### configurator（独立配置工具）

与客户端相同 NL 运行时 + ClientConfigurator.exe

### server（服务端 self-contained 发布）

UpdateServer.dll/exe + ASP.NET Core 运行时 + SQLite + MailKit

### clientinstaller

ClientInstaller.exe — 单文件安装器

---

## 十四、临时/废弃目录（不建议手动修改）

| 目录 | 说明 |
|------|------|
| _decompiled_runtime_2\ | 从 DLL 反编译的运行时代码参考 |
| _recovered_from_dll\ | 从 DLL 恢复的源码（MainForm / WorkflowRuntimeService） |
| _temp_build\ | 临时构建输出 |
| _tmp_publish_client_configurator_fix2\ | 临时发布产物副本 |
| _tmp_publish_*\ | 历史临时发布目录（多个） |
| _publish_obj\ | 临时发布中间件 |

---

## 十五、项目文件格式说明

| 后缀 | 说明 |
|------|------|
| .myai | 项目保存文件（含 workflow.json + 节点参数 + 产物副本） |
| .json | workflow 模板、配置、版本清单 |
| .cs | C# 源代码 |
| .csproj | .NET 项目文件 |
| .sln | Visual Studio 解决方案 |
| .ps1 | PowerShell 脚本 |
| .pfx | SSL 证书 |
| .db | SQLite 数据库 |

---

## 十六、关键文件优先级速查

| 场景 | 最可能修改的文件 |
|------|------------------|
| 新增 AI 漫剧节点类型 | WorkflowModels.cs → WorkflowExecutor.cs → WorkflowStore.cs → WorkflowRuntimeService.cs |
| 修改 ComfyUI workflow | Workflowsapi\*.json → WorkflowRuntimeService.cs（节点 ID 注入） |
| 调整主界面布局 | MainForm.Layout.cs |
| 修改模型路由策略 | WorkflowModelResolver.cs |
| 修改会员/登录逻辑 | MembershipApiClient.cs + UpdateServer\MembershipStore.cs |
| 修改版本更新逻辑 | Updater\Program.cs + UpdateServer\manifests\stable.json |
| 修改项目保存格式 | WorkflowStore.cs |
| Cloud/云雾视频排障 | WorkflowRuntimeService.cs（create/query/download 段） |
