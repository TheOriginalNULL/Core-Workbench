using System.IO;
using System.Text.Json;
using Core_Workbench.Models;

namespace Core_Workbench.Services
{
    /// <summary>
    /// Loads and saves notes as a single JSON file under
    /// %AppData%\Core Workbench\notes.json.
    /// </summary>
    public sealed class NotesService
    {
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Core Workbench");

        private static readonly string FilePath = Path.Combine(Dir, "notes.json");

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public List<Note> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<Note>();
                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<Note>>(json) ?? new List<Note>();
            }
            catch
            {
                // Corrupt or unreadable file: start clean rather than crash.
                return new List<Note>();
            }
        }

        public void Save(IEnumerable<Note> notes)
        {
            Directory.CreateDirectory(Dir);
            string json = JsonSerializer.Serialize(notes, JsonOpts);
            File.WriteAllText(FilePath, json);
        }
    }
}
