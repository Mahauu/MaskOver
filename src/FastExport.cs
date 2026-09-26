using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MaskOver
{
    internal struct ChangePoint
    {
        public int X;
        public int Y;
        public int Color;
    }

    internal sealed class ExportJob
    {
        public string Destination;
        public bool SingleColor;
        public int Color;
    }

    internal sealed class ChangeIndex
    {
        public const int MaxPixels = 2000000;

        public int Width;
        public int Height;
        public bool Truncated;
        public Dictionary<int, int> ColorCounts;
        public Dictionary<long, int> Pixels;

        public static ChangeIndex Build(BmpSurface mask, BmpSurface original, Func<bool> cancel)
        {
            ChangeIndex index = new ChangeIndex();
            index.Width = mask.Width;
            index.Height = mask.Height;
            index.ColorCounts = new Dictionary<int, int>();
            index.Pixels = new Dictionary<long, int>();
            bool cancelled = mask.VisitChanges(original, delegate(int x, int y, int color)
            {
                int count;
                index.ColorCounts.TryGetValue(color, out count);
                index.ColorCounts[color] = count + 1;
                if (index.Truncated)
                    return;
                long id = ((long)y * index.Width) + x;
                if (!index.Pixels.ContainsKey(id) && index.Pixels.Count >= MaxPixels)
                {
                    index.Truncated = true;
                    index.Pixels = null;
                    return;
                }
                index.Pixels[id] = color;
            }, cancel);
            if (cancelled)
                return null;
            return index;
        }

        public bool HasColor(int color)
        {
            return ColorCounts.ContainsKey(color);
        }

        public int CountOf(int color)
        {
            int count;
            if (ColorCounts.TryGetValue(color, out count))
                return count;
            return 0;
        }

        public void ApplyHistory(StrokeHistory history, BmpSurface mask, BmpSurface original, bool forward)
        {
            foreach (PixelDelta delta in history.Pixels)
            {
                int x;
                int y;
                mask.DecodeOffset(delta.Offset, out x, out y);
                int before = forward ? delta.Before : delta.After;
                int after = forward ? delta.After : delta.Before;
                Apply(x, y, before, after, original.ReadPacked(x, y));
            }
        }

        public void Apply(int x, int y, int before, int after, int originalColor)
        {
            long id = ((long)y * Width) + x;
            if (before != originalColor)
                RemovePixel(id, before);
            if (after != originalColor)
                AddPixel(id, after);
        }

        private void RemovePixel(long id, int color)
        {
            int count;
            if (ColorCounts.TryGetValue(color, out count))
            {
                count--;
                if (count <= 0)
                    ColorCounts.Remove(color);
                else
                    ColorCounts[color] = count;
            }
            if (Pixels != null)
                Pixels.Remove(id);
        }

        private void AddPixel(long id, int color)
        {
            int count;
            ColorCounts.TryGetValue(color, out count);
            ColorCounts[color] = count + 1;
            if (Pixels == null)
                return;
            if (!Pixels.ContainsKey(id) && Pixels.Count >= MaxPixels)
            {
                Truncated = true;
                Pixels = null;
                return;
            }
            Pixels[id] = color;
        }
    }

    internal sealed class MaskChangeTracker
    {
        private readonly object gate = new object();
        private readonly BmpSurface mask;
        private readonly BmpSurface original;
        private ChangeIndex current;
        private int epoch;
        private volatile bool stroke;
        private volatile bool stop;
        private bool running;
        private Exception error;

        public MaskChangeTracker(BmpSurface mask, BmpSurface original)
        {
            this.mask = mask;
            this.original = original;
            running = true;
            ThreadPool.QueueUserWorkItem(delegate { Work(); });
        }

        public void BeginStroke()
        {
            lock (gate)
            {
                stroke = true;
                epoch++;
                Monitor.PulseAll(gate);
            }
        }

        public void EndStroke(StrokeHistory history)
        {
            lock (gate)
            {
                stroke = false;
                epoch++;
                if (current != null && history != null && history.Pixels.Count > 0)
                    current.ApplyHistory(history, mask, original, true);
                Monitor.PulseAll(gate);
            }
        }

        public void CancelStroke()
        {
            lock (gate)
            {
                stroke = false;
                epoch++;
                Monitor.PulseAll(gate);
            }
        }

        public void ApplyCommitted(StrokeHistory history, bool forward)
        {
            lock (gate)
            {
                epoch++;
                if (current != null)
                    current.ApplyHistory(history, mask, original, forward);
                Monitor.PulseAll(gate);
            }
        }

        public ChangeIndex TryGet()
        {
            lock (gate)
            {
                if (stroke)
                    return null;
                return current;
            }
        }

        public ChangeIndex WaitForReady()
        {
            lock (gate)
            {
                while (current == null && error == null && !stop)
                    Monitor.Wait(gate, 200);
                if (error != null)
                    throw error;
                if (current == null)
                    throw new InvalidOperationException(Locale.T("Indeks zmian został przerwany.", "The change index was cancelled."));
                return current;
            }
        }

        public void Stop()
        {
            lock (gate)
            {
                stop = true;
                epoch++;
                Monitor.PulseAll(gate);
                while (running)
                    Monitor.Wait(gate, 50);
            }
        }

        private bool Cancel()
        {
            return stop || stroke;
        }

        private void Work()
        {
            try
            {
                while (true)
                {
                    int seen;
                    lock (gate)
                    {
                        if (stop)
                        {
                            running = false;
                            Monitor.PulseAll(gate);
                            return;
                        }
                        if (stroke)
                        {
                            Monitor.Wait(gate, 30);
                            continue;
                        }
                        seen = epoch;
                    }
                    ChangeIndex built = ChangeIndex.Build(mask, original, Cancel);
                    lock (gate)
                    {
                        if (stop)
                        {
                            running = false;
                            Monitor.PulseAll(gate);
                            return;
                        }
                        if (built == null || stroke || seen != epoch)
                            continue;
                        current = built;
                        running = false;
                        Monitor.PulseAll(gate);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    error = ex;
                    running = false;
                    Monitor.PulseAll(gate);
                }
            }
        }
    }

    internal sealed class BitPattern
    {
        public byte[] Data;
        public int BitLength;
    }

    internal static class FastExport
    {
        private struct Run
        {
            public byte Value;
            public int Count;
        }

        private sealed class ScanTarget
        {
            public ExportJob Job;
            public string TemporaryBmp;
            public BmpSurface Surface;
            public long Count;
        }

        private static readonly object PatternGate = new object();
        private static readonly Dictionary<long, BitPattern> CleanPatterns = new Dictionary<long, BitPattern>();
        private static readonly int[] LengthSymbol = new int[259];
        private static readonly int[] LengthExtraBits = new int[259];
        private static readonly int[] LengthExtraBase = new int[259];
        private static readonly uint[] CrcTable;

        static FastExport()
        {
            int[] symbols = { 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275, 276, 277, 278, 279, 280, 281, 282, 283, 284, 285 };
            int[] bases = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 };
            int[] extras = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };
            for (int i = 0; i < symbols.Length; i++)
            {
                int max;
                if (symbols[i] == 285)
                    max = 258;
                else if (symbols[i] == 284)
                    max = 257;
                else
                {
                    int span = extras[i] == 0 ? 1 : (1 << extras[i]);
                    max = bases[i] + span - 1;
                }
                for (int length = bases[i]; length <= max; length++)
                {
                    LengthSymbol[length] = symbols[i];
                    LengthExtraBits[length] = extras[i];
                    LengthExtraBase[length] = bases[i];
                }
            }
            LengthSymbol[258] = 285;
            LengthExtraBits[258] = 0;
            LengthExtraBase[258] = 258;
            CrcTable = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    if ((c & 1) != 0)
                        c = 0xEDB88320u ^ (c >> 1);
                    else
                        c = c >> 1;
                }
                CrcTable[n] = c;
            }
        }

        public static long Write(BmpSurface mask, BmpSurface original, string originalPath, ChangeIndex index, int backgroundMode, bool png, HashSet<int> selected, List<ExportJob> jobs)
        {
            if (index != null && !index.Truncated && index.Pixels != null)
                return WriteIndexed(mask, original, originalPath, index, backgroundMode, png, selected, jobs);
            return WriteScanned(mask, original, originalPath, backgroundMode, png, selected, jobs);
        }

        public static void WriteSurfacePng(string path, BmpSurface surface)
        {
            WriteStreamPng(path, surface, new List<ChangePoint>(), true, 0);
        }

        private static long WriteIndexed(BmpSurface mask, BmpSurface original, string originalPath, ChangeIndex index, int backgroundMode, bool png, HashSet<int> selected, List<ExportJob> jobs)
        {
            long total = 0;
            for (int i = 0; i < jobs.Count; i++)
            {
                ExportJob job = jobs[i];
                List<ChangePoint> points = Collect(index, selected, job.SingleColor, job.Color);
                WriteFile(original, originalPath, mask.Width, mask.Height, backgroundMode, png, job, points);
                total += points.Count;
            }
            return total;
        }

        private static long WriteScanned(BmpSurface mask, BmpSurface original, string originalPath, int backgroundMode, bool png, HashSet<int> selected, List<ExportJob> jobs)
        {
            List<ScanTarget> targets = new List<ScanTarget>();
            try
            {
                for (int i = 0; i < jobs.Count; i++)
                {
                    ScanTarget target = new ScanTarget();
                    target.Job = jobs[i];
                    target.TemporaryBmp = jobs[i].Destination + ".scan.bmp";
                    if (File.Exists(target.TemporaryBmp))
                        File.Delete(target.TemporaryBmp);
                    if (backgroundMode == 2)
                        File.Copy(originalPath, target.TemporaryBmp, true);
                    else
                        CreateBmp(target.TemporaryBmp, mask.Width, mask.Height, backgroundMode == 1 ? (byte)255 : (byte)0);
                    target.Surface = new BmpSurface(target.TemporaryBmp, true);
                    targets.Add(target);
                }
                mask.VisitChanges(original, delegate(int x, int y, int color)
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        ScanTarget target = targets[i];
                        bool accept = target.Job.SingleColor ? target.Job.Color == color : selected.Contains(color);
                        if (!accept)
                            continue;
                        target.Surface.WritePackedAtOffset(target.Surface.PixelOffset(x, y), color);
                        target.Count++;
                    }
                }, null);
                for (int i = 0; i < targets.Count; i++)
                {
                    targets[i].Surface.Flush();
                    targets[i].Surface.Dispose();
                    targets[i].Surface = null;
                }
                long total = 0;
                for (int i = 0; i < targets.Count; i++)
                {
                    ScanTarget target = targets[i];
                    if (png)
                    {
                        string temporaryPng = target.Job.Destination + ".maskover.tmp";
                        using (BmpSurface exported = new BmpSurface(target.TemporaryBmp, false))
                            WriteSurfacePng(temporaryPng, exported);
                        Publish(temporaryPng, target.Job.Destination);
                    }
                    else
                        Publish(target.TemporaryBmp, target.Job.Destination);
                    total += target.Count;
                }
                return total;
            }
            finally
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i].Surface != null)
                        targets[i].Surface.Dispose();
                    if (File.Exists(targets[i].TemporaryBmp))
                        File.Delete(targets[i].TemporaryBmp);
                }
            }
        }

        private static void WriteFile(BmpSurface original, string originalPath, int width, int height, int backgroundMode, bool png, ExportJob job, List<ChangePoint> points)
        {
            string temporary = job.Destination + ".maskover.tmp";
            if (File.Exists(temporary))
                File.Delete(temporary);
            try
            {
                if (png && backgroundMode != 2)
                    WriteConstantPng(temporary, width, height, backgroundMode, points, job.SingleColor, job.Color);
                else if (png)
                    WriteStreamPng(temporary, original, points, job.SingleColor, job.Color);
                else
                {
                    if (backgroundMode == 2)
                        File.Copy(originalPath, temporary, true);
                    else
                        CreateBmp(temporary, width, height, backgroundMode == 1 ? (byte)255 : (byte)0);
                    PatchBmp(temporary, points, job.SingleColor, job.Color);
                }
                Publish(temporary, job.Destination);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static List<ChangePoint> Collect(ChangeIndex index, HashSet<int> selected, bool single, int color)
        {
            List<ChangePoint> points = new List<ChangePoint>();
            foreach (KeyValuePair<long, int> pair in index.Pixels)
            {
                if (single)
                {
                    if (pair.Value != color)
                        continue;
                }
                else if (!selected.Contains(pair.Value))
                    continue;
                ChangePoint point = new ChangePoint();
                point.X = (int)(pair.Key % index.Width);
                point.Y = (int)(pair.Key / index.Width);
                point.Color = pair.Value;
                points.Add(point);
            }
            points.Sort(ComparePoints);
            return points;
        }

        private static int ComparePoints(ChangePoint left, ChangePoint right)
        {
            if (left.Y != right.Y)
                return left.Y.CompareTo(right.Y);
            return left.X.CompareTo(right.X);
        }

        private static void PatchBmp(string path, List<ChangePoint> points, bool single, int singleColor)
        {
            if (points.Count == 0)
                return;
            using (BmpSurface output = new BmpSurface(path, true))
            {
                for (int i = 0; i < points.Count; i++)
                {
                    ChangePoint point = points[i];
                    int packed = single ? singleColor : point.Color;
                    output.WritePackedAtOffset(output.PixelOffset(point.X, point.Y), packed);
                }
                output.Flush();
            }
        }

        private static void CreateBmp(string path, int width, int height, byte fill)
        {
            int rowBytes = width * 3;
            int stride = (rowBytes + 3) & ~3;
            long pixelBytes = (long)stride * height;
            long fileSize = 54L + pixelBytes;
            if (fileSize > uint.MaxValue)
                throw new InvalidDataException(Locale.T("Maska jest za duża dla pliku BMP.", "The mask is too large for a BMP file."));
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] header = new byte[54];
                header[0] = (byte)'B';
                header[1] = (byte)'M';
                WriteLe(header, 2, (uint)fileSize);
                WriteLe(header, 10, 54);
                WriteLe(header, 14, 40);
                WriteLe(header, 18, (uint)width);
                WriteLe(header, 22, (uint)height);
                header[26] = 1;
                header[28] = 24;
                WriteLe(header, 34, (uint)pixelBytes);
                stream.Write(header, 0, 54);
                if (fill == 0 && TryMakeSparse(stream))
                {
                    // A sparse hole reads back as zeros, so the black background costs a header
                    // instead of a full-size copy of the mask.
                    stream.SetLength(fileSize);
                    return;
                }
                byte[] row = new byte[stride];
                for (int i = 0; i < rowBytes; i++)
                    row[i] = fill;
                for (int y = 0; y < height; y++)
                    stream.Write(row, 0, stride);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        private static bool TryMakeSparse(FileStream stream)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return false;
            try
            {
                uint ignored;
                return DeviceIoControl(stream.SafeFileHandle, 0x000900C4, IntPtr.Zero, 0, IntPtr.Zero, 0, out ignored, IntPtr.Zero);
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        private static void Publish(string temporary, string destination)
        {
            string backup = destination + ".maskover.bak";
            if (File.Exists(destination))
            {
                if (File.Exists(backup))
                    File.Delete(backup);
                File.Replace(temporary, destination, backup, true);
            }
            else
                File.Move(temporary, destination);
        }

        private static void WriteConstantPng(string path, int width, int height, int backgroundMode, List<ChangePoint> points, bool single, int singleColor)
        {
            BitWriter writer = new BitWriter();
            BitPattern clean = GetCleanPattern(width, backgroundMode);
            uint adlerA = 1;
            uint adlerB = 0;
            int point = 0;
            int y = 0;
            while (y < height)
            {
                int nextY = point < points.Count ? points[point].Y : height;
                if (nextY > y)
                {
                    int span = nextY - y;
                    writer.AppendPattern(clean, span);
                    AdlerClean(ref adlerA, ref adlerB, width, backgroundMode, span);
                    y = nextY;
                }
                if (y >= height)
                    break;
                int start = point;
                while (point < points.Count && points[point].Y == y)
                    point++;
                AddDirtyRow(writer, width, backgroundMode, points, start, point, single, singleColor, ref adlerA, ref adlerB);
                y++;
            }
            EmitBlock(writer, new List<Run>(), true);
            WritePng(path, width, height, writer.ToArray(), adlerA, adlerB);
        }

        private static void AddDirtyRow(BitWriter writer, int width, int backgroundMode, List<ChangePoint> points, int start, int end, bool single, int singleColor, ref uint adlerA, ref uint adlerB)
        {
            List<Run> runs = new List<Run>();
            int rowLength = 1 + (width * 3);
            int position = 0;
            for (int index = start; index < end; index++)
            {
                int at = 1 + (points[index].X * 3);
                if (at > position)
                    AddBackground(runs, backgroundMode, position, at);
                int packed = single ? singleColor : points[index].Color;
                AddRun(runs, (byte)((packed >> 16) & 255), 1);
                AddRun(runs, (byte)((packed >> 8) & 255), 1);
                AddRun(runs, (byte)(packed & 255), 1);
                position = at + 3;
            }
            if (position < rowLength)
                AddBackground(runs, backgroundMode, position, rowLength);
            EmitBlock(writer, runs, false);
            AdlerRuns(ref adlerA, ref adlerB, runs);
        }

        private static void AddBackground(List<Run> runs, int backgroundMode, int from, int to)
        {
            if (to <= from)
                return;
            if (backgroundMode != 1)
            {
                AddRun(runs, 0, to - from);
                return;
            }
            int cursor = from;
            if (cursor == 0)
            {
                AddRun(runs, 0, 1);
                cursor = 1;
            }
            if (cursor < to)
                AddRun(runs, 255, to - cursor);
        }

        private static BitPattern GetCleanPattern(int width, int backgroundMode)
        {
            long key = ((long)width << 2) | (uint)(backgroundMode == 1 ? 1 : 0);
            lock (PatternGate)
            {
                BitPattern existing;
                if (CleanPatterns.TryGetValue(key, out existing))
                    return existing;
            }
            List<Run> runs = new List<Run>();
            if (backgroundMode == 1)
            {
                AddRun(runs, 0, 1);
                AddRun(runs, 255, width * 3);
            }
            else
                AddRun(runs, 0, 1 + (width * 3));
            BitWriter writer = new BitWriter();
            EmitBlock(writer, runs, false);
            BitPattern pattern = writer.ToPattern();
            lock (PatternGate)
            {
                CleanPatterns[key] = pattern;
            }
            return pattern;
        }

        private static void WriteStreamPng(string path, BmpSurface source, List<ChangePoint> points, bool single, int singleColor)
        {
            int width = source.Width;
            int height = source.Height;
            byte[] row = new byte[1 + (width * 3)];
            MemoryStream deflate = new MemoryStream();
            uint adlerA = 1;
            uint adlerB = 0;
            using (DeflateStream compressor = new DeflateStream(deflate, CompressionLevel.Fastest, true))
            {
                int point = 0;
                for (int y = 0; y < height; y++)
                {
                    source.CopyPngRow(y, row);
                    while (point < points.Count && points[point].Y == y)
                    {
                        int packed = single ? singleColor : points[point].Color;
                        int offset = 1 + (points[point].X * 3);
                        row[offset] = (byte)((packed >> 16) & 255);
                        row[offset + 1] = (byte)((packed >> 8) & 255);
                        row[offset + 2] = (byte)(packed & 255);
                        point++;
                    }
                    compressor.Write(row, 0, row.Length);
                    AdlerBuffer(ref adlerA, ref adlerB, row, row.Length);
                }
            }
            WritePng(path, width, height, deflate.ToArray(), adlerA, adlerB);
        }

        private static void WritePng(string path, int width, int height, byte[] deflate, uint adlerA, uint adlerB)
        {
            uint adler = (adlerB << 16) | adlerA;
            byte[] zlib = new byte[deflate.Length + 6];
            zlib[0] = 0x78;
            zlib[1] = 0x01;
            Buffer.BlockCopy(deflate, 0, zlib, 2, deflate.Length);
            WriteBe(zlib, zlib.Length - 4, adler);
            byte[] ihdr = new byte[13];
            WriteBe(ihdr, 0, (uint)width);
            WriteBe(ihdr, 4, (uint)height);
            ihdr[8] = 8;
            ihdr[9] = 2;
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                stream.Write(signature, 0, signature.Length);
                WriteChunk(stream, "IHDR", ihdr);
                WriteChunk(stream, "IDAT", zlib);
                WriteChunk(stream, "IEND", new byte[0]);
            }
        }

        private static void WriteChunk(FileStream stream, string type, byte[] data)
        {
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            byte[] length = new byte[4];
            WriteBe(length, 0, (uint)data.Length);
            stream.Write(length, 0, 4);
            stream.Write(typeBytes, 0, 4);
            if (data.Length > 0)
                stream.Write(data, 0, data.Length);
            uint crc = 0xFFFFFFFFu;
            crc = UpdateCrc(crc, typeBytes, 0, 4);
            crc = UpdateCrc(crc, data, 0, data.Length);
            crc ^= 0xFFFFFFFFu;
            byte[] crcBytes = new byte[4];
            WriteBe(crcBytes, 0, crc);
            stream.Write(crcBytes, 0, 4);
        }

        private static uint UpdateCrc(uint crc, byte[] data, int offset, int count)
        {
            for (int i = 0; i < count; i++)
                crc = CrcTable[(crc ^ data[offset + i]) & 255] ^ (crc >> 8);
            return crc;
        }

        private static void EmitBlock(BitWriter writer, List<Run> runs, bool finalBlock)
        {
            writer.WriteLsb(finalBlock ? 1 : 0, 1);
            writer.WriteLsb(1, 2);
            for (int i = 0; i < runs.Count; i++)
                EmitRun(writer, runs[i].Value, runs[i].Count);
            writer.WriteHuff(0, 7);
        }

        private static void EmitRun(BitWriter writer, byte value, int count)
        {
            if (count <= 0)
                return;
            int code;
            int bits;
            LiteralCode(value, out code, out bits);
            writer.WriteHuff(code, bits);
            int left = count - 1;
            while (left > 0)
            {
                int length = left >= 258 ? 258 : left;
                if (length < 3)
                {
                    for (int i = 0; i < length; i++)
                    {
                        LiteralCode(value, out code, out bits);
                        writer.WriteHuff(code, bits);
                    }
                    break;
                }
                int extraBits = LengthExtraBits[length];
                LiteralCode(LengthSymbol[length], out code, out bits);
                writer.WriteHuff(code, bits);
                if (extraBits > 0)
                    writer.WriteLsb(length - LengthExtraBase[length], extraBits);
                writer.WriteHuff(0, 5);
                left -= length;
            }
        }

        private static void LiteralCode(int symbol, out int code, out int bits)
        {
            if (symbol <= 143)
            {
                code = 48 + symbol;
                bits = 8;
                return;
            }
            if (symbol <= 255)
            {
                code = 400 + (symbol - 144);
                bits = 9;
                return;
            }
            if (symbol <= 279)
            {
                code = symbol - 256;
                bits = 7;
                return;
            }
            code = 192 + (symbol - 280);
            bits = 8;
        }

        private static void AddRun(List<Run> runs, byte value, int count)
        {
            if (count <= 0)
                return;
            if (runs.Count > 0)
            {
                Run last = runs[runs.Count - 1];
                if (last.Value == value)
                {
                    last.Count += count;
                    runs[runs.Count - 1] = last;
                    return;
                }
            }
            Run run = new Run();
            run.Value = value;
            run.Count = count;
            runs.Add(run);
        }

        private static void AdlerClean(ref uint a, ref uint b, int width, int backgroundMode, int rowCount)
        {
            if (rowCount <= 0)
                return;
            if (backgroundMode != 1)
            {
                AdlerAdd(ref a, ref b, 0, (long)(1 + (width * 3)) * rowCount);
                return;
            }
            for (int i = 0; i < rowCount; i++)
            {
                AdlerAdd(ref a, ref b, 0, 1);
                AdlerAdd(ref a, ref b, 255, width * 3);
            }
        }

        private static void AdlerRuns(ref uint a, ref uint b, List<Run> runs)
        {
            for (int i = 0; i < runs.Count; i++)
                AdlerAdd(ref a, ref b, runs[i].Value, runs[i].Count);
        }

        private static void AdlerAdd(ref uint a, ref uint b, byte value, long count)
        {
            const int Mod = 65521;
            while (count > 0)
            {
                int n = count > 3800 ? 3800 : (int)count;
                long aa = (long)a + ((long)value * n);
                long bb = (long)b + ((long)n * a) + (((long)value * n * (n + 1)) / 2);
                a = (uint)(aa % Mod);
                b = (uint)(bb % Mod);
                count -= n;
            }
        }

        private static void AdlerBuffer(ref uint a, ref uint b, byte[] data, int length)
        {
            const int Mod = 65521;
            int index = 0;
            while (index < length)
            {
                int n = length - index;
                if (n > 3800)
                    n = 3800;
                ulong aa = a;
                ulong bb = b;
                for (int k = 0; k < n; k++)
                {
                    aa += data[index++];
                    bb += aa;
                }
                a = (uint)(aa % (uint)Mod);
                b = (uint)(bb % (uint)Mod);
            }
        }

        private static void WriteLe(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteBe(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }
    }

    internal sealed class BitWriter
    {
        private readonly List<byte> bytes = new List<byte>();
        private int acc;
        private int nbit;
        private int bitLength;

        public void WriteLsb(int value, int count)
        {
            while (count > 0)
            {
                int room = 8 - nbit;
                int take = count < room ? count : room;
                acc |= (value & ((1 << take) - 1)) << nbit;
                nbit += take;
                bitLength += take;
                value >>= take;
                count -= take;
                if (nbit == 8)
                {
                    bytes.Add((byte)acc);
                    acc = 0;
                    nbit = 0;
                }
            }
        }

        public void WriteHuff(int code, int nbits)
        {
            int reversed = 0;
            for (int i = 0; i < nbits; i++)
            {
                if ((code & (1 << (nbits - 1 - i))) != 0)
                    reversed |= 1 << i;
            }
            WriteLsb(reversed, nbits);
        }

        public void AppendPattern(BitPattern pattern, int times)
        {
            byte[] data = pattern.Data;
            for (int time = 0; time < times; time++)
            {
                int left = pattern.BitLength;
                int position = 0;
                while (left > 0)
                {
                    int take = left > 16 ? 16 : left;
                    int value = 0;
                    for (int i = 0; i < take; i++)
                    {
                        int bitPos = position + i;
                        int bit = (data[bitPos >> 3] >> (bitPos & 7)) & 1;
                        value |= bit << i;
                    }
                    WriteLsb(value, take);
                    position += take;
                    left -= take;
                }
            }
        }

        public BitPattern ToPattern()
        {
            BitPattern pattern = new BitPattern();
            pattern.Data = ToArray();
            pattern.BitLength = bitLength;
            return pattern;
        }

        public byte[] ToArray()
        {
            int size = bytes.Count;
            if (nbit > 0)
                size++;
            byte[] data = new byte[size];
            for (int i = 0; i < bytes.Count; i++)
                data[i] = bytes[i];
            if (nbit > 0)
                data[bytes.Count] = (byte)acc;
            return data;
        }
    }
}
