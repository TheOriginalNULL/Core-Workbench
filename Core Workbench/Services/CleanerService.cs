using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Core_Workbench.Models;

namespace Core_Workbench.Services
{
    public sealed record CleanResult(long BytesFreed, int FilesDeleted, int Skipped);

    /// <summary>
    /// Scans and clears common Windows temp locations and the recycle bin.
    /// Files that are locked or in use are skipped rather than forced.
    /// </summary>
    public sealed class CleanerService
    {
        /// <summary>The set of locations this tool offers to clean.</summary>
        public List<CleanTarget> BuildTargets()
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var targets = new List<CleanTarget>
            {
                new()
                {
                    Name = "User temp files",
                    Description = "%TEMP% — your account's scratch folder",
                    Path = Path.GetTempPath(),
                    Icon = Ico("M3 7 H9 L11 9 H21 V19 H3 Z"),
                },
                new()
                {
                    Name = "Windows temp files",
                    Description = @"C:\Windows\Temp — system scratch folder (needs admin)",
                    Path = Path.Combine(winDir, "Temp"),
                    Icon = Ico("M3 4 H10 V11 H3 Z M14 4 H21 V11 H14 Z M3 14 H10 V21 H3 Z M14 14 H21 V21 H14 Z"),
                },
                new()
                {
                    Name = "Internet cache",
                    Description = "Temporary internet files",
                    Path = Path.Combine(localAppData, "Microsoft", "Windows", "INetCache"),
                    Icon = Ico("M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 M3 12 H21 M12 3 C8 7 8 17 12 21 C16 17 16 7 12 3"),
                },
                new()
                {
                    Name = "Crash dumps",
                    Description = "Windows error report dumps",
                    Path = Path.Combine(localAppData, "CrashDumps"),
                    Icon = Ico("M12 4 L21 19 H3 Z M12 10 V14 M12 16.5 V16.6"),
                },
                new()
                {
                    Name = "Recycle Bin",
                    Description = "Empties the recycle bin on all drives",
                    IsRecycleBin = true,
                    Icon = Ico("M4 7 H20 M9 7 V4 H15 V7 M6 7 L7.2 20 H16.8 L18 7 M10 11 V16 M14 11 V16"),
                },
            };

            // Drop folders that don't exist on this machine so the list stays honest.
            return targets
                .Where(t => t.IsRecycleBin || (t.Path != null && Directory.Exists(t.Path)))
                .ToList();
        }

        /// <summary>Parse a path-mini-language string into a frozen geometry for the UI.</summary>
        private static Geometry Ico(string data)
        {
            var g = Geometry.Parse(data);
            g.Freeze();
            return g;
        }

        /// <summary>Measure how much a target currently holds.</summary>
        public void Scan(CleanTarget target)
        {
            try
            {
                if (target.IsRecycleBin)
                {
                    (long size, long count) = QueryRecycleBin();
                    target.SizeBytes = size;
                    target.FileCount = (int)Math.Min(count, int.MaxValue);
                }
                else if (target.Path != null && Directory.Exists(target.Path))
                {
                    (long size, int count) = MeasureFolder(target.Path);
                    target.SizeBytes = size;
                    target.FileCount = count;
                }
                target.Status = "Scanned";
            }
            catch (Exception ex)
            {
                target.Status = $"Scan error: {ex.Message}";
            }
        }

        /// <summary>Delete everything inside a target. Skips locked files.</summary>
        public CleanResult Clean(CleanTarget target)
        {
            if (target.IsRecycleBin)
            {
                long freed = target.SizeBytes;
                int items = target.FileCount;
                EmptyRecycleBin();
                target.SizeBytes = 0;
                target.FileCount = 0;
                target.Status = "Emptied";
                return new CleanResult(freed, items, 0);
            }

            if (target.Path == null || !Directory.Exists(target.Path))
                return new CleanResult(0, 0, 0);

            long bytesFreed = 0;
            int filesDeleted = 0, skipped = 0;
            var dir = new DirectoryInfo(target.Path);

            foreach (var file in EnumerateFilesSafe(dir))
            {
                try
                {
                    long len = file.Length;
                    file.Attributes = FileAttributes.Normal;   // clear read-only
                    file.Delete();
                    bytesFreed += len;
                    filesDeleted++;
                }
                catch
                {
                    skipped++;   // in use / access denied — leave it be
                }
            }

            // Remove now-empty subdirectories (best effort).
            foreach (var sub in dir.GetDirectories())
            {
                try { sub.Delete(true); }
                catch { skipped++; }
            }

            // Refresh the figures.
            (long remaining, int remainingCount) = MeasureFolder(target.Path);
            target.SizeBytes = remaining;
            target.FileCount = remainingCount;
            target.Status = skipped > 0 ? $"Cleaned ({skipped} in use)" : "Cleaned";

            return new CleanResult(bytesFreed, filesDeleted, skipped);
        }

        // ---- folder measuring ----

        private static (long size, int count) MeasureFolder(string path)
        {
            long size = 0;
            int count = 0;
            foreach (var file in EnumerateFilesSafe(new DirectoryInfo(path)))
            {
                try { size += file.Length; count++; }
                catch { /* vanished mid-scan */ }
            }
            return (size, count);
        }

        /// <summary>
        /// Enumerate files recursively, swallowing per-directory access errors
        /// (some temp subfolders are locked by the OS).
        /// </summary>
        private static IEnumerable<FileInfo> EnumerateFilesSafe(DirectoryInfo root)
        {
            var stack = new Stack<DirectoryInfo>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();

                FileInfo[] files;
                try { files = dir.GetFiles(); }
                catch { continue; }
                foreach (var f in files) yield return f;

                DirectoryInfo[] subs;
                try { subs = dir.GetDirectories(); }
                catch { continue; }
                foreach (var s in subs) stack.Push(s);
            }
        }

        // ---- recycle bin via shell32 ----

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 0x01;
        private const uint SHERB_NOPROGRESSUI = 0x02;
        private const uint SHERB_NOSOUND = 0x04;

        private static (long size, long count) QueryRecycleBin()
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            int hr = SHQueryRecycleBin(null, ref info);   // null = all drives
            return hr == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
        }

        private static void EmptyRecycleBin()
        {
            SHEmptyRecycleBin(IntPtr.Zero, null,
                SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        }
    }
}
