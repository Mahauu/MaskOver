using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace MaskOver
{
    internal static class SelfTest
    {
        public static int Run()
        {
            string directory = Path.Combine(Path.GetTempPath(), "MaskOverSelfTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string satellitePath = Path.Combine(directory, "satellite.bmp");
                string maskPath = Path.Combine(directory, "mask.bmp");
                string originalMaskPath = Path.Combine(directory, "original-mask.bmp");
                string changesPath = Path.Combine(directory, "changes.bmp");
                string layersPath = Path.Combine(directory, "layers.cfg");
                string englishSettingsPath = Path.Combine(directory, "MaskOver.ini");
                File.WriteAllText(englishSettingsPath, "Language=en\r\nBrushRadius=8\r\n");
                if (AppSettings.Load(englishSettingsPath).Language != "en")
                    return 34;
                using (Bitmap satellite = new Bitmap(256, 256, PixelFormat.Format24bppRgb))
                using (Bitmap mask = new Bitmap(256, 256, PixelFormat.Format24bppRgb))
                {
                    using (Graphics graphics = Graphics.FromImage(satellite)) graphics.Clear(Color.FromArgb(40, 80, 120));
                    using (Graphics graphics = Graphics.FromImage(mask)) graphics.Clear(Color.FromArgb(0, 255, 0));
                    satellite.Save(satellitePath, ImageFormat.Bmp);
                    mask.Save(maskPath, ImageFormat.Bmp);
                }
                File.Copy(maskPath, originalMaskPath);
                File.Copy(maskPath, changesPath);
                File.WriteAllText(layersPath, "class Legend\r\n{\r\n class Colors\r\n {\r\n  grass[]={0,255,0};\r\n  concrete1[]={255,0,255};\r\n };\r\n};\r\n");
                List<TerrainLayer> layers = LayersParser.Parse(layersPath);
                if (layers.Count != 2 || layers[1].Name != "concrete1")
                    return 11;

                using (BmpSurface satellite = new BmpSurface(satellitePath, false))
                using (BmpSurface mask = new BmpSurface(maskPath, true))
                using (BmpSurface originalMask = new BmpSurface(originalMaskPath, false))
                using (BmpSurface changes = new BmpSurface(changesPath, true))
                {
                    Dictionary<long, int> original = new Dictionary<long, int>();
                    int magenta = (255 << 16) | 255;
                    mask.PaintCircle(128.0, 128.0, 10.0, 256.0, 256.0, magenta, original);
                    int center = mask.ReadPacked(127, 128);
                    if (center != magenta || original.Count < 70)
                        return 12;
                    Dictionary<long, int> erased = new Dictionary<long, int>();
                    mask.RestoreCircleFrom(originalMask, 128.0, 128.0, 3.0, 256.0, 256.0, erased);
                    if (mask.ReadPacked(127, 128) != (255 << 8) || erased.Count < 5)
                        return 23;
                    Dictionary<long, int> squareOriginal = new Dictionary<long, int>();
                    int blue = 255;
                    mask.PaintSquare(64.0, 64.0, 5.0, 256.0, 256.0, blue, squareOriginal);
                    if (squareOriginal.Count != 25)
                        return 18;
                    Dictionary<long, int> blocked = new Dictionary<long, int>();
                    mask.PaintCircle(200.0, 200.0, 8.0, 256.0, 256.0, magenta, blocked, true, blue);
                    if (blocked.Count != 0)
                        return 19;
                    Dictionary<long, int> organicOriginal = new Dictionary<long, int>();
                    int orange = (255 << 16) | (125 << 8);
                    int green = 255 << 8;
                    mask.PaintOrganic(200.0, 200.0, 10.0, 256.0, 256.0, orange, organicOriginal, true, green);
                    if (organicOriginal.Count < 40)
                        return 20;
                    Dictionary<long, int> twoPixelSquare = new Dictionary<long, int>();
                    mask.PaintSquare(50.0, 20.0, 2.0, 256.0, 256.0, magenta, twoPixelSquare);
                    if (twoPixelSquare.Count != 4)
                        return 30;
                    Dictionary<long, int> fourPixelSquare = new Dictionary<long, int>();
                    mask.PaintSquare(60.0, 20.0, 4.0, 256.0, 256.0, magenta, fourPixelSquare);
                    if (fourPixelSquare.Count != 16)
                        return 31;
                    Dictionary<long, int> twoPixelCircle = new Dictionary<long, int>();
                    mask.PaintCircle(70.0, 20.0, 2.0, 256.0, 256.0, magenta, twoPixelCircle);
                    if (twoPixelCircle.Count != 4)
                        return 32;
                    Dictionary<long, int> fourPixelCircle = new Dictionary<long, int>();
                    mask.PaintCircle(80.0, 20.0, 4.0, 256.0, 256.0, magenta, fourPixelCircle);
                    if (fourPixelCircle.Count != 12)
                        return 33;
                    Dictionary<long, int> onePixelCircle = new Dictionary<long, int>();
                    mask.PaintCircle(20.0, 20.0, 1.0, 256.0, 256.0, magenta, onePixelCircle);
                    if (onePixelCircle.Count != 1)
                        return 26;
                    Dictionary<long, int> onePixelSquare = new Dictionary<long, int>();
                    mask.PaintSquare(30.0, 20.0, 1.0, 256.0, 256.0, magenta, onePixelSquare);
                    if (onePixelSquare.Count != 1)
                        return 27;
                    Dictionary<long, int> onePixelOrganic = new Dictionary<long, int>();
                    mask.PaintOrganic(40.0, 20.0, 1.0, 256.0, 256.0, magenta, onePixelOrganic);
                    if (onePixelOrganic.Count != 1)
                        return 28;
                    Dictionary<long, int> onePixelErased = new Dictionary<long, int>();
                    mask.RestoreCircleFrom(originalMask, 20.0, 20.0, 1.0, 256.0, 256.0, onePixelErased);
                    if (onePixelErased.Count != 1)
                        return 29;
                    Dictionary<long, int> weakOriginal = new Dictionary<long, int>();
                    mask.PaintCircle(40.0, 200.0, 10.0, 256.0, 256.0, magenta, weakOriginal, false, 0, 0.25);
                    if (weakOriginal.Count < 10 || weakOriginal.Count > 40)
                        return 24;
                    Dictionary<long, int> sprayOriginal = new Dictionary<long, int>();
                    mask.PaintSquare(120.0, 40.0, 16.0, 256.0, 256.0, magenta, sprayOriginal, false, 0, 0.25);
                    int firstSprayCount = sprayOriginal.Count;
                    if (firstSprayCount < 35 || firstSprayCount > 95)
                        return 35;
                    for (int dab = 0; dab < 12; dab++)
                        mask.PaintSquare(120.0, 40.0, 16.0, 256.0, 256.0, magenta, sprayOriginal, false, 0, 0.25);
                    if (sprayOriginal.Count <= firstSprayCount || sprayOriginal.Count < 220)
                        return 36;
                    long changed = mask.WriteDifferencesAgainst(originalMask, changes);
                    if (changed < 140 || changes.ReadPacked(0, 0) != 0 || changes.ReadPacked(64, 191) != blue)
                        return 25;
                    string blocksPath = Path.Combine(directory, "blocks-mask.bmp");
                    File.Copy(originalMaskPath, blocksPath);
                    using (BmpSurface blocks = new BmpSurface(blocksPath, true))
                    {
                        for (int y = 48; y < 64; y++)
                            for (int x = 32; x < 48; x++)
                                blocks.WritePackedAtOffset(blocks.PixelOffset(x, y), blue);
                        List<TerrainPreviewCell> wholeBlock = TerrainPreviewBuilder.Build(blocks, originalMask, 128.0, 128.0, 200.0, 256.0, 256.0);
                        if (wholeBlock.Count != 1 || Math.Abs(wholeBlock[0].Width / (256.0 / 255.0) - 16.0) > 0.001)
                            return 39;
                        for (int y = 48; y < 52; y++)
                            for (int x = 48; x < 52; x++)
                                blocks.WritePackedAtOffset(blocks.PixelOffset(x, y), magenta);
                        blocks.WritePackedAtOffset(blocks.PixelOffset(35, 51), magenta);
                        List<TerrainPreviewCell> splitBlocks = TerrainPreviewBuilder.Build(blocks, originalMask, 128.0, 128.0, 200.0, 256.0, 256.0);
                        int[,] rendered = new int[256, 256];
                        for (int y = 0; y < 256; y++)
                            for (int x = 0; x < 256; x++) rendered[x, y] = -1;
                        double step = 256.0 / 255.0;
                        foreach (TerrainPreviewCell cell in splitBlocks)
                        {
                            int size = (int)Math.Round(cell.Width / step);
                            int left = (int)Math.Round(cell.X / step - (size - 1) * 0.5);
                            int top = (int)Math.Round(255 - cell.Z / step - (size - 1) * 0.5);
                            for (int y = top; y < top + size; y++)
                                for (int x = left; x < left + size; x++)
                                {
                                    if (rendered[x, y] != -1) return 40;
                                    rendered[x, y] = cell.Color;
                                }
                        }
                        for (int y = 0; y < 256; y++)
                            for (int x = 0; x < 256; x++)
                            {
                                int expected = blocks.ReadPacked(x, y) == originalMask.ReadPacked(x, y) ? -1 : blocks.ReadPacked(x, y);
                                if (rendered[x, y] != expected) return 41;
                            }
                    }
                    string largeBlocksPath = Path.Combine(directory, "large-blocks-mask.bmp");
                    File.Copy(originalMaskPath, largeBlocksPath);
                    using (BmpSurface largeBlocks = new BmpSurface(largeBlocksPath, true))
                    {
                        for (int y = 64; y < 128; y++)
                            for (int x = 64; x < 128; x++)
                                largeBlocks.WritePackedAtOffset(largeBlocks.PixelOffset(x, y), blue);
                        for (int y = 64; y < 96; y++)
                            for (int x = 128; x < 160; x++)
                                largeBlocks.WritePackedAtOffset(largeBlocks.PixelOffset(x, y), magenta);
                        List<TerrainPreviewCell> largeCells = TerrainPreviewBuilder.Build(largeBlocks, originalMask, 128.0, 128.0, 200.0, 256.0, 256.0);
                        if (largeCells.Count != 2) return 42;
                        double step = 256.0 / 255.0;
                        if (largeCells[0].Color != blue || Math.Abs(largeCells[0].Width / step - 64.0) > 0.001
                            || largeCells[1].Color != magenta || Math.Abs(largeCells[1].Width / step - 32.0) > 0.001)
                            return 43;
                    }
                    using (Bitmap preview = satellite.RenderPreview(mask, 128.0, 128.0, 256.0, 256.0, 100, 200, 0.6))
                    {
                        if (preview.Width != 200 || preview.Height != 200)
                            return 13;
                    }
                    mask.Flush();
                }

                WorldPosition position;
                if (!PositionParser.TryParse("Conn Pos=(12574.4, 39.8, 1394.4) Tile=(1676,159)", "test", out position))
                    return 14;
                if (Math.Abs(position.X - 12574.4) > 0.01 || Math.Abs(position.Z - 1394.4) > 0.01)
                    return 15;
                if (!PositionParser.TryParse("aCONN Pos=(13352.8,25.6,1347.3)", "bridge", out position))
                    return 16;
                if (Math.Abs(position.X - 13352.8) > 0.01 || Math.Abs(position.Z - 1347.3) > 0.01)
                    return 17;
                if (!PositionParser.TryParse("<100 20 200>\r\n<1 0 0>", "bridge", out position))
                    return 21;
                if (!position.HasDirection || Math.Abs(position.HeadingDegrees - 90.0) > 0.01)
                    return 22;
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "MaskOverSelfTest-error.txt"), ex.ToString()); }
                catch { }
                return 99;
            }
            finally
            {
                try { Directory.Delete(directory, true); }
                catch { }
            }
        }
    }
}
