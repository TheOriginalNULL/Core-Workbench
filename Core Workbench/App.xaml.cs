using System.Windows;

namespace Core_Workbench
{
    /// <summary>
    /// Interaction logic for App.xaml. Launches straight into the main window.
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var start = new MainWindow();
            MainWindow = start;
            start.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Release the shared hardware-monitor driver once, on shutdown.
            Services.OverlayManager.Hide();
            Services.HardwareMonitor.Shutdown();
            base.OnExit(e);
        }
    }
}
