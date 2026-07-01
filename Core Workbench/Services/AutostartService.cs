using System.Diagnostics;
using System.IO;

namespace Core_Workbench.Services
{
    /// <summary>
    /// "Start with Windows" via a Scheduled Task that runs at logon with highest
    /// privileges. A scheduled task (not a Run key) is used because the app needs
    /// admin — this way it launches elevated at sign-in without a UAC prompt.
    /// All operations require admin (the app already runs elevated).
    /// </summary>
    public static class AutostartService
    {
        private const string TaskName = "CoreWorkbenchStartup";

        private static string AppExe => Path.Combine(AppContext.BaseDirectory, "Core Workbench.exe");

        public static bool IsEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

        public static bool Enable()
        {
            string tr = $"\\\"{AppExe}\\\"";   // quoted exe path for /TR
            return RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"{tr}\" /SC ONLOGON /RL HIGHEST /F") == 0;
        }

        public static bool Disable() => RunSchtasks($"/Delete /TN \"{TaskName}\" /F") == 0;

        private static int RunSchtasks(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return -1;
                p.WaitForExit(8000);
                return p.HasExited ? p.ExitCode : -1;
            }
            catch { return -1; }
        }
    }
}
