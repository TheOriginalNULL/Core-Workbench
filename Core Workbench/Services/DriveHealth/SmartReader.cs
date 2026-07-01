using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Core_Workbench.Services.DriveHealth
{
    /// <summary>Extra SMART/NVMe fields not exposed by WMI. Any may be null.</summary>
    public sealed class SmartExtra
    {
        public long? PowerOnHours { get; init; }
        public long? PowerCycles { get; init; }
        public long? ReallocatedSectors { get; init; }
        public long? MediaErrors { get; init; }
        public double? HostReadsGb { get; init; }
        public double? HostWritesGb { get; init; }
        public double? TempC { get; init; }
        public double? RemainingLifePercent { get; init; }
        public string? CriticalWarning { get; init; }

        // From NVMe Identify Controller (authoritative when WMI returns blanks).
        public string? Serial { get; init; }
        public string? Model { get; init; }
        public string? Firmware { get; init; }
    }

    /// <summary>
    /// Reads raw S.M.A.R.T. (SATA/ATA) and NVMe health-log data straight from the
    /// device via DeviceIoControl — the fields Windows' WMI classes don't surface
    /// (reallocated sectors, total host reads/writes, NVMe power-on hours, etc.).
    /// Requires administrator rights. Every call is wrapped so a hostile/odd drive
    /// returns null instead of throwing.
    /// </summary>
    public static class SmartReader
    {
        public static SmartExtra? TryRead(int index, string iface)
        {
            if (index < 0) return null;
            try
            {
                return iface.Equals("NVMe", StringComparison.OrdinalIgnoreCase)
                    ? ReadNvme(index)
                    : ReadAta(index);   // SATA / ATA
            }
            catch
            {
                return null;
            }
        }

        // ---- device handle ----

        private static SafeFileHandle? OpenDrive(int index)
        {
            var h = CreateFile($@"\\.\PhysicalDrive{index}",
                GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            return h.IsInvalid ? null : h;
        }

        // ---- NVMe SMART / Health Information Log (log page 0x02) ----

        private static SmartExtra? ReadNvme(int index)
        {
            using var handle = OpenDrive(index);
            if (handle is null) return null;

            const int dataOffset = 48;          // 8 (query header) + 40 (protocol-specific)
            byte[] buf = new byte[dataOffset + 512];
            BitConverter.GetBytes(50).CopyTo(buf, 0);     // StorageDeviceProtocolSpecificProperty
            BitConverter.GetBytes(0).CopyTo(buf, 4);      // PropertyStandardQuery
            BitConverter.GetBytes(3).CopyTo(buf, 8);      // ProtocolTypeNvme
            BitConverter.GetBytes(2).CopyTo(buf, 12);     // NVMeDataTypeLogPage
            BitConverter.GetBytes(0x02).CopyTo(buf, 16);  // log page id = SMART/Health
            BitConverter.GetBytes(40).CopyTo(buf, 24);    // ProtocolDataOffset
            BitConverter.GetBytes(512).CopyTo(buf, 28);   // ProtocolDataLength

            if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY,
                    buf, (uint)buf.Length, buf, (uint)buf.Length, out _, IntPtr.Zero))
                return null;

            int b = dataOffset;
            byte critical = buf[b + 0];
            ushort tempK = BitConverter.ToUInt16(buf, b + 1);
            byte percentUsed = buf[b + 5];
            ulong dataRead = BitConverter.ToUInt64(buf, b + 32);    // units of 512000 bytes
            ulong dataWritten = BitConverter.ToUInt64(buf, b + 48);
            ulong powerCycles = BitConverter.ToUInt64(buf, b + 128);
            ulong powerOnHours = BitConverter.ToUInt64(buf, b + 144);
            ulong mediaErrors = BitConverter.ToUInt64(buf, b + 176);

            const double unitBytes = 512.0 * 1000.0;
            const double gb = 1_000_000_000.0;

            (string? serial, string? model, string? firmware) = ReadNvmeIdentify(handle);

            return new SmartExtra
            {
                PowerOnHours = (long)powerOnHours,
                PowerCycles = (long)powerCycles,
                MediaErrors = (long)mediaErrors,
                HostReadsGb = dataRead * unitBytes / gb,
                HostWritesGb = dataWritten * unitBytes / gb,
                TempC = tempK > 0 ? tempK - 273.15 : null,
                RemainingLifePercent = 100.0 - percentUsed,
                CriticalWarning = DescribeNvmeWarning(critical),
                Serial = serial,
                Model = model,
                Firmware = firmware,
            };
        }

        /// <summary>NVMe Identify Controller (CNS 0x01) → real serial / model / firmware.</summary>
        private static (string? serial, string? model, string? firmware) ReadNvmeIdentify(SafeFileHandle handle)
        {
            try
            {
                const int dataOffset = 48;
                byte[] buf = new byte[dataOffset + 4096];
                BitConverter.GetBytes(50).CopyTo(buf, 0);    // StorageDeviceProtocolSpecificProperty
                BitConverter.GetBytes(0).CopyTo(buf, 4);     // PropertyStandardQuery
                BitConverter.GetBytes(3).CopyTo(buf, 8);     // ProtocolTypeNvme
                BitConverter.GetBytes(1).CopyTo(buf, 12);    // NVMeDataTypeIdentify
                BitConverter.GetBytes(1).CopyTo(buf, 16);    // CNS 0x01 = Identify Controller
                BitConverter.GetBytes(40).CopyTo(buf, 24);   // ProtocolDataOffset
                BitConverter.GetBytes(4096).CopyTo(buf, 28); // ProtocolDataLength

                if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY,
                        buf, (uint)buf.Length, buf, (uint)buf.Length, out _, IntPtr.Zero))
                    return (null, null, null);

                string serial = Ascii(buf, dataOffset + 4, 20);
                string model = Ascii(buf, dataOffset + 24, 40);
                string firmware = Ascii(buf, dataOffset + 64, 8);
                return (Blank(serial), Blank(model), Blank(firmware));
            }
            catch { return (null, null, null); }
        }

        private static string Ascii(byte[] data, int offset, int length)
        {
            var chars = System.Text.Encoding.ASCII.GetString(data, offset, length);
            return chars.Trim().Trim('\0').Trim();
        }

        private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private static string? DescribeNvmeWarning(byte flags)
        {
            if (flags == 0) return null;
            var parts = new List<string>();
            if ((flags & 0x01) != 0) parts.Add("spare low");
            if ((flags & 0x02) != 0) parts.Add("temperature");
            if ((flags & 0x04) != 0) parts.Add("reliability degraded");
            if ((flags & 0x08) != 0) parts.Add("read-only");
            if ((flags & 0x10) != 0) parts.Add("backup failed");
            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        // ---- ATA SMART READ DATA (attribute table) ----

        private static SmartExtra? ReadAta(int index)
        {
            using var handle = OpenDrive(index);
            if (handle is null) return null;

            // Try the legacy SMART IOCTL first; fall back to ATA pass-through, which
            // works on AHCI controllers where the legacy path is often refused.
            byte[]? table = ReadAtaLegacy(handle, index) ?? ReadAtaPassThrough(handle);
            return table is null ? null : ParseAtaAttributes(table);
        }

        /// <summary>SMART attribute table (512 bytes: 2-byte revision then 12-byte entries).</summary>
        private static byte[]? ReadAtaLegacy(SafeFileHandle handle, int index)
        {
            byte[] inBuf = new byte[33];
            BitConverter.GetBytes(512u).CopyTo(inBuf, 0); // cBufferSize
            inBuf[4] = 0xD0;  // Features = SMART READ DATA
            inBuf[5] = 1;
            inBuf[6] = 1;
            inBuf[7] = 0x4F;
            inBuf[8] = 0xC2;
            inBuf[9] = 0xA0;
            inBuf[10] = 0xB0; // Command = SMART
            inBuf[12] = (byte)index;

            byte[] outBuf = new byte[524];   // 4 + 8 + 512
            if (!DeviceIoControl(handle, IOCTL_SMART_RCV_DRIVE_DATA,
                    inBuf, (uint)inBuf.Length, outBuf, (uint)outBuf.Length, out _, IntPtr.Zero))
                return null;

            byte[] table = new byte[512];
            Array.Copy(outBuf, 12, table, 0, 512);   // skip cBufferSize + DRIVERSTATUS
            return table;
        }

        private static byte[]? ReadAtaPassThrough(SafeFileHandle handle)
        {
            int size = Marshal.SizeOf<ATA_PASS_THROUGH_DIRECT>();
            IntPtr dataBuf = Marshal.AllocHGlobal(512);
            IntPtr aptPtr = Marshal.AllocHGlobal(size);
            try
            {
                var apt = new ATA_PASS_THROUGH_DIRECT
                {
                    Length = (ushort)size,
                    AtaFlags = 0x02,           // ATA_FLAGS_DATA_IN
                    DataTransferLength = 512,
                    TimeOutValue = 10,
                    DataBuffer = dataBuf,
                    PreviousTaskFile = new byte[8],
                    CurrentTaskFile = new byte[8] { 0xD0, 1, 1, 0x4F, 0xC2, 0xA0, 0xB0, 0 },
                };
                Marshal.StructureToPtr(apt, aptPtr, false);

                if (!DeviceIoControl(handle, IOCTL_ATA_PASS_THROUGH_DIRECT,
                        aptPtr, (uint)size, aptPtr, (uint)size, out _, IntPtr.Zero))
                    return null;

                byte[] table = new byte[512];
                Marshal.Copy(dataBuf, table, 0, 512);
                return table;
            }
            catch { return null; }
            finally
            {
                Marshal.FreeHGlobal(dataBuf);
                Marshal.FreeHGlobal(aptPtr);
            }
        }

        private static SmartExtra ParseAtaAttributes(byte[] table)
        {
            long? realloc = null, poh = null, cycles = null;
            double? readsGb = null, writesGb = null;
            const double gb = 1_000_000_000.0;

            // Entries start after the 2-byte revision; up to 30 entries of 12 bytes.
            for (int i = 0; i < 30; i++)
            {
                int off = 2 + i * 12;
                if (off + 11 >= table.Length) break;
                byte id = table[off];
                if (id == 0) continue;
                long raw = Read6(table, off + 5);

                switch (id)
                {
                    case 5: realloc = raw; break;                  // reallocated sectors
                    case 9: poh = raw; break;                      // power-on hours
                    case 12: cycles = raw; break;                  // power cycle count
                    case 241: writesGb = raw * 512.0 / gb; break;  // total LBAs written
                    case 242: readsGb = raw * 512.0 / gb; break;   // total LBAs read
                }
            }

            return new SmartExtra
            {
                ReallocatedSectors = realloc,
                PowerOnHours = poh,
                PowerCycles = cycles,
                HostReadsGb = readsGb,
                HostWritesGb = writesGb,
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ATA_PASS_THROUGH_DIRECT
        {
            public ushort Length;
            public ushort AtaFlags;
            public byte PathId;
            public byte TargetId;
            public byte Lun;
            public byte ReservedAsUchar;
            public uint DataTransferLength;
            public uint TimeOutValue;
            public uint ReservedAsUlong;
            public IntPtr DataBuffer;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] PreviousTaskFile;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] CurrentTaskFile;
        }

        private static long Read6(byte[] data, int offset)
        {
            long v = 0;
            for (int i = 0; i < 6; i++) v |= (long)data[offset + i] << (8 * i);
            return v;
        }

        // ---- P/Invoke ----

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x1;
        private const uint FILE_SHARE_WRITE = 0x2;
        private const uint OPEN_EXISTING = 3;
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        private const uint IOCTL_SMART_RCV_DRIVE_DATA = 0x0007C088;
        private const uint IOCTL_ATA_PASS_THROUGH_DIRECT = 0x0004D02C;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string fileName, uint access, uint share,
            IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
            byte[] inBuffer, uint inBufferSize, byte[] outBuffer, uint outBufferSize,
            out uint bytesReturned, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
            IntPtr inBuffer, uint inBufferSize, IntPtr outBuffer, uint outBufferSize,
            out uint bytesReturned, IntPtr overlapped);
    }
}
