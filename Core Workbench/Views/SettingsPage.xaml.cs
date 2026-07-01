using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using Core_Workbench.Services;

namespace Core_Workbench.Views
{
    /// <summary>App settings: start-with-Windows, tray behaviour, and about info.</summary>
    public partial class SettingsPage : UserControl
    {
        private readonly AppSettings _settings = AppSettings.Current;

        public SettingsPage()
        {
            InitializeComponent();

            AutostartCheck.IsChecked = AutostartService.IsEnabled();
            MinTrayCheck.IsChecked = _settings.MinimizeToTray;
            CloseTrayCheck.IsChecked = _settings.CloseToTray;

            AdminText.Text = IsAdmin() ? "Administrator" : "Standard (limited)";
            RuntimeText.Text = $".NET {Environment.Version}";
        }

        private void Autostart_Click(object sender, RoutedEventArgs e)
        {
            bool want = AutostartCheck.IsChecked == true;
            bool ok = want ? AutostartService.Enable() : AutostartService.Disable();
            if (!ok)
            {
                MessageBox.Show("Couldn't update the startup task. Make sure the app is running as administrator.",
                    "Startup", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            // Reflect the real state afterwards.
            AutostartCheck.IsChecked = AutostartService.IsEnabled();
        }

        private void MinTray_Click(object sender, RoutedEventArgs e)
        {
            _settings.MinimizeToTray = MinTrayCheck.IsChecked == true;
            _settings.Save();
        }

        private void CloseTray_Click(object sender, RoutedEventArgs e)
        {
            _settings.CloseToTray = CloseTrayCheck.IsChecked == true;
            _settings.Save();
        }

        private void OpenData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Core Workbench");
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
            catch { }
        }

        private static bool IsAdmin()
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
