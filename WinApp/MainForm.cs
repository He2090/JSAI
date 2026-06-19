using System;
using System.Windows.Forms;

namespace JSAI.WinApp
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
            BuildLayout();
            ConfigureToolbarRuntimeLayout();
            HookMembershipHeartbeat();
            HookStartupVisibility();
            HookSessionManagement();
            HookProjectLaunchers();
            HookEvents();
            LoadInitialDocument();
        }
    }
}
