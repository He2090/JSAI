using System.Diagnostics;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    internal static class Program
    {
        private const string FromUpdaterArgument = "--from-updater";

        [STAThread]
        private static void Main(string[] args)
        {
            if (!args.Any(arg => string.Equals(arg, FromUpdaterArgument, StringComparison.OrdinalIgnoreCase)) &&
                TryLaunchUpdater())
            {
                return;
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => LogCrash("UI", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash("AppDomain", e.ExceptionObject as Exception);

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var serverConfig = AppServerConfig.Load();
            if (!serverConfig.IsConfigured)
            {
                using var setupForm = new InitialServerSetupForm(serverConfig);
                if (setupForm.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                serverConfig = AppServerConfig.Load();
            }

            var activationStatus = MachineActivationService.LoadStatus();
            if (activationStatus.IsActivated)
            {
                MembershipContext.CurrentSession = MachineActivationService.CreateSession(activationStatus);
            }
            else
            {
                using var activationForm = new MachineActivationForm(serverConfig, activationStatus);
                var activationResult = activationForm.ShowDialog();
                if (activationResult != DialogResult.OK && activationResult != DialogResult.Ignore)
                {
                    return;
                }

                MembershipContext.CurrentSession = activationForm.ResolvedSession ??
                                                   MachineActivationService.CreateUnlicensedSession(activationStatus.MachineCode);
            }

            Application.Run(new MainForm());
        }

        private static bool TryLaunchUpdater()
        {
            try
            {
                var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var updaterPath = Path.Combine(baseDirectory, "Updater.exe");
                var updaterDllPath = Path.Combine(baseDirectory, "Updater.dll");
                var updaterDepsPath = Path.Combine(baseDirectory, "Updater.deps.json");
                var updaterRuntimeConfigPath = Path.Combine(baseDirectory, "Updater.runtimeconfig.json");

                if (!File.Exists(updaterPath) ||
                    !File.Exists(updaterDllPath) ||
                    !File.Exists(updaterDepsPath) ||
                    !File.Exists(updaterRuntimeConfigPath))
                {
                    return false;
                }

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = $"--main \"{Path.Combine(baseDirectory, "WinApp.exe")}\" {FromUpdaterArgument}",
                    WorkingDirectory = baseDirectory,
                    UseShellExecute = true,
                });

                return process != null;
            }
            catch
            {
                return false;
            }
        }

        private static void LogCrash(string source, Exception? exception)
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                var text =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}" +
                    $"{exception}{Environment.NewLine}{Environment.NewLine}";
                File.AppendAllText(path, text);
            }
            catch
            {
            }

            if (exception != null)
            {
                MessageBox.Show(exception.ToString(), "程序异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (source == "UI")
            {
                Application.ExitThread();
                return;
            }

            Environment.Exit(1);
        }
    }
}
