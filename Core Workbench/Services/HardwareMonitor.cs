namespace Core_Workbench.Services
{
    /// <summary>
    /// App-wide single instance of <see cref="HardwareMonitorService"/>. Both the
    /// Performance page and the Drive Health page read from the same monitor so we
    /// only ever load one kernel driver. Disposed once when the app exits.
    /// </summary>
    public static class HardwareMonitor
    {
        private static HardwareMonitorService? _instance;
        private static readonly object Gate = new();

        public static HardwareMonitorService Instance
        {
            get
            {
                lock (Gate)
                {
                    return _instance ??= new HardwareMonitorService();
                }
            }
        }

        public static void Shutdown()
        {
            lock (Gate)
            {
                _instance?.Dispose();
                _instance = null;
            }
        }
    }
}
