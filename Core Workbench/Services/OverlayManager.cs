using Core_Workbench.Views;

namespace Core_Workbench.Services
{
    /// <summary>Owns the single overlay window and applies live setting changes.</summary>
    public static class OverlayManager
    {
        private static OverlayWindow? _window;

        public static bool IsShown => _window != null;

        public static void Show()
        {
            if (_window == null)
            {
                _window = new OverlayWindow();
                _window.Closed += (_, _) => _window = null;
            }
            _window.Show();
            _window.ApplyLook();
        }

        public static void Hide()
        {
            _window?.Close();
            _window = null;
        }

        public static void SetEnabled(bool enabled)
        {
            OverlaySettings.Current.Enabled = enabled;
            OverlaySettings.Current.Save();
            if (enabled) Show();
            else Hide();
        }

        /// <summary>Re-read settings into the live overlay (position, opacity, click-through, metrics).</summary>
        public static void Apply()
        {
            OverlaySettings.Current.Save();
            _window?.ApplyLook();
        }

        /// <summary>Called at startup to restore the overlay if it was left enabled.</summary>
        public static void RestoreIfEnabled()
        {
            if (OverlaySettings.Current.Enabled) Show();
        }
    }
}
