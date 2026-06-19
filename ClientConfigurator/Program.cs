using System.Windows.Forms;

namespace JSAI.ClientConfigurator;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ConfiguratorForm(args.FirstOrDefault()));
    }
}
