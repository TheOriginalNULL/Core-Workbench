using System.Drawing;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Core_Workbench.Services
{
    /// <summary>
    /// System-tray icon with a dark, themed menu: live CPU/GPU/RAM stats, an
    /// overlay toggle, start-with-Windows toggle, open, and exit. The tooltip also
    /// updates with live usage. The host wires open/exit actions.
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        // Theme colours (match the WPF palette).
        private static readonly Color Surface = Color.FromArgb(0x1A, 0x11, 0x33);
        private static readonly Color SurfaceAlt = Color.FromArgb(0x2C, 0x1D, 0x52);
        private static readonly Color Accent = Color.FromArgb(0x7E, 0x57, 0xD6);
        private static readonly Color BorderClr = Color.FromArgb(0x38, 0x25, 0x62);
        private static readonly Color TextClr = Color.FromArgb(0xEC, 0xE8, 0xF6);
        private static readonly Color MutedClr = Color.FromArgb(0x9A, 0x8F, 0xB8);

        private readonly Forms.NotifyIcon _icon;
        private readonly Forms.ToolStripLabel _stats;
        private readonly Forms.ToolStripMenuItem _overlayItem;
        private readonly Forms.ToolStripMenuItem _startupItem;
        private readonly DispatcherTimer _timer;

        public TrayIcon(Action onOpen, Action onExit)
        {
            _icon = new Forms.NotifyIcon
            {
                Visible = true,
                Text = "Core Workbench",
                Icon = LoadIcon(),
            };

            var menu = new Forms.ContextMenuStrip
            {
                BackColor = Surface,
                ForeColor = TextClr,
                ShowImageMargin = true,
                Renderer = new Forms.ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false },
                Font = new Font("Segoe UI", 9f),
            };

            _stats = new Forms.ToolStripLabel("CPU --   GPU --   RAM --") { ForeColor = MutedClr, Enabled = false };
            menu.Items.Add(_stats);
            menu.Items.Add(new Forms.ToolStripSeparator());

            var open = Item("Open Core Workbench", (_, _) => onOpen());
            open.Font = new Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold);
            menu.Items.Add(open);

            _overlayItem = Item("Performance overlay", (_, _) =>
            {
                OverlayManager.SetEnabled(!OverlaySettings.Current.Enabled);
            });
            _overlayItem.CheckOnClick = false;
            menu.Items.Add(_overlayItem);

            _startupItem = Item("Start with Windows", (_, _) =>
            {
                bool target = !AutostartService.IsEnabled();
                if (target) AutostartService.Enable(); else AutostartService.Disable();
            });
            menu.Items.Add(_startupItem);

            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(Item("Exit", (_, _) => onExit()));

            menu.Opening += (_, _) => RefreshMenu();
            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (_, _) => onOpen();

            // Live tooltip + stats line.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += async (_, _) => await UpdateStats();
            _timer.Start();
            _ = UpdateStats();
        }

        private static Forms.ToolStripMenuItem Item(string text, EventHandler onClick)
        {
            var i = new Forms.ToolStripMenuItem(text) { ForeColor = TextClr };
            i.Click += onClick;
            return i;
        }

        private void RefreshMenu()
        {
            _overlayItem.Checked = OverlaySettings.Current.Enabled;
            try { _startupItem.Checked = AutostartService.IsEnabled(); } catch { }
        }

        private async Task UpdateStats()
        {
            try
            {
                HardwareSnapshot s = await Task.Run(() => HardwareMonitor.Instance.Read());
                string line = $"CPU {s.CpuLoad:0}%   GPU {s.GpuLoad:0}%   RAM {s.RamLoad:0}%";
                _stats.Text = line;
                _icon.Text = $"Core Workbench\n{line}";   // tooltip (max 63 chars)
            }
            catch { }
        }

        public void ShowBalloon(string title, string text)
        {
            try { _icon.ShowBalloonTip(2000, title, text, Forms.ToolTipIcon.Info); } catch { }
        }

        private static Icon LoadIcon()
        {
            try
            {
                var stream = Application.GetResourceStream(
                    new Uri("/Assets/SecondaryLogo.ico", UriKind.Relative))?.Stream;
                if (stream != null)
                    return new Icon(stream);
            }
            catch { }
            return SystemIcons.Application;
        }

        public void Dispose()
        {
            _timer.Stop();
            _icon.Visible = false;
            _icon.Dispose();
        }

        /// <summary>Dark colour table so the WinForms menu matches the app theme.</summary>
        private sealed class DarkColors : Forms.ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Surface;
            public override Color ImageMarginGradientBegin => Surface;
            public override Color ImageMarginGradientMiddle => Surface;
            public override Color ImageMarginGradientEnd => Surface;
            public override Color MenuBorder => BorderClr;
            public override Color MenuItemBorder => Accent;
            public override Color MenuItemSelected => SurfaceAlt;
            public override Color MenuItemSelectedGradientBegin => SurfaceAlt;
            public override Color MenuItemSelectedGradientEnd => SurfaceAlt;
            public override Color MenuItemPressedGradientBegin => SurfaceAlt;
            public override Color MenuItemPressedGradientEnd => SurfaceAlt;
            public override Color SeparatorDark => BorderClr;
            public override Color SeparatorLight => BorderClr;
            public override Color CheckBackground => Accent;
            public override Color CheckSelectedBackground => Accent;
            public override Color CheckPressedBackground => Accent;
        }
    }
}
