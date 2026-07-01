using System.IO;
using System.Text.Json;
using Core_Workbench.Views;

namespace Core_Workbench.Services
{
    public enum WidgetType { Note, Cpu, Gpu, Ram, Clock, Bar }

    /// <summary>Persisted state for one desktop widget.</summary>
    public sealed class WidgetState
    {
        public WidgetType Type { get; set; } = WidgetType.Note;
        public Guid NoteId { get; set; }   // only used when Type == Note
        public double Left { get; set; } = double.NaN;
        public double Top { get; set; } = double.NaN;
        public double Width { get; set; } = 300;
        public double Height { get; set; } = 340;
        public double Opacity { get; set; } = 1.0;
        public bool Topmost { get; set; } = true;

        /// <summary>True = glued to the desktop layer; false = a normal floating window.</summary>
        public bool OnDesktop { get; set; } = false;
    }

    /// <summary>
    /// Tracks open desktop widgets, persists them to widgets.json, and restores
    /// them on launch. Saves are suppressed during shutdown so closing widgets as
    /// the app exits doesn't wipe the saved set.
    /// </summary>
    public static class WidgetManager
    {
        private static readonly List<WidgetWindow> _open = new();
        private static bool _suppressSave;
        private static bool _restored;

        /// <summary>Raised when a widget is opened or closed (for the Widgets page list).</summary>
        public static event Action? Changed;

        public static IReadOnlyList<WidgetWindow> Active => _open;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Core Workbench", "widgets.json");

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public static void Open(WidgetState state)
        {
            var w = new WidgetWindow(state);
            w.Closed += (_, _) =>
            {
                _open.Remove(w);
                if (!_suppressSave) SaveAll();
                Changed?.Invoke();
            };
            _open.Add(w);
            w.Show();
            if (!_suppressSave) SaveAll();
            Changed?.Invoke();
        }

        /// <summary>Pin a specific note as a widget.</summary>
        public static void Open(Guid noteId)
            => Open(new WidgetState { Type = WidgetType.Note, NoteId = noteId });

        /// <summary>Add a system widget (gauge or clock) with a sensible default size.</summary>
        public static void OpenSystem(WidgetType type)
        {
            var s = new WidgetState { Type = type };
            (s.Width, s.Height) = type switch
            {
                WidgetType.Bar => (820, 96),
                WidgetType.Clock => (250, 150),
                _ => (200, 220),
            };
            Open(s);
        }

        public static void RestoreSaved()
        {
            if (_restored) return;
            _restored = true;
            foreach (var s in LoadStates()) Open(s);
        }

        public static void RefreshAll()
        {
            foreach (var w in _open.ToList()) w.ReloadContent();
        }

        public static void SaveAll()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var states = _open.Select(w => w.GetState()).ToList();
                File.WriteAllText(FilePath, JsonSerializer.Serialize(states, JsonOpts));
            }
            catch { }
        }

        public static void PrepareForShutdown()
        {
            SaveAll();
            _suppressSave = true;
        }

        private static List<WidgetState> LoadStates()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<List<WidgetState>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { }
            return new List<WidgetState>();
        }
    }
}
