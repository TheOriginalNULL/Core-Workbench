using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Core_Workbench.Services;

namespace Core_Workbench.Views
{
    /// <summary>
    /// Home screen: a greeting, the message of the day, and quick-access cards
    /// that jump to the other modules.
    /// </summary>
    public partial class DashboardPage : UserControl
    {
        public DashboardPage()
        {
            InitializeComponent();

            int hour = DateTime.Now.Hour;
            GreetingText.Text = hour < 12 ? "Good morning"
                              : hour < 18 ? "Good afternoon"
                              : "Good evening";
            DateText.Text = DateTime.Now.ToString("dddd, MMMM d");
            DailyText.Text = DailyMessage.Today();
        }

        private void Quick_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string target)
                (Window.GetWindow(this) as MainWindow)?.NavigateTo(target);
        }
    }
}
