using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace MaskOver
{
    internal unsafe sealed class BmpSurface : IDisposable
    {
        private readonly FileStream stream;
        private readonly MemoryMappedFile map;
        private readonly MemoryMappedViewAccessor view;
        private byte* pointer;
        private readonly bool writable;
        private readonly long pixelOffset;
        private readonly int stride;
        private readonly Random sprayRandom = new Random();

        public readonly int Width;
        public readonly int Height;

        public BmpSurface(string path, bool writableAccess)
        {
            writable = writableAccess;
            FileAccess access = writable ? FileAccess.ReadWrite : FileAccess.Read;
            FileShare share = writable ? FileShare.Read : FileShare.ReadWrite | FileShare.Delete;
            stream = new FileStream(path, FileMode.Open, access, share);
            BinaryReader reader = new BinaryReader(stream);
            if (reader.ReadUInt16() != 0x4D42)
                throw new InvalidDataException(path + Locale.T(" nie jest plikiem BMP.", " is not a BMP file."));
            stream.Position = 10;
            pixelOffset = reader.ReadUInt32();
            stream.Position = 18;
            Width = reader.ReadInt32();
            Height = reader.ReadInt32();
            stream.Position = 28;
            ushort bits = reader.ReadUInt16();
            if (bits != 24 || Width <= 0 || Height <= 0)
                throw new InvalidDataException(Locale.T("Obslugiwany jest tylko dodatni BMP 24-bit: ", "Only positive-height 24-bit BMP files are supported: ") + path);
            stride = ((Width * 3) + 3) & ~3;
            MemoryMappedFileAccess mapAccess = writable ? MemoryMappedFileAccess.ReadWrite : MemoryMappedFileAccess.Read;
            map = MemoryMappedFile.CreateFromFile(stream, null, 0, mapAccess, HandleInheritability.None, false);
            view = map.CreateViewAccessor(0, 0, mapAccess);
            byte* acquired = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref acquired);
            pointer = acquired + view.PointerOffset;
        }

        private long OffsetFromTop(int x, int yFromTop)
        {
            int fileRow = Height - 1 - yFromTop;
            return pixelOffset + ((long)fileRow * stride) + ((long)x * 3L);
        }

        public int ReadPackedAtOffset(long offset)
        {
            byte blue = pointer[offset];
            byte green = pointer[offset + 1];
            byte red = pointer[offset + 2];
            return (red << 16) | (green << 8) | blue;
        }

        public void WritePackedAtOffset(long offset, int packed)
        {
            if (!writable)
                throw new InvalidOperationException(Locale.T("Powierzchnia jest tylko do odczytu.", "The image is read-only."));
            pointer[offset] = (byte)(packed & 255);
            pointer[offset + 1] = (byte)((packed >> 8) & 255);
            pointer[offset + 2] = (byte)((packed >> 16) & 255);
        }

        public int ReadPacked(int x, int yFromTop)
        {
            if (x < 0 || x >= Width || yFromTop < 0 || yFromTop >= Height)
                return 0;
            return ReadPackedAtOffset(OffsetFromTop(x, yFromTop));
        }

        public long PixelOffset(int x, int yFromTop)
        {
            return OffsetFromTop(x, yFromTop);
        }

        public void Flush()
        {
            if (writable)
                view.Flush();
        }

        private bool PassesStrength(int x, int y, double strength)
        {
            if (strength >= 0.999999)
                return true;
            // A fresh roll for every dab lets a held brush build up like a spray can.
            return sprayRandom.NextDouble() < strength;
        }

        public long WriteDifferencesAgainst(BmpSurface original, BmpSurface output)
        {
            return WriteSelectedDifferencesAgainst(original, output, null, 0);
        }

        public HashSet<int> FindChangedColors(BmpSurface original)
        {
            if (original.Width != Width || original.Height != Height)
                throw new InvalidDataException(Locale.T("Maska robocza i oryginalna mają różne wymiary.", "The working and original masks have different dimensions."));
            HashSet<int> colors = new HashSet<int>();
            for (int y = 0; y < Height; y++)
            {
                long currentOffset = OffsetFromTop(0, y);
                long originalOffset = original.OffsetFromTop(0, y);
                for (int x = 0; x < Width; x++)
                {
                    int currentColor = ReadPackedAtOffset(currentOffset);
                    if (currentColor != original.ReadPackedAtOffset(originalOffset))
                        colors.Add(currentColor);
                    currentOffset += 3;
                    originalOffset += 3;
                }
            }
            return colors;
        }

        public long WriteSelectedDifferencesAgainst(BmpSurface original, BmpSurface output, HashSet<int> selectedColors, int backgroundMode)
        {
            if (original.Width != Width || original.Height != Height || output.Width != Width || output.Height != Height)
                throw new InvalidDataException(Locale.T("Maska robocza, oryginal i eksport musza miec ten sam rozmiar.", "Working mask, source mask and export must have the same dimensions."));
            if (!output.writable)
                throw new InvalidOperationException(Locale.T("Plik eksportu jest tylko do odczytu.", "The export file is read-only."));

            long changed = 0;
            for (int y = 0; y < Height; y++)
            {
                long currentOffset = OffsetFromTop(0, y);
                long originalOffset = original.OffsetFromTop(0, y);
                long outputOffset = output.OffsetFromTop(0, y);
                for (int x = 0; x < Width; x++)
                {
                    int currentColor = ReadPackedAtOffset(currentOffset);
                    bool differs = currentColor != original.ReadPackedAtOffset(originalOffset);
                    bool selected = selectedColors == null || selectedColors.Contains(currentColor);
                    int backgroundColor = backgroundMode == 1 ? 0xFFFFFF : backgroundMode == 2 ? original.ReadPackedAtOffset(originalOffset) : 0;
                    int outputColor = differs && selected ? currentColor : backgroundColor;
                    output.pointer[outputOffset] = (byte)(outputColor & 255);
                    output.pointer[outputOffset + 1] = (byte)((outputColor >> 8) & 255);
                    output.pointer[outputOffset + 2] = (byte)((outputColor >> 16) & 255);
                    if (differs && selected)
                        changed++;
                    currentOffset += 3;
                    originalOffset += 3;
                    outputOffset += 3;
                }
            }
            output.Flush();
            return changed;
        }

        public Bitmap ToBitmap()
        {
            Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                byte* output = (byte*)data.Scan0;
                for (int y = 0; y < Height; y++)
                {
                    byte* row = output + ((long)y * data.Stride);
                    long sourceOffset = OffsetFromTop(0, y);
                    for (int x = 0; x < Width; x++)
                    {
                        row[x * 3] = pointer[sourceOffset];
                        row[(x * 3) + 1] = pointer[sourceOffset + 1];
                        row[(x * 3) + 2] = pointer[sourceOffset + 2];
                        sourceOffset += 3;
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        /*
         * Kept as a separate implementation point so old callers retain the
         * original all-colours/black-background behavior.
         */
        private long LegacyWriteDifferencesAgainst(BmpSurface original, BmpSurface output)
        {
            long changed = 0;
            for (int y = 0; y < Height; y++)
            {
                long currentOffset = OffsetFromTop(0, y);
                long originalOffset = original.OffsetFromTop(0, y);
                long outputOffset = output.OffsetFromTop(0, y);
                for (int x = 0; x < Width; x++)
                {
                    bool differs = pointer[currentOffset] != original.pointer[originalOffset]
                        || pointer[currentOffset + 1] != original.pointer[originalOffset + 1]
                        || pointer[currentOffset + 2] != original.pointer[originalOffset + 2];
                    if (differs)
                    {
                        output.pointer[outputOffset] = pointer[currentOffset];
                        output.pointer[outputOffset + 1] = pointer[currentOffset + 1];
                        output.pointer[outputOffset + 2] = pointer[currentOffset + 2];
                        changed++;
                    }
                    else
                    {
                        output.pointer[outputOffset] = 0;
                        output.pointer[outputOffset + 1] = 0;
                        output.pointer[outputOffset + 2] = 0;
                    }
                    currentOffset += 3;
                    originalOffset += 3;
                    outputOffset += 3;
                }
            }
            output.Flush();
            return changed;
        }

        public Bitmap RenderPreview(BmpSurface mask, double worldX, double worldZ, double worldWidth, double worldHeight, int spanMeters, int outputSize, double maskOpacity, double directionX = 0.0, double directionZ = 1.0)
        {
            Bitmap bitmap = new Bitmap(outputSize, outputSize, PixelFormat.Format24bppRgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, outputSize, outputSize), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                byte* output = (byte*)data.Scan0;
                double directionLength = Math.Sqrt((directionX * directionX) + (directionZ * directionZ));
                if (directionLength < 0.0001)
                {
                    directionX = 0.0;
                    directionZ = 1.0;
                }
                else
                {
                    directionX /= directionLength;
                    directionZ /= directionLength;
                }
                for (int py = 0; py < outputSize; py++)
                {
                    double forward = ((outputSize * 0.5) - (py + 0.5)) * spanMeters / outputSize;
                    byte* row = output + ((long)py * data.Stride);
                    for (int px = 0; px < outputSize; px++)
                    {
                        double right = ((px + 0.5) - (outputSize * 0.5)) * spanMeters / outputSize;
                        double x = worldX + (right * directionZ) + (forward * directionX);
                        double z = worldZ - (right * directionX) + (forward * directionZ);
                        int sourceX = (int)Math.Round(x * (Width - 1) / worldWidth);
                        int sourceY = Height - 1 - (int)Math.Round(z * (Height - 1) / worldHeight);
                        int satelliteColor = mask == this ? mask.ReadPacked(sourceX, sourceY) : ReadPacked(sourceX, sourceY);
                        int maskColor = mask.ReadPacked(sourceX, sourceY);
                        int satRed = (satelliteColor >> 16) & 255;
                        int satGreen = (satelliteColor >> 8) & 255;
                        int satBlue = satelliteColor & 255;
                        int maskRed = (maskColor >> 16) & 255;
                        int maskGreen = (maskColor >> 8) & 255;
                        int maskBlue = maskColor & 255;
                        int red = (int)Math.Round((satRed * (1.0 - maskOpacity)) + (maskRed * maskOpacity));
                        int green = (int)Math.Round((satGreen * (1.0 - maskOpacity)) + (maskGreen * maskOpacity));
                        int blue = (int)Math.Round((satBlue * (1.0 - maskOpacity)) + (maskBlue * maskOpacity));
                        row[(px * 3)] = (byte)blue;
                        row[(px * 3) + 1] = (byte)green;
                        row[(px * 3) + 2] = (byte)red;
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        public Bitmap RenderFullMask(int outputWidth, int outputHeight)
        {
            Bitmap bitmap = new Bitmap(outputWidth, outputHeight, PixelFormat.Format24bppRgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, outputWidth, outputHeight), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                byte* output = (byte*)data.Scan0;
                for (int py = 0; py < outputHeight; py++)
                {
                    int sourceY = (int)Math.Round((double)py * (Height - 1) / Math.Max(1, outputHeight - 1));
                    byte* row = output + ((long)py * data.Stride);
                    for (int px = 0; px < outputWidth; px++)
                    {
                        int sourceX = (int)Math.Round((double)px * (Width - 1) / Math.Max(1, outputWidth - 1));
                        int packed = ReadPacked(sourceX, sourceY);
                        row[px * 3] = (byte)(packed & 255);
                        row[(px * 3) + 1] = (byte)((packed >> 8) & 255);
                        row[(px * 3) + 2] = (byte)((packed >> 16) & 255);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        public void PaintCircle(double worldX, double worldZ, double radiusMeters, double worldWidth, double worldHeight, int packedColor, Dictionary<long, int> originals, bool replaceOnly = false, int requiredColor = 0, double strength = 1.0)
        {
            int centerX = (int)Math.Round(worldX * (Width - 1) / worldWidth);
            int centerY = Height - 1 - (int)Math.Round(worldZ * (Height - 1) / worldHeight);
            int size = Math.Max(1, (int)Math.Round(radiusMeters));
            int left = centerX - (size / 2);
            int top = centerY - (size / 2);
            double shapeCenterX = left + ((size - 1) * 0.5);
            double shapeCenterY = top + ((size - 1) * 0.5);
            double radius = size * 0.5;
            int minY = Math.Max(0, top);
            int maxY = Math.Min(Height - 1, top + size - 1);
            for (int y = minY; y <= maxY; y++)
            {
                int minX = Math.Max(0, left);
                int maxX = Math.Min(Width - 1, left + size - 1);
                for (int x = minX; x <= maxX; x++)
                {
                    double dx = x - shapeCenterX;
                    double dy = y - shapeCenterY;
                    if ((dx * dx) + (dy * dy) > radius * radius)
                        continue;
                    if (!PassesStrength(x, y, strength))
                        continue;
                    long offset = OffsetFromTop(x, y);
                    int existing = ReadPackedAtOffset(offset);
                    if (replaceOnly && existing != requiredColor)
                        continue;
                    if (existing == packedColor)
                        continue;
                    if (!originals.ContainsKey(offset))
                        originals.Add(offset, existing);
                    WritePackedAtOffset(offset, packedColor);
                }
            }
        }

        public void PaintSquare(double worldX, double worldZ, double radiusMeters, double worldWidth, double worldHeight, int packedColor, Dictionary<long, int> originals, bool replaceOnly = false, int requiredColor = 0, double strength = 1.0)
        {
            int centerX = (int)Math.Round(worldX * (Width - 1) / worldWidth);
            int centerY = Height - 1 - (int)Math.Round(worldZ * (Height - 1) / worldHeight);
            int size = Math.Max(1, (int)Math.Round(radiusMeters));
            int minX = Math.Max(0, centerX - (size / 2));
            int maxX = Math.Min(Width - 1, centerX - (size / 2) + size - 1);
            int minY = Math.Max(0, centerY - (size / 2));
            int maxY = Math.Min(Height - 1, centerY - (size / 2) + size - 1);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!PassesStrength(x, y, strength))
                        continue;
                    long offset = OffsetFromTop(x, y);
                    int existing = ReadPackedAtOffset(offset);
                    if (replaceOnly && existing != requiredColor)
                        continue;
                    if (existing == packedColor)
                        continue;
                    if (!originals.ContainsKey(offset))
                        originals.Add(offset, existing);
                    WritePackedAtOffset(offset, packedColor);
                }
            }
        }

        public void PaintOrganic(double worldX, double worldZ, double radiusMeters, double worldWidth, double worldHeight, int packedColor, Dictionary<long, int> originals, bool replaceOnly = false, int requiredColor = 0, double strength = 1.0)
        {
            if (radiusMeters <= 2.0)
            {
                PaintSquare(worldX, worldZ, radiusMeters, worldWidth, worldHeight, packedColor, originals, replaceOnly, requiredColor, strength);
                return;
            }
            int centerX = (int)Math.Round(worldX * (Width - 1) / worldWidth);
            int centerY = Height - 1 - (int)Math.Round(worldZ * (Height - 1) / worldHeight);
            int size = Math.Max(1, (int)Math.Round(radiusMeters));
            int left = centerX - (size / 2);
            int top = centerY - (size / 2);
            double shapeCenterX = left + ((size - 1) * 0.5);
            double shapeCenterY = top + ((size - 1) * 0.5);
            double radius = size * 0.5;
            int minY = Math.Max(0, top);
            int maxY = Math.Min(Height - 1, top + size - 1);
            int minX = Math.Max(0, left);
            int maxX = Math.Min(Width - 1, left + size - 1);
            for (int y = minY; y <= maxY; y++)
            {
                double normalizedY = (y - shapeCenterY) / radius;
                for (int x = minX; x <= maxX; x++)
                {
                    double normalizedX = (x - shapeCenterX) / radius;
                    double distance = Math.Sqrt((normalizedX * normalizedX) + (normalizedY * normalizedY));
                    uint hash = unchecked(((uint)x * 73856093u) ^ ((uint)y * 19349663u));
                    double noise = (hash & 1023u) / 1023.0;
                    double edge = 0.76 + (noise * 0.34);
                    if (distance > edge)
                        continue;
                    if (!PassesStrength(x, y, strength))
                        continue;
                    long offset = OffsetFromTop(x, y);
                    int existing = ReadPackedAtOffset(offset);
                    if (replaceOnly && existing != requiredColor)
                        continue;
                    if (existing == packedColor)
                        continue;
                    if (!originals.ContainsKey(offset))
                        originals.Add(offset, existing);
                    WritePackedAtOffset(offset, packedColor);
                }
            }
        }

        private void RestorePixelFrom(BmpSurface source, int x, int y, Dictionary<long, int> originals)
        {
            long offset = OffsetFromTop(x, y);
            int existing = ReadPackedAtOffset(offset);
            int restored = source.ReadPacked(x, y);
            if (existing == restored)
                return;
            if (!originals.ContainsKey(offset))
                originals.Add(offset, existing);
            WritePackedAtOffset(offset, restored);
        }

        public void RestoreCircleFrom(BmpSurface source, double worldX, double worldZ, double radiusMeters, double worldWidth, double worldHeight, Dictionary<long, int> originals, double strength = 1.0)
        {
            int centerX = (int)Math.Round(worldX * (Width - 1) / worldWidth);
            int centerY = Height - 1 - (int)Math.Round(worldZ * (Height - 1) / worldHeight);
            int size = Math.Max(1, (int)Math.Round(radiusMeters));
            int left = centerX - (size / 2);
            int top = centerY - (size / 2);
            double shapeCenterX = left + ((size - 1) * 0.5);
            double shapeCenterY = top + ((size - 1) * 0.5);
            double radius = size * 0.5;
            int minY = Math.Max(0, top);
            int maxY = Math.Min(Height - 1, top + size - 1);
            for (int y = minY; y <= maxY; y++)
            {
                int minX = Math.Max(0, left);
                int maxX = Math.Min(Width - 1, left + size - 1);
                for (int x = minX; x <= maxX; x++)
                {
                    double dx = x - shapeCenterX;
                    double dy = y - shapeCenterY;
                    if ((dx * dx) + (dy * dy) > radius * radius)
                        continue;
                    if (PassesStrength(x, y, strength))
                        RestorePixelFrom(source, x, y, originals);
                }
            }
        }

        public void RestoreSquareFrom(BmpSurface source, double worldX, double worldZ, double radiusMeters, double worldWidth, double worldHeight, Dictionary<long, int> originals, double strength = 1.0)
        {
            int centerX = (int)Math.Round(worldX * (Width - 1) / worldWidth);
            int centerY = Height - 1 - (int)Math.Round(worldZ * (Height - 1) / worldHeight);
            int size = Math.Max(1, (int)Math.Round(radiusMeters));
            int minX = Math.Max(0, centerX - (size / 2));
            int maxX = Math.Min(Width - 1, centerX - (size / 2) + size - 1);
            int minY = Math.Max(0, centerY - (size / 2));
            int maxY = Math.Min(Height - 1, centerY - (size / 2) + size - 1);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (PassesStrength(x, y, strength))
                        RestorePixelFrom(source, x, y, originals);
                }
            }
        }

        public void RestoreOrganicFrom(BmpSurface source, double worldX, double worldZ, double radiusMeters, double worldWidth, double worldHeight, Dictionary<long, int> originals, double strength = 1.0)
        {
            if (radiusMeters <= 2.0)
            {
                RestoreSquareFrom(source, worldX, worldZ, radiusMeters, worldWidth, worldHeight, originals, strength);
                return;
            }
            int centerX = (int)Math.Round(worldX * (Width - 1) / worldWidth);
            int centerY = Height - 1 - (int)Math.Round(worldZ * (Height - 1) / worldHeight);
            int size = Math.Max(1, (int)Math.Round(radiusMeters));
            int left = centerX - (size / 2);
            int top = centerY - (size / 2);
            double shapeCenterX = left + ((size - 1) * 0.5);
            double shapeCenterY = top + ((size - 1) * 0.5);
            double radius = size * 0.5;
            int minY = Math.Max(0, top);
            int maxY = Math.Min(Height - 1, top + size - 1);
            int minX = Math.Max(0, left);
            int maxX = Math.Min(Width - 1, left + size - 1);
            for (int y = minY; y <= maxY; y++)
            {
                double normalizedY = (y - shapeCenterY) / radius;
                for (int x = minX; x <= maxX; x++)
                {
                    double normalizedX = (x - shapeCenterX) / radius;
                    double distance = Math.Sqrt((normalizedX * normalizedX) + (normalizedY * normalizedY));
                    uint hash = unchecked(((uint)x * 73856093u) ^ ((uint)y * 19349663u));
                    double noise = (hash & 1023u) / 1023.0;
                    double edge = 0.76 + (noise * 0.34);
                    if (distance <= edge && PassesStrength(x, y, strength))
                        RestorePixelFrom(source, x, y, originals);
                }
            }
        }

        public void Dispose()
        {
            if (pointer != null)
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
                pointer = null;
            }
            view.Dispose();
            map.Dispose();
            stream.Dispose();
        }
    }
}
