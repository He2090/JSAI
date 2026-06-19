namespace JSAI.Installer;

internal static class InstallerProfile
{
#if SERVER_INSTALLER
    public static string WindowTitle => "JSAI 服务端安装程序";
    public static string HeaderTitle => "JSAI 服务端";
    public static string HeaderDescription =>
        "这个安装器会把服务端安装到本机，首次启动后会在系统托盘中常驻运行，并提供客户端连接、会员服务和后台管理能力。";
    public static string TargetDisplayName => "服务端";
    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "JSAIServer");
    public static string MainExecutableName => "UpdateServer.exe";
    public static string ShortcutBaseName => "JSAI 服务端";
    public static string PayloadFilePrefix => "server-payload";
    public static string CompletionMessage =>
        "服务端安装完成。启动后会自动加载服务端程序，并在右下角托盘显示运行状态。";
#else
    public static string WindowTitle => "JSAI 客户端安装程序";
    public static string HeaderTitle => "JSAI 工作助手";
    public static string HeaderDescription =>
        "这个安装器会把客户端安装到本机，首次启动时会提示配置服务器地址和端口，然后进入登录与创作界面。";
    public static string TargetDisplayName => "客户端";
    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "JSAIClient");
    public static string MainExecutableName => "WinApp.exe";
    public static string ShortcutBaseName => "JSAI 工作助手";
    public static string PayloadFilePrefix => "client-payload";
    public static string CompletionMessage =>
        "客户端安装完成。首次启动时会提示配置服务器地址和端口。";
#endif
}
