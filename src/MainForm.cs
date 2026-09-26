using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MaskOver
{
    internal sealed class PreviewControl : Control
    {
        private Bitmap image;
        public float BrushRadiusMeters;
        public int PreviewMeters = 600;
        public Color BrushColor = Color.Magenta;
        public int BrushShape;
        public float BrushRotationDegrees;
        public float BrushOffsetRightMeters;
        public float BrushOffsetForwardMeters;
        public bool FullMaskView;
        public event Action<Point> MiddleClicked;

        public PreviewControl()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(18, 20, 24);
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        public void SetImage(Bitmap replacement)
        {
            Bitmap old = image;
            image = replacement;
            if (old != null)
                old.Dispose();
            Invalidate();
        }

        /// <summary>
        /// Square letterboxed area used to draw the map without stretching.
        /// </summary>
        public Rectangle GetMapViewport()
        {
            int side = Math.Min(ClientSize.Width, ClientSize.Height);
            if (side < 2)
                return ClientRectangle;
            int left = (ClientSize.Width - side) / 2;
            int top = (ClientSize.Height - side) / 2;
            return new Rectangle(left, top, side, side);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Rectangle viewport = GetMapViewport();
            if (image != null)
                e.Graphics.DrawImage(image, viewport);
            if (FullMaskView)
                return;
            // Brush is positioned in the same square viewport so it stays aligned with the map.
            float scale = PreviewMeters > 0 ? (float)viewport.Width / PreviewMeters : 1.0f;
            float centerX = viewport.Left + viewport.Width * 0.5f + BrushOffsetRightMeters * scale;
            float centerY = viewport.Top + viewport.Height * 0.5f - BrushOffsetForwardMeters * scale;
            float radius = BrushRadiusMeters * scale;
            GraphicsState brushState = e.Graphics.Save();
            e.Graphics.TranslateTransform(centerX, centerY);
            e.Graphics.RotateTransform(BrushRotationDegrees);
            e.Graphics.TranslateTransform(-centerX, -centerY);
            using (Pen shadow = new Pen(Color.Black, 4.0f))
            using (Pen pen = new Pen(BrushColor, 2.0f))
            {
                if (BrushShape == 1)
                {
                    e.Graphics.DrawRectangle(shadow, centerX - radius, centerY - radius, radius * 2.0f, radius * 2.0f);
                    e.Graphics.DrawRectangle(pen, centerX - radius, centerY - radius, radius * 2.0f, radius * 2.0f);
                }
                else if (BrushShape == 2)
                {
                    PointF[] points = new PointF[32];
                    for (int index = 0; index < points.Length; index++)
                    {
                        double angle = index * Math.PI * 2.0 / points.Length;
                        double uneven = 0.88 + (0.08 * Math.Sin(index * 2.7)) + (0.04 * Math.Cos(index * 5.3));
                        float organicRadius = (float)(radius * uneven);
                        points[index] = new PointF(centerX + (float)Math.Cos(angle) * organicRadius, centerY + (float)Math.Sin(angle) * organicRadius);
                    }
                    e.Graphics.DrawPolygon(shadow, points);
                    e.Graphics.DrawPolygon(pen, points);
                }
                else
                {
                    e.Graphics.DrawEllipse(shadow, centerX - radius, centerY - radius, radius * 2.0f, radius * 2.0f);
                    e.Graphics.DrawEllipse(pen, centerX - radius, centerY - radius, radius * 2.0f, radius * 2.0f);
                }
            }
            e.Graphics.Restore(brushState);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Middle && MiddleClicked != null)
                MiddleClicked(e.Location);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && image != null)
            {
                image.Dispose();
                image = null;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class TerrainPreviewCell
    {
        public double X;
        public double Z;
        public double Width;
        public double Depth;
        public int Color;
    }

    internal sealed class TerrainPreviewLine
    {
        public double X1;
        public double Z1;
        public double X2;
        public double Z2;
        public int Color;
    }

    internal static class TerrainPreviewBuilder
    {
        public static void GetMaskTileCoordinates(BmpSurface mask, double worldX, double worldZ, double worldWidth, double worldHeight,
            int tilePixels, int overlapPixels, int tilesInRow, out int tileX, out int tileY)
        {
            double stepX = worldWidth / (mask.Width - 1);
            double stepZ = worldHeight / (mask.Height - 1);
            int advance = Math.Max(1, tilePixels - overlapPixels);
            int pixelX = Math.Max(0, Math.Min(mask.Width - 1, (int)Math.Round(worldX / stepX)));
            int pixelY = Math.Max(0, Math.Min(mask.Height - 1, mask.Height - 1 - (int)Math.Round(worldZ / stepZ)));
            tileX = Math.Min(Math.Max(0, tilesInRow - 1), pixelX / advance);
            tileY = Math.Min(Math.Max(0, tilesInRow - 1), pixelY / advance);
        }

        public static List<TerrainPreviewCell> Build(BmpSurface mask, BmpSurface original, double worldX, double worldZ, double range, double worldWidth, double worldHeight, bool changedOnly = true)
        {
            double stepX = worldWidth / (mask.Width - 1);
            double stepZ = worldHeight / (mask.Height - 1);
            int centerX = (int)Math.Round(worldX / stepX);
            int centerY = mask.Height - 1 - (int)Math.Round(worldZ / stepZ);
            int reachX = (int)Math.Ceiling(range / stepX) + 1;
            int reachY = (int)Math.Ceiling(range / stepZ) + 1;
            int minX = Math.Max(0, centerX - reachX);
            int maxX = Math.Min(mask.Width - 1, centerX + reachX);
            int minY = Math.Max(0, centerY - reachY);
            int maxY = Math.Min(mask.Height - 1, centerY + reachY);
            if (maxX < minX || maxY < minY) return new List<TerrainPreviewCell>();
            int[,] colors = new int[maxX - minX + 1, maxY - minY + 1];
            bool[,] covered = new bool[maxX - minX + 1, maxY - minY + 1];
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    double dx = x * stepX - worldX;
                    double dz = (mask.Height - 1 - y) * stepZ - worldZ;
                    int color = mask.ReadPacked(x, y);
                    colors[x - minX, y - minY] = dx * dx + dz * dz <= range * range && (!changedOnly || color != original.ReadPacked(x, y)) ? color : -1;
                }
            }

            List<TerrainPreviewCell> result = new List<TerrainPreviewCell>();
            for (int size = 64; size >= 1; size /= 2)
            {
                for (int y = (minY / size) * size; y <= maxY - size + 1; y += size)
                {
                    if (y < minY) continue;
                    for (int x = (minX / size) * size; x <= maxX - size + 1; x += size)
                    {
                        if (x < minX) continue;
                        int color = colors[x - minX, y - minY];
                        if (color < 0 || covered[x - minX, y - minY]) continue;
                        bool complete = true;
                        for (int blockY = y; blockY < y + size && complete; blockY++)
                            for (int blockX = x; blockX < x + size; blockX++)
                                if (colors[blockX - minX, blockY - minY] != color || covered[blockX - minX, blockY - minY])
                                {
                                    complete = false;
                                    break;
                                }
                        if (!complete) continue;
                        for (int blockY = y; blockY < y + size; blockY++)
                            for (int blockX = x; blockX < x + size; blockX++)
                                covered[blockX - minX, blockY - minY] = true;
                        TerrainPreviewCell cell = new TerrainPreviewCell();
                        cell.X = (x + (size - 1) * 0.5) * stepX;
                        cell.Z = (mask.Height - 1 - y - (size - 1) * 0.5) * stepZ;
                        cell.Width = size * stepX;
                        cell.Depth = size * stepZ;
                        cell.Color = color;
                        result.Add(cell);
                    }
                }
            }
            return result;
        }

        public static List<TerrainPreviewCell> BuildMaskTile(BmpSurface mask, BmpSurface original, double worldX, double worldZ,
            double worldWidth, double worldHeight, int tilePixels, int overlapPixels, int tilesInRow, bool changedOnly)
        {
            double stepX = worldWidth / (mask.Width - 1);
            double stepZ = worldHeight / (mask.Height - 1);
            int advance = tilePixels - overlapPixels;
            if (tilePixels < 1 || advance < 1 || tilesInRow < 1)
                return new List<TerrainPreviewCell>();
            int pixelX = Math.Max(0, Math.Min(mask.Width - 1, (int)Math.Round(worldX / stepX)));
            int pixelY = Math.Max(0, Math.Min(mask.Height - 1, mask.Height - 1 - (int)Math.Round(worldZ / stepZ)));
            int tileX;
            int tileY;
            GetMaskTileCoordinates(mask, worldX, worldZ, worldWidth, worldHeight, tilePixels, overlapPixels, tilesInRow, out tileX, out tileY);
            int minX = Math.Max(0, tileX * advance);
            int minY = Math.Max(0, tileY * advance);
            int maxX = Math.Min(mask.Width - 1, minX + tilePixels - 1);
            int maxY = Math.Min(mask.Height - 1, minY + tilePixels - 1);
            return BuildRegion(mask, original, minX, maxX, minY, maxY, stepX, stepZ, changedOnly);
        }

        public static List<TerrainPreviewLine> BuildTileBoundaries(BmpSurface mask, double worldX, double worldZ,
            double range, double worldWidth, double worldHeight, int tilePixels, int overlapPixels, int tilesInRow)
        {
            List<TerrainPreviewLine> result = new List<TerrainPreviewLine>();
            double stepX = worldWidth / (mask.Width - 1);
            double stepZ = worldHeight / (mask.Height - 1);
            int advance = tilePixels - overlapPixels;
            if (tilePixels < 1 || advance < 1 || tilesInRow < 1)
                return result;

            int centerX = (int)Math.Round(worldX / stepX);
            int centerY = mask.Height - 1 - (int)Math.Round(worldZ / stepZ);
            int reachX = (int)Math.Ceiling(range / stepX) + 1;
            int reachY = (int)Math.Ceiling(range / stepZ) + 1;
            int minX = Math.Max(0, centerX - reachX);
            int maxX = Math.Min(mask.Width - 1, centerX + reachX);
            int minY = Math.Max(0, centerY - reachY);
            int maxY = Math.Min(mask.Height - 1, centerY + reachY);
            int tileMinX = Math.Max(0, minX / advance - 1);
            int tileMaxX = Math.Min(tilesInRow - 1, maxX / advance + 1);
            int tileMinY = Math.Max(0, minY / advance - 1);
            int tileMaxY = Math.Min(tilesInRow - 1, maxY / advance + 1);

            HashSet<int> verticalTileLines = new HashSet<int>();
            HashSet<int> horizontalTileLines = new HashSet<int>();
            HashSet<int> verticalOverlapLines = new HashSet<int>();
            HashSet<int> horizontalOverlapLines = new HashSet<int>();
            for (int tileY = tileMinY; tileY <= tileMaxY; tileY++)
            {
                int startY = tileY * advance;
                int endY = Math.Min(mask.Height - 1, startY + tilePixels - 1);
                if (endY < minY || startY > maxY)
                    continue;
                horizontalTileLines.Add(startY);
                horizontalTileLines.Add(endY);
                if (overlapPixels > 0)
                    horizontalOverlapLines.Add(Math.Min(mask.Height - 1, startY + advance));
            }
            for (int tileX = tileMinX; tileX <= tileMaxX; tileX++)
            {
                int startX = tileX * advance;
                int endX = Math.Min(mask.Width - 1, startX + tilePixels - 1);
                if (endX < minX || startX > maxX)
                    continue;
                verticalTileLines.Add(startX);
                verticalTileLines.Add(endX);
                if (overlapPixels > 0)
                    verticalOverlapLines.Add(Math.Min(mask.Width - 1, startX + advance));
            }

            foreach (int x in verticalTileLines)
                result.Add(CreateVerticalLine(x, minY, maxY, stepX, stepZ, mask.Height, unchecked((int)0xFFFFC107)));
            foreach (int x in verticalOverlapLines)
                result.Add(CreateVerticalLine(x, minY, maxY, stepX, stepZ, mask.Height, unchecked((int)0xFF00E5FF)));
            foreach (int y in horizontalTileLines)
                result.Add(CreateHorizontalLine(y, minX, maxX, stepX, stepZ, mask.Height, unchecked((int)0xFFFFC107)));
            foreach (int y in horizontalOverlapLines)
                result.Add(CreateHorizontalLine(y, minX, maxX, stepX, stepZ, mask.Height, unchecked((int)0xFF00E5FF)));
            return result;
        }

        private static TerrainPreviewLine CreateVerticalLine(int pixelX, int minY, int maxY, double stepX, double stepZ, int maskHeight, int color)
        {
            return new TerrainPreviewLine
            {
                X1 = pixelX * stepX,
                Z1 = (maskHeight - 1 - maxY) * stepZ,
                X2 = pixelX * stepX,
                Z2 = (maskHeight - 1 - minY) * stepZ,
                Color = color
            };
        }

        private static TerrainPreviewLine CreateHorizontalLine(int pixelY, int minX, int maxX, double stepX, double stepZ, int maskHeight, int color)
        {
            return new TerrainPreviewLine
            {
                X1 = minX * stepX,
                Z1 = (maskHeight - 1 - pixelY) * stepZ,
                X2 = maxX * stepX,
                Z2 = (maskHeight - 1 - pixelY) * stepZ,
                Color = color
            };
        }

        private static List<TerrainPreviewCell> BuildRegion(BmpSurface mask, BmpSurface original, int minX, int maxX, int minY, int maxY,
            double stepX, double stepZ, bool changedOnly)
        {
            if (maxX < minX || maxY < minY) return new List<TerrainPreviewCell>();
            int[,] colors = new int[maxX - minX + 1, maxY - minY + 1];
            bool[,] covered = new bool[maxX - minX + 1, maxY - minY + 1];
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    int color = mask.ReadPacked(x, y);
                    colors[x - minX, y - minY] = !changedOnly || color != original.ReadPacked(x, y) ? color : -1;
                }
            List<TerrainPreviewCell> result = new List<TerrainPreviewCell>();
            for (int size = 64; size >= 1; size /= 2)
                for (int y = (minY / size) * size; y <= maxY - size + 1; y += size)
                {
                    if (y < minY) continue;
                    for (int x = (minX / size) * size; x <= maxX - size + 1; x += size)
                    {
                        if (x < minX) continue;
                        int color = colors[x - minX, y - minY];
                        if (color < 0 || covered[x - minX, y - minY]) continue;
                        bool complete = true;
                        for (int blockY = y; blockY < y + size && complete; blockY++)
                            for (int blockX = x; blockX < x + size; blockX++)
                                if (colors[blockX - minX, blockY - minY] != color || covered[blockX - minX, blockY - minY]) { complete = false; break; }
                        if (!complete) continue;
                        for (int blockY = y; blockY < y + size; blockY++)
                            for (int blockX = x; blockX < x + size; blockX++) covered[blockX - minX, blockY - minY] = true;
                        result.Add(new TerrainPreviewCell
                        {
                            X = (x + (size - 1) * 0.5) * stepX,
                            Z = (mask.Height - 1 - y - (size - 1) * 0.5) * stepZ,
                            Width = size * stepX,
                            Depth = size * stepZ,
                            Color = color
                        });
                    }
                }
            return result;
        }

    }

    internal sealed class MainForm : Form
    {

        private const int VK_MENU = 0x12;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;
        private const int VK_CONTROL = 0x11;
        private const int VK_Z = 0x5A;
        private const int VK_Y = 0x59;
        private const int VK_OEM_4 = 0xDB;
        private const int VK_OEM_6 = 0xDD;
        private const int VK_OEM_PLUS = 0xBB;
        private const int VK_OEM_MINUS = 0xBD;
        private const int VK_ADD = 0x6B;
        private const int VK_SUBTRACT = 0x6D;
        private const int VK_NUMPAD0 = 0x60;
        private const int VK_E = 0x45;
        private const int VK_F6 = 0x75;
        private const int VK_F7 = 0x76;
        private const int VK_F8 = 0x77;
        private const int VK_F9 = 0x78;
        private const int VK_PRIOR = 0x21;
        private const int VK_NEXT = 0x22;
        private const int VK_MBUTTON = 0x04;
        private const int VK_TAB = 0x09;

        private readonly AppSettings settings;
        private readonly string configPath;
        private readonly List<TerrainLayer> layers;
        private readonly ComboBox layerCombo;
        private readonly NumericUpDown radiusInput;
        private readonly NumericUpDown previewInput;
        private readonly NumericUpDown terrainRangeInput;
        private readonly ComboBox shapeCombo;
        private readonly CheckBox replaceOnlyCheck;
        private readonly ComboBox replaceSourceCombo;
        private readonly CheckBox paintingCheck;
        private readonly CheckBox rotatePreviewCheck;
        private readonly CheckBox showBuldozerBrushCheck;
        private readonly CheckBox showTerrainPreviewCheck;
        private readonly CheckBox eraserCheck;
        private readonly NumericUpDown strengthInput;
        private readonly Label positionLabel;
        private readonly Label sourceLabel;
        private readonly Label statusLabel;
        private readonly PreviewControl preview;
        private readonly CheckBox showTileBoundariesCheck;
        private readonly CheckBox checkMaskTileColorsCheck;
        private readonly Label maskTileColorStatusLabel;
        private readonly System.Windows.Forms.Timer timer;
        private readonly BridgePositionProvider bridge;
        private readonly GlobalMouseWheelHook wheelHook;
        private readonly Stack<StrokeHistory> undo = new Stack<StrokeHistory>();
        private readonly Stack<StrokeHistory> redo = new Stack<StrokeHistory>();

        private BmpSurface mask;
        private BmpSurface originalMask;
        private BmpSurface satellite;
        private string satelliteTemporaryPath;
        private WorldPosition currentPosition;
        private bool hasPosition;
        private bool strokeActive;
        private Dictionary<long, int> strokeOriginals;
        private WorldPosition lastPaintPosition;
        private DateTime lastPreview = DateTime.MinValue;
        private bool lastUndoKeys;
        private bool lastRedoKeys;
        private bool lastDecreaseKey;
        private bool lastIncreaseKey;
        private bool lastZoomResetKey;
        private bool lastEraserKey;
        private bool lastMiddleButton;
        private bool lastTabKey;
        private readonly bool[] lastActionKeys = new bool[5];
        private int lastBrushSizeValue;
        private string strokeName;
        private readonly bool[] lastNumberKeys = new bool[9];
        private bool terrainPreviewDirty = true;
        private DateTime lastTerrainPreview = DateTime.MinValue;
        private double lastTerrainPreviewX = Double.NaN;
        private double lastTerrainPreviewZ = Double.NaN;
        private int lastMaskTileX = -1;
        private int lastMaskTileY = -1;
        private bool maskTileColorCheckDirty = true;
        private bool maskTileColorCheckRunning;
        private int terrainPreviewGeneration = Environment.TickCount & Int32.MaxValue;

        public MainForm()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            PresetInfo selectedPreset = PresetManager.SelectOrCreate(null, baseDirectory);
            if (selectedPreset == null)
                throw new OperationCanceledException();
            this.configPath = selectedPreset.ConfigPath;
            settings = AppSettings.Load(selectedPreset.ConfigPath);
            Locale.English = true;
            if (String.IsNullOrWhiteSpace(settings.BridgePath) || String.IsNullOrWhiteSpace(settings.BrushStatePath) || String.IsNullOrWhiteSpace(settings.TerrainPreviewPath))
            {
                string defaultProfile = PresetManager.FindDefaultProfileDirectory();
                if (!String.IsNullOrEmpty(defaultProfile))
                {
                    settings.BridgePath = Path.Combine(defaultProfile, "MaskOver.cursor");
                    settings.BrushStatePath = Path.Combine(defaultProfile, "MaskOver.brush");
                    settings.TerrainPreviewPath = Path.Combine(defaultProfile, "MaskOver.terrain");
                    SaveSettings("BridgePath", settings.BridgePath, "BrushStatePath", settings.BrushStatePath);
                    SaveSettings("TerrainPreviewPath", settings.TerrainPreviewPath);
                }
            }
            if (!File.Exists(settings.MaskPath) || !File.Exists(settings.LayersPath))
                throw new FileNotFoundException(T("Maska lub layers.cfg z wybranego presetu nie istnieje.", "The mask or layers.cfg from the selected preset does not exist."));
            if (!String.IsNullOrWhiteSpace(settings.SatellitePath) && !File.Exists(settings.SatellitePath))
                throw new FileNotFoundException(T("Satelita z wybranego presetu nie istnieje.", "The satellite from the selected preset does not exist."));
            EnsureWorkingCopy();
            layers = LayersParser.Parse(settings.LayersPath);
            mask = new BmpSurface(settings.WorkingMaskPath, true);
            originalMask = new BmpSurface(settings.MaskPath, false);
            if (!String.IsNullOrWhiteSpace(settings.SatellitePath))
            {
                string satelliteLoadPath = PrepareRasterForBmp(settings.SatellitePath);
                satelliteTemporaryPath = satelliteLoadPath.Equals(settings.SatellitePath, StringComparison.OrdinalIgnoreCase) ? "" : satelliteLoadPath;
                satellite = new BmpSurface(satelliteLoadPath, false);
            }
            if ((satellite != null && (mask.Width != satellite.Width || mask.Height != satellite.Height)) || mask.Width != originalMask.Width || mask.Height != originalMask.Height)
                throw new InvalidDataException(T("Maska robocza, zrodlowa i satelita maja rozne wymiary.", "Working mask, source mask and satellite image have different dimensions."));

            bridge = new BridgePositionProvider(settings.BridgePath);
            Text = "MaskOver";
            Width = 620;
            Height = 960;
            MinimumSize = new Size(540, 720);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(Math.Max(0, Screen.PrimaryScreen.WorkingArea.Right - Width - 20), 40);
            TopMost = true;
            Opacity = 0.98;
            BackColor = Color.FromArgb(22, 24, 28);
            ForeColor = Color.FromArgb(235, 238, 245);
            Font = new Font("Segoe UI", 10.0f);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12, 10, 12, 8);
            root.ColumnCount = 1;
            root.RowCount = 7;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            sourceLabel = NewLabel(T("Zrodlo pozycji: oczekiwanie...", "Position source: waiting..."));
            sourceLabel.Font = new Font("Segoe UI", 10.0f, FontStyle.Bold);
            positionLabel = NewLabel("X --   Y --   Z --");
            positionLabel.Font = new Font("Consolas", 13.0f, FontStyle.Bold);
            positionLabel.ForeColor = Color.FromArgb(120, 210, 255);
            root.Controls.Add(sourceLabel, 0, 0);
            root.Controls.Add(positionLabel, 0, 1);

            preview = new PreviewControl();
            preview.Dock = DockStyle.Fill;
            preview.Margin = new Padding(0, 8, 0, 8);
            root.Controls.Add(preview, 0, 2);

            FlowLayoutPanel materialRow = NewRow();
            materialRow.Controls.Add(NewLabel("Material:"));
            layerCombo = new ComboBox();
            layerCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            layerCombo.Width = 380;
            layerCombo.Font = new Font("Segoe UI", 10.0f);
            layerCombo.BackColor = Color.FromArgb(38, 42, 50);
            layerCombo.ForeColor = Color.FromArgb(235, 238, 245);
            layerCombo.FlatStyle = FlatStyle.Flat;
            layerCombo.DrawMode = DrawMode.OwnerDrawFixed;
            layerCombo.ItemHeight = 22;
            layerCombo.DrawItem += DrawLayerItem;
            foreach (TerrainLayer layer in layers)
                layerCombo.Items.Add(layer);
            layerCombo.SelectedIndex = FindDefaultLayer();
            layerCombo.SelectedIndexChanged += delegate { UpdateBrushDisplay(); };
            materialRow.Controls.Add(layerCombo);
            Label eyedropperHint = NewLabel(T("Pipeta: środkowy przycisk myszy = materiał spod środka pędzla", "Eyedropper: middle mouse button = material under brush center"));
            eyedropperHint.ForeColor = Color.FromArgb(120, 210, 255);
            eyedropperHint.Font = new Font("Segoe UI", 9.0f, FontStyle.Italic);
            materialRow.Controls.Add(eyedropperHint);
            root.Controls.Add(materialRow, 0, 3);

            FlowLayoutPanel brushRow = NewRow();
            brushRow.Controls.Add(NewLabel(T("Rozmiar [px]:", "Size [px]:")));
            int initialBrushSize = 1;
            while (initialBrushSize < settings.BrushRadius && initialBrushSize < 256)
                initialBrushSize *= 2;
            lastBrushSizeValue = initialBrushSize;
            radiusInput = NewNumber(initialBrushSize, 1, 256, 70);
            radiusInput.ValueChanged += BrushSizeChanged;
            brushRow.Controls.Add(radiusInput);
            brushRow.Controls.Add(NewLabel(T("Kształt (Tab):", "Shape (Tab):")));
            shapeCombo = new ComboBox();
            shapeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            shapeCombo.Width = 100;
            shapeCombo.Font = new Font("Segoe UI", 10.0f);
            shapeCombo.BackColor = Color.FromArgb(38, 42, 50);
            shapeCombo.ForeColor = Color.FromArgb(235, 238, 245);
            shapeCombo.FlatStyle = FlatStyle.Flat;
            shapeCombo.Items.Add(T("Kolo", "Circle"));
            shapeCombo.Items.Add(T("Kwadrat", "Square"));
            shapeCombo.Items.Add(T("Organiczny", "Organic"));
            shapeCombo.SelectedIndex = 0;
            shapeCombo.SelectedIndexChanged += delegate { UpdateBrushDisplay(); };
            brushRow.Controls.Add(shapeCombo);
            brushRow.Controls.Add(NewLabel(T("Sila [%]:", "Strength [%]:")));
            strengthInput = NewNumber(settings.BrushStrength, 1, 100, 60);
            brushRow.Controls.Add(strengthInput);
            eraserCheck = new CheckBox();
            eraserCheck.Text = T("Gumka (E)", "Eraser (E)");
            eraserCheck.AutoSize = true;
            eraserCheck.ForeColor = Color.FromArgb(230, 234, 242);
            eraserCheck.CheckedChanged += delegate
            {
                if (strokeActive)
                    EndStroke();
                UpdateBrushDisplay();
                statusLabel.Text = eraserCheck.Checked ? T("Tryb gumki: przywracanie oryginalnej maski", "Eraser mode: restoring the source mask") : T("Tryb malowania", "Paint mode");
            };
            brushRow.Controls.Add(eraserCheck);
            replaceOnlyCheck = new CheckBox();
            replaceOnlyCheck.Text = T("Zamieniaj tylko:", "Replace only:");
            replaceOnlyCheck.AutoSize = true;
            replaceOnlyCheck.ForeColor = Color.FromArgb(230, 234, 242);
            brushRow.Controls.Add(replaceOnlyCheck);
            replaceSourceCombo = new ComboBox();
            replaceSourceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            replaceSourceCombo.Width = 150;
            replaceSourceCombo.Font = new Font("Segoe UI", 10.0f);
            replaceSourceCombo.BackColor = Color.FromArgb(38, 42, 50);
            replaceSourceCombo.ForeColor = Color.FromArgb(235, 238, 245);
            replaceSourceCombo.FlatStyle = FlatStyle.Flat;
            replaceSourceCombo.DrawMode = DrawMode.OwnerDrawFixed;
            replaceSourceCombo.ItemHeight = 22;
            replaceSourceCombo.DrawItem += DrawLayerItem;
            foreach (TerrainLayer sourceLayer in layers)
                replaceSourceCombo.Items.Add(sourceLayer);
            replaceSourceCombo.SelectedIndex = FindLayer("grass");
            replaceSourceCombo.Enabled = false;
            replaceOnlyCheck.CheckedChanged += delegate { replaceSourceCombo.Enabled = replaceOnlyCheck.Checked; };
            brushRow.Controls.Add(replaceSourceCombo);
            brushRow.Controls.Add(NewLabel(T("Podglad [m]:", "Preview [m]:")));
            previewInput = NewNumber(settings.PreviewMeters, 10, 4000, 85);
            previewInput.Increment = 50;
            previewInput.ValueChanged += delegate { lastPreview = DateTime.MinValue; UpdateBrushDisplay(); };
            brushRow.Controls.Add(previewInput);
            paintingCheck = new CheckBox();
            paintingCheck.Text = T("Malowanie przytrzymanym ALT", "Paint while holding ALT");
            paintingCheck.Checked = true;
            paintingCheck.AutoSize = true;
            paintingCheck.ForeColor = Color.FromArgb(230, 234, 242);
            brushRow.Controls.Add(paintingCheck);
            rotatePreviewCheck = new CheckBox();
            rotatePreviewCheck.Text = T("Obracaj z kamera", "Rotate with camera");
            rotatePreviewCheck.Checked = true;
            rotatePreviewCheck.AutoSize = true;
            rotatePreviewCheck.ForeColor = Color.FromArgb(230, 234, 242);
            rotatePreviewCheck.CheckedChanged += delegate { lastPreview = DateTime.MinValue; };
            brushRow.Controls.Add(rotatePreviewCheck);
            showBuldozerBrushCheck = new CheckBox();
            showBuldozerBrushCheck.Text = T("Pokazuj zasieg w Buldozerze", "Show brush outline in Buldozer");
            showBuldozerBrushCheck.Checked = true;
            showBuldozerBrushCheck.AutoSize = true;
            showBuldozerBrushCheck.ForeColor = Color.FromArgb(230, 234, 242);
            showBuldozerBrushCheck.CheckedChanged += delegate { WriteBrushState(); };
            brushRow.Controls.Add(showBuldozerBrushCheck);
            showTerrainPreviewCheck = new CheckBox();
            showTerrainPreviewCheck.Text = T("Pokazuj namalowane (F9)", "Show painted mask (F9)");
            showTerrainPreviewCheck.Checked = false;
            showTerrainPreviewCheck.AutoSize = true;
            showTerrainPreviewCheck.ForeColor = Color.FromArgb(230, 234, 242);
            showTerrainPreviewCheck.CheckedChanged += delegate { terrainPreviewDirty = true; WriteTerrainPreview(true); };
            brushRow.Controls.Add(showTerrainPreviewCheck);
            showTileBoundariesCheck = new CheckBox();
            showTileBoundariesCheck.Text = T("Granice tile/overlap (mapa)", "Tile/overlap boundaries (map)");
            showTileBoundariesCheck.AutoSize = true;
            showTileBoundariesCheck.ForeColor = Color.FromArgb(230, 234, 242);
            showTileBoundariesCheck.CheckedChanged += delegate
            {
                if (showTileBoundariesCheck.Checked && !EnsureMaskTileSettings())
                {
                    showTileBoundariesCheck.Checked = false;
                    return;
                }
                // Granice tile rysujemy tylko na mapie aplikacji (nie w buldozerze).
                // Wymuś odświeżenie podglądu w buldozerze bez linii granic.
                terrainPreviewDirty = true;
                WriteTerrainPreview(true);
                lastPreview = DateTime.MinValue;
                if (hasPosition)
                    RefreshPreview();
                else
                    preview.Invalidate();
            };
            brushRow.Controls.Add(showTileBoundariesCheck);
            checkMaskTileColorsCheck = new CheckBox();
            checkMaskTileColorsCheck.Text = T("Sprawdzaj kolory tile'a", "Check tile colors");
            checkMaskTileColorsCheck.AutoSize = true;
            checkMaskTileColorsCheck.ForeColor = Color.FromArgb(230, 234, 242);
            checkMaskTileColorsCheck.CheckedChanged += delegate
            {
                if (checkMaskTileColorsCheck.Checked && !EnsureMaskTileSettings())
                {
                    checkMaskTileColorsCheck.Checked = false;
                    return;
                }
                maskTileColorCheckDirty = true;
                if (!checkMaskTileColorsCheck.Checked)
                {
                    maskTileColorStatusLabel.Text = T("Kolory tile'a: wyłączone", "Tile colors: disabled");
                    maskTileColorStatusLabel.ForeColor = Color.FromArgb(230, 234, 242);
                }
                UpdateMaskTileColorCheck();
            };
            brushRow.Controls.Add(checkMaskTileColorsCheck);
            maskTileColorStatusLabel = NewLabel(T("Kolory tile'a: wyłączone", "Tile colors: disabled"));
            brushRow.Controls.Add(maskTileColorStatusLabel);
            brushRow.Controls.Add(NewLabel(T("Zasieg podgladu [m]:", "Preview range [m]:")));
            terrainRangeInput = NewNumber(settings.TerrainPreviewMeters, 10, 150, 75);
            terrainRangeInput.Increment = 10;
            terrainRangeInput.ValueChanged += delegate { terrainPreviewDirty = true; WriteTerrainPreview(true); SaveSettings("TerrainPreviewMeters", terrainRangeInput.Value.ToString()); };
            brushRow.Controls.Add(terrainRangeInput);
            root.Controls.Add(brushRow, 0, 4);

            FlowLayoutPanel actionRow = NewRow();
            actionRow.Controls.Add(NewButton(T("Cofnij", "Undo"), delegate { Undo(); }));
            actionRow.Controls.Add(NewButton(T("Ponow", "Redo"), delegate { Redo(); }));
            actionRow.Controls.Add(NewButton(T("Zapisz maskę (F6)", "Save working mask (F6)"), delegate { Export(); }));
            actionRow.Controls.Add(NewButton(T("Eksport zmian (F7)", "Export changes (F7)"), delegate { ExportChanges(); }));
            actionRow.Controls.Add(NewButton(T("Wyczysc maske robocza", "Clear working mask"), delegate { ClearWorkingMask(); }));
            actionRow.Controls.Add(NewButton(T("Zmien preset", "Change preset"), delegate { ChangePreset(); }));
            root.Controls.Add(actionRow, 0, 5);

            statusLabel = NewLabel(T("Kopia robocza: ", "Working copy: ") + settings.WorkingMaskPath);
            statusLabel.AutoEllipsis = true;
            statusLabel.Height = 36;
            statusLabel.ForeColor = Color.FromArgb(170, 178, 190);
            root.Controls.Add(statusLabel, 0, 6);

            UpdateBrushDisplay();
            preview.MiddleClicked += delegate { PickMaterialFromBrushCenter(); };
            MouseDown += FormMouseDown;
            MouseWheel += FormMouseWheel;
            FormClosing += ClosingForm;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 33;
            timer.Tick += Tick;
            timer.Start();

            wheelHook = new GlobalMouseWheelHook();
            wheelHook.Wheel += GlobalMouseWheel;
            WriteTerrainPreview(true);
        }

        private void EnsureWorkingCopy()
        {
            if (!File.Exists(settings.MaskPath))
                throw new FileNotFoundException(T("Nie znaleziono maski zrodlowej.", "Source mask not found."), settings.MaskPath);
            if (File.Exists(settings.WorkingMaskPath))
                return;
            string directory = Path.GetDirectoryName(settings.WorkingMaskPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            File.Copy(settings.MaskPath, settings.WorkingMaskPath, false);
        }

        private static string T(string polish, string english)
        {
            return Locale.T(polish, english);
        }

        private static Label NewLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Margin = new Padding(4, 8, 6, 4);
            label.ForeColor = Color.FromArgb(230, 234, 242);
            label.Font = new Font("Segoe UI", 10.0f);
            return label;
        }

        private static FlowLayoutPanel NewRow()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.AutoSize = true;
            panel.WrapContents = true;
            panel.Padding = new Padding(0, 2, 0, 2);
            return panel;
        }

        private static NumericUpDown NewNumber(int value, int minimum, int maximum, int width)
        {
            NumericUpDown input = new NumericUpDown();
            input.Minimum = minimum;
            input.Maximum = maximum;
            input.Value = Math.Max(minimum, Math.Min(maximum, value));
            input.Width = width;
            input.Height = 26;
            input.Font = new Font("Segoe UI", 10.0f);
            input.BackColor = Color.FromArgb(38, 42, 50);
            input.ForeColor = Color.FromArgb(235, 238, 245);
            input.BorderStyle = BorderStyle.FixedSingle;
            input.Margin = new Padding(2, 4, 8, 4);
            return input;
        }

        private static Button NewButton(string text, EventHandler handler)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.MinimumSize = new Size(0, 30);
            button.Padding = new Padding(10, 4, 10, 4);
            button.Margin = new Padding(3, 4, 3, 4);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 120, 190);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(35, 90, 150);
            button.BackColor = Color.FromArgb(45, 105, 170);
            button.ForeColor = Color.White;
            button.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            button.Click += handler;
            return button;
        }

        private int FindDefaultLayer()
        {
            for (int index = 0; index < layers.Count; index++)
            {
                if (layers[index].Name.Equals("concrete1", StringComparison.OrdinalIgnoreCase))
                    return index;
            }
            return 0;
        }

        private int FindLayer(string name)
        {
            for (int index = 0; index < layers.Count; index++)
            {
                if (layers[index].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return index;
            }
            return 0;
        }

        private void DrawLayerItem(object sender, DrawItemEventArgs e)
        {
            Color bg = (e.State & DrawItemState.Selected) != 0
                ? Color.FromArgb(45, 105, 170)
                : Color.FromArgb(38, 42, 50);
            using (SolidBrush bgBrush = new SolidBrush(bg))
                e.Graphics.FillRectangle(bgBrush, e.Bounds);
            ComboBox combo = sender as ComboBox;
            if (combo == null || e.Index < 0 || e.Index >= combo.Items.Count)
                return;
            TerrainLayer layer = (TerrainLayer)combo.Items[e.Index];
            Rectangle swatch = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 3, 22, e.Bounds.Height - 6);
            using (Brush brush = new SolidBrush(layer.Color))
                e.Graphics.FillRectangle(brush, swatch);
            using (Pen border = new Pen(Color.FromArgb(200, 205, 215)))
                e.Graphics.DrawRectangle(border, swatch);
            using (Brush textBrush = new SolidBrush(Color.FromArgb(235, 238, 245)))
                e.Graphics.DrawString(layer.ToString(), e.Font, textBrush, e.Bounds.Left + 32, e.Bounds.Top + 3);
        }

        private TerrainLayer SelectedLayer
        {
            get { return layerCombo.SelectedItem as TerrainLayer; }
        }

        private void UpdateBrushDisplay()
        {
            if (preview == null || radiusInput == null || previewInput == null)
                return;
            double pixelStep = settings.WorldWidth / (mask.Width - 1);
            preview.BrushRadiusMeters = (float)(((double)radiusInput.Value * 0.5) * pixelStep);
            preview.PreviewMeters = (int)previewInput.Value;
            preview.BrushShape = shapeCombo == null ? 0 : shapeCombo.SelectedIndex;
            TerrainLayer layer = SelectedLayer;
            if (eraserCheck != null && eraserCheck.Checked)
                preview.BrushColor = Color.White;
            else if (layer != null)
                preview.BrushColor = layer.Color;
            preview.Invalidate();
            WriteBrushState();
        }

        private void WriteBrushState()
        {
            if (radiusInput == null || shapeCombo == null || showBuldozerBrushCheck == null || mask == null)
                return;
            try
            {
                string destination = settings.BrushStatePath;
                string directory = Path.GetDirectoryName(destination);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                string temporary = destination + ".tmp";
                double pixelStepX = settings.WorldWidth / (mask.Width - 1);
                double pixelStepZ = settings.WorldHeight / (mask.Height - 1);
                string content = ((int)radiusInput.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n"
                    + shapeCombo.SelectedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n"
                    + (showBuldozerBrushCheck.Checked ? "1" : "0") + "\r\n"
                    + (eraserCheck != null && eraserCheck.Checked ? "1" : "0") + "\r\n"
                    + pixelStepX.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "\r\n"
                    + pixelStepZ.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "\r\n"
                    + "0\r\n";
                File.WriteAllText(temporary, content, Encoding.ASCII);
                if (File.Exists(destination))
                    File.Replace(temporary, destination, null, true);
                else
                    File.Move(temporary, destination);
            }
            catch (IOException)
            {
                // Buldozer moze akurat czytac plik. Kolejna zmiana ustawien ponowi zapis.
            }
            catch (UnauthorizedAccessException)
            {
                // Podglad w aplikacji i malowanie pozostaja dostepne bez mostu obrysu.
            }
        }

        private void UpdateTerrainPreview()
        {
            // Granice tile są rysowane wyłącznie na mapie aplikacji – tutaj tylko podgląd terenu do buldozera.
            if (showTerrainPreviewCheck == null || !showTerrainPreviewCheck.Checked || !hasPosition)
                return;
            if ((DateTime.UtcNow - lastTerrainPreview).TotalMilliseconds < 250.0)
                return;
            double dx = currentPosition.X - lastTerrainPreviewX;
            double dz = currentPosition.Z - lastTerrainPreviewZ;
            if (!terrainPreviewDirty && !Double.IsNaN(lastTerrainPreviewX) && (dx * dx) + (dz * dz) < 2.25)
                return;
            WriteTerrainPreview(false);
        }

        private void WriteTerrainPreview(bool force)
        {
            if (showTerrainPreviewCheck == null || (!hasPosition && !force))
                return;
            try
            {
                StringBuilder content = new StringBuilder();
                content.AppendLine((++terrainPreviewGeneration).ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (!showTerrainPreviewCheck.Checked)
                {
                    content.AppendLine("0");
                }
                else if (hasPosition)
                {
                    double range = (double)terrainRangeInput.Value;
                    List<TerrainPreviewCell> cells = TerrainPreviewBuilder.Build(mask, originalMask, currentPosition.X, currentPosition.Z,
                        range, settings.WorldWidth, settings.WorldHeight);
                    AppendTerrainPreview(content, cells, 1);
                }
                else
                {
                    content.AppendLine("0");
                }
                if (hasPosition)
                {
                    lastTerrainPreviewX = currentPosition.X;
                    lastTerrainPreviewZ = currentPosition.Z;
                }
                // Linie granic tile nie są już wysyłane do buldozera – rysowane tylko na mapie aplikacji.
                string destination = settings.TerrainPreviewPath;
                string directory = Path.GetDirectoryName(destination);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                string temporary = destination + ".tmp";
                File.WriteAllText(temporary, content.ToString(), Encoding.ASCII);
                if (File.Exists(destination))
                    File.Replace(temporary, destination, null, true);
                else
                    File.Move(temporary, destination);
                lastTerrainPreview = DateTime.UtcNow;
                terrainPreviewDirty = false;
            }
            catch (IOException)
            {
                terrainPreviewDirty = true;
            }
            catch (UnauthorizedAccessException)
            {
                terrainPreviewDirty = true;
            }
        }

        private static void AppendTerrainPreview(StringBuilder content, List<TerrainPreviewCell> cells, int mode)
        {
            Dictionary<int, List<TerrainPreviewCell>> groups = new Dictionary<int, List<TerrainPreviewCell>>();
            foreach (TerrainPreviewCell cell in cells)
            {
                List<TerrainPreviewCell> group;
                if (!groups.TryGetValue(cell.Color, out group))
                {
                    group = new List<TerrainPreviewCell>();
                    groups.Add(cell.Color, group);
                }
                group.Add(cell);
            }
            content.AppendLine(mode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (KeyValuePair<int, List<TerrainPreviewCell>> pair in groups)
            {
                content.Append("G ").Append(pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture));
                content.Append(' ').Append(pair.Value.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
                foreach (TerrainPreviewCell cell in pair.Value)
                {
                    content.Append(cell.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                    content.Append(cell.Z.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                    content.Append(cell.Width.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                    content.AppendLine(cell.Depth.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        private static void AppendTerrainPreviewLines(StringBuilder content, List<TerrainPreviewLine> lines)
        {
            foreach (TerrainPreviewLine line in lines)
            {
                content.Append("L ").Append(line.Color.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                content.Append(line.X1.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                content.Append(line.Z1.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                content.Append(line.X2.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                content.AppendLine(line.Z2.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private void Tick(object sender, EventArgs e)
        {
            WorldPosition position;
            bool bridgeAvailable = bridge.TryRead(out position);
            if (bridgeAvailable)
                AcceptPosition(position);
            HandleKeyboard();
            HandlePainting();
            UpdateMaskTileColorCheck();
            UpdateTerrainPreview();
            if (hasPosition && (DateTime.UtcNow - lastPreview).TotalMilliseconds >= 90)
                RefreshPreview();
        }

        private void AcceptPosition(WorldPosition position)
        {
            if (position.X < 0.0 || position.Z < 0.0 || position.X > settings.WorldWidth || position.Z > settings.WorldHeight)
                return;
            currentPosition = position;
            hasPosition = true;
            if (position.HasDirection)
                positionLabel.Text = String.Format(System.Globalization.CultureInfo.InvariantCulture, "X {0:0.0}   Y {1:0.0}   Z {2:0.0}   {3:0}°", position.X, position.Y, position.Z, position.HeadingDegrees);
            else
                positionLabel.Text = String.Format(System.Globalization.CultureInfo.InvariantCulture, "X {0:0.0}   Y {1:0.0}   Z {2:0.0}", position.X, position.Y, position.Z);
            UpdatePaintIndicator(false, NativeMethods.IsBuldozerForeground(), false);
        }

        private void RefreshPreview()
        {
            lastPreview = DateTime.UtcNow;
            try
            {
                preview.FullMaskView = false;
                double directionX = rotatePreviewCheck.Checked && currentPosition.HasDirection ? currentPosition.DirectionX : 0.0;
                double directionZ = rotatePreviewCheck.Checked && currentPosition.HasDirection ? currentPosition.DirectionZ : 1.0;
                preview.BrushRotationDegrees = rotatePreviewCheck.Checked && currentPosition.HasDirection ? -(float)currentPosition.HeadingDegrees : 0.0f;
                double pixelStepX = settings.WorldWidth / (mask.Width - 1);
                double pixelStepZ = settings.WorldHeight / (mask.Height - 1);
                int centerPixelX = (int)Math.Round(currentPosition.X / pixelStepX);
                int centerPixelY = mask.Height - 1 - (int)Math.Round(currentPosition.Z / pixelStepZ);
                double brushX = centerPixelX * pixelStepX;
                double brushZ = (mask.Height - 1 - centerPixelY) * pixelStepZ;
                if (((int)radiusInput.Value & 1) == 0)
                {
                    brushX -= pixelStepX * 0.5;
                    brushZ += pixelStepZ * 0.5;
                }
                double deltaX = brushX - currentPosition.X;
                double deltaZ = brushZ - currentPosition.Z;
                preview.BrushOffsetRightMeters = (float)((deltaX * directionZ) - (deltaZ * directionX));
                preview.BrushOffsetForwardMeters = (float)((deltaX * directionX) + (deltaZ * directionZ));
                BmpSurface background = satellite ?? mask;
                int spanMeters = (int)previewInput.Value;
                const int outputSize = 420;
                Bitmap image = background.RenderPreview(mask, currentPosition.X, currentPosition.Z, settings.WorldWidth, settings.WorldHeight, spanMeters, outputSize, satellite == null ? 1.0 : settings.MaskOpacity, directionX, directionZ);

                // Rysuj granice tile/overlap na mapie aplikacji (nie w buldozerze).
                if (showTileBoundariesCheck != null && showTileBoundariesCheck.Checked &&
                    settings.MaskTilePixels > 0 && settings.MaskTilesInRow > 0)
                {
                    List<TerrainPreviewLine> lines = TerrainPreviewBuilder.BuildTileBoundaries(
                        mask, currentPosition.X, currentPosition.Z, spanMeters,
                        settings.WorldWidth, settings.WorldHeight,
                        settings.MaskTilePixels, settings.MaskTileOverlapPixels, settings.MaskTilesInRow);
                    DrawTileBoundariesOnPreview(image, lines, currentPosition.X, currentPosition.Z, spanMeters, outputSize, directionX, directionZ);
                }

                preview.SetImage(image);
            }
            catch (Exception ex)
            {
                statusLabel.Text = T("Podglad: ", "Preview: ") + ex.Message;
            }
        }

        private static void DrawTileBoundariesOnPreview(Bitmap image, List<TerrainPreviewLine> lines,
            double worldX, double worldZ, int spanMeters, int outputSize, double directionX, double directionZ)
        {
            if (lines == null || lines.Count == 0 || spanMeters < 1 || outputSize < 2)
                return;
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
            using (Graphics g = Graphics.FromImage(image))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (TerrainPreviewLine line in lines)
                {
                    float x1, y1, x2, y2;
                    if (!ProjectWorldToPreview(line.X1, line.Z1, worldX, worldZ, spanMeters, outputSize, directionX, directionZ, out x1, out y1))
                        continue;
                    if (!ProjectWorldToPreview(line.X2, line.Z2, worldX, worldZ, spanMeters, outputSize, directionX, directionZ, out x2, out y2))
                        continue;
                    // Color z ARGB (np. 0xFFFFC107) – bierzemy RGB.
                    int argb = line.Color;
                    Color color = Color.FromArgb(255, (argb >> 16) & 255, (argb >> 8) & 255, argb & 255);
                    using (Pen pen = new Pen(color, 1.5f))
                    {
                        g.DrawLine(pen, x1, y1, x2, y2);
                    }
                }
            }
        }

        private static bool ProjectWorldToPreview(double x, double z, double worldX, double worldZ,
            int spanMeters, int outputSize, double directionX, double directionZ, out float px, out float py)
        {
            double deltaX = x - worldX;
            double deltaZ = z - worldZ;
            // right = delta · rightVector, forward = delta · forwardVector
            double right = (deltaX * directionZ) - (deltaZ * directionX);
            double forward = (deltaX * directionX) + (deltaZ * directionZ);
            px = (float)((right / spanMeters) * outputSize + (outputSize * 0.5));
            py = (float)((outputSize * 0.5) - (forward / spanMeters) * outputSize);
            // Zwracamy true nawet jeśli poza kadrą – Graphics i tak przytnie.
            return true;
        }

        private static bool IsKeyPressed(int key)
        {
            return (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
        }

        private void HandlePainting()
        {
            bool foreground = NativeMethods.IsBuldozerForeground();
            bool altPressed = IsKeyPressed(VK_MENU) || IsKeyPressed(VK_LMENU) || IsKeyPressed(VK_RMENU);
            bool requested = paintingCheck.Checked && hasPosition && foreground && altPressed;
            if (requested && !strokeActive)
                BeginStroke();
            if (requested && strokeActive)
                ContinueStroke();
            if (!requested && strokeActive)
                EndStroke();
            UpdatePaintIndicator(requested, foreground, altPressed);
        }

        private void UpdatePaintIndicator(bool painting, bool foreground, bool altPressed)
        {
            string source = hasPosition ? currentPosition.Source : T("brak", "none");
            if (painting)
            {
                sourceLabel.Text = T("Zrodlo: ", "Source: ") + source + (eraserCheck.Checked ? T("   |   GUMKA ALT", "   |   ERASING ALT") : T("   |   MALOWANIE ALT", "   |   PAINTING ALT"));
                sourceLabel.BackColor = eraserCheck.Checked ? Color.FromArgb(85, 85, 85) : Color.FromArgb(120, 45, 25);
                sourceLabel.ForeColor = Color.White;
            }
            else
            {
                string state = foreground ? (altPressed ? T("ALT wykryty", "ALT pressed") : T("ALT gotowy", "ALT ready")) : T("uaktywnij Buldozer", "activate Buldozer");
                if (eraserCheck.Checked)
                    state = T("GUMKA | ", "ERASER | ") + state;
                sourceLabel.Text = T("Zrodlo: ", "Source: ") + source + "   |   " + state;
                sourceLabel.BackColor = Color.Transparent;
                sourceLabel.ForeColor = Color.WhiteSmoke;
            }
        }

        private void BeginStroke()
        {
            TerrainLayer layer = SelectedLayer;
            if (layer == null)
                return;
            strokeActive = true;
            strokeOriginals = new Dictionary<long, int>();
            strokeName = eraserCheck.Checked ? T("Gumka", "Eraser") : layer.Name;
            lastPaintPosition = currentPosition;
            PaintAt(currentPosition);
        }

        private void ContinueStroke()
        {
            double dx = currentPosition.X - lastPaintPosition.X;
            double dz = currentPosition.Z - lastPaintPosition.Z;
            double distance = Math.Sqrt((dx * dx) + (dz * dz));
            double pixelStep = Math.Min(settings.WorldWidth / (mask.Width - 1), settings.WorldHeight / (mask.Height - 1));
            double spacing = Math.Max(pixelStep * 0.5, (double)radiusInput.Value * pixelStep * 0.35);
            int steps = Math.Max(1, (int)Math.Ceiling(distance / spacing));
            for (int step = 1; step <= steps; step++)
            {
                double amount = (double)step / steps;
                WorldPosition interpolated = new WorldPosition(lastPaintPosition.X + (dx * amount), currentPosition.Y, lastPaintPosition.Z + (dz * amount), currentPosition.Source);
                PaintAt(interpolated);
            }
            lastPaintPosition = currentPosition;
        }

        private void PaintAt(WorldPosition position)
        {
            TerrainLayer layer = SelectedLayer;
            if (layer == null)
                return;
            double strength = (double)strengthInput.Value / 100.0;
            if (eraserCheck.Checked)
            {
                if (shapeCombo.SelectedIndex == 1)
                    mask.RestoreSquareFrom(originalMask, position.X, position.Z, (double)radiusInput.Value, settings.WorldWidth, settings.WorldHeight, strokeOriginals, strength);
                else if (shapeCombo.SelectedIndex == 2)
                    mask.RestoreOrganicFrom(originalMask, position.X, position.Z, (double)radiusInput.Value, settings.WorldWidth, settings.WorldHeight, strokeOriginals, strength);
                else
                    mask.RestoreCircleFrom(originalMask, position.X, position.Z, (double)radiusInput.Value, settings.WorldWidth, settings.WorldHeight, strokeOriginals, strength);
                lastPreview = DateTime.MinValue;
                terrainPreviewDirty = true;
                return;
            }
            int packed = (layer.Color.R << 16) | (layer.Color.G << 8) | layer.Color.B;
            TerrainLayer sourceLayer = replaceSourceCombo.SelectedItem as TerrainLayer;
            bool replaceOnly = replaceOnlyCheck.Checked && sourceLayer != null;
            int requiredColor = sourceLayer == null ? 0 : (sourceLayer.Color.R << 16) | (sourceLayer.Color.G << 8) | sourceLayer.Color.B;
            if (shapeCombo.SelectedIndex == 1)
                mask.PaintSquare(position.X, position.Z, (double)radiusInput.Value, settings.WorldWidth, settings.WorldHeight, packed, strokeOriginals, replaceOnly, requiredColor, strength);
            else if (shapeCombo.SelectedIndex == 2)
                mask.PaintOrganic(position.X, position.Z, (double)radiusInput.Value, settings.WorldWidth, settings.WorldHeight, packed, strokeOriginals, replaceOnly, requiredColor, strength);
            else
                mask.PaintCircle(position.X, position.Z, (double)radiusInput.Value, settings.WorldWidth, settings.WorldHeight, packed, strokeOriginals, replaceOnly, requiredColor, strength);
            lastPreview = DateTime.MinValue;
            terrainPreviewDirty = true;
        }

        private void EndStroke()
        {
            strokeActive = false;
            StrokeHistory history = new StrokeHistory(String.IsNullOrEmpty(strokeName) ? "?" : strokeName);
            foreach (KeyValuePair<long, int> pair in strokeOriginals)
            {
                int after = mask.ReadPackedAtOffset(pair.Key);
                if (after != pair.Value)
                {
                    PixelDelta delta = new PixelDelta();
                    delta.Offset = pair.Key;
                    delta.Before = pair.Value;
                    delta.After = after;
                    history.Pixels.Add(delta);
                }
            }
            strokeOriginals = null;
            if (history.Pixels.Count > 0)
            {
                undo.Push(history);
                redo.Clear();
                while (undo.Count > 30)
                    TrimOldest(undo);
                statusLabel.Text = T("Pociagniecie: ", "Stroke: ") + history.LayerName + T(", piksele: ", ", pixels: ") + history.Pixels.Count;
                mask.Flush();
                maskTileColorCheckDirty = true;
            }
        }

        private static void TrimOldest(Stack<StrokeHistory> stack)
        {
            StrokeHistory[] entries = stack.ToArray();
            stack.Clear();
            for (int index = entries.Length - 2; index >= 0; index--)
                stack.Push(entries[index]);
        }

        private void Undo()
        {
            if (strokeActive || undo.Count == 0)
                return;
            StrokeHistory history = undo.Pop();
            foreach (PixelDelta delta in history.Pixels)
                mask.WritePackedAtOffset(delta.Offset, delta.Before);
            redo.Push(history);
            lastPreview = DateTime.MinValue;
            terrainPreviewDirty = true;
            statusLabel.Text = T("Cofnieto: ", "Undone: ") + history.LayerName;
        }

        private void Redo()
        {
            if (strokeActive || redo.Count == 0)
                return;
            StrokeHistory history = redo.Pop();
            foreach (PixelDelta delta in history.Pixels)
                mask.WritePackedAtOffset(delta.Offset, delta.After);
            undo.Push(history);
            lastPreview = DateTime.MinValue;
            terrainPreviewDirty = true;
            statusLabel.Text = T("Ponowiono: ", "Redone: ") + history.LayerName;
        }

        private void HandleKeyboard()
        {
            bool shortcutContext = NativeMethods.IsBuldozerForeground() || NativeMethods.GetForegroundWindow() == Handle;
            if (!shortcutContext)
            {
                lastUndoKeys = false;
                lastRedoKeys = false;
                lastDecreaseKey = false;
                lastIncreaseKey = false;
                lastZoomResetKey = false;
                lastEraserKey = false;
                lastMiddleButton = false;
                lastTabKey = false;
                for (int actionIndex = 0; actionIndex < lastActionKeys.Length; actionIndex++) lastActionKeys[actionIndex] = false;
                for (int resetIndex = 0; resetIndex < 9; resetIndex++)
                    lastNumberKeys[resetIndex] = false;
                return;
            }
            bool control = IsKeyPressed(VK_CONTROL);
            bool undoKeys = control && IsKeyPressed(VK_Z);
            bool redoKeys = control && IsKeyPressed(VK_Y);
            if (undoKeys && !lastUndoKeys) Undo();
            if (redoKeys && !lastRedoKeys) Redo();
            lastUndoKeys = undoKeys;
            lastRedoKeys = redoKeys;

            bool decrease = IsKeyPressed(VK_OEM_4);
            bool increase = IsKeyPressed(VK_OEM_6);
            if (decrease && !lastDecreaseKey) ChangeRadius(-1);
            if (increase && !lastIncreaseKey) ChangeRadius(1);
            lastDecreaseKey = decrease;
            lastIncreaseKey = increase;

            bool zoomIn = IsKeyPressed(VK_OEM_PLUS) || IsKeyPressed(VK_ADD);
            bool zoomOut = IsKeyPressed(VK_OEM_MINUS) || IsKeyPressed(VK_SUBTRACT);
            bool zoomReset = IsKeyPressed(VK_NUMPAD0);
            if (zoomIn != zoomOut) ChangeZoom(zoomIn);
            if (zoomReset && !lastZoomResetKey) ResetZoom();
            lastZoomResetKey = zoomReset;

            bool eraserKey = IsKeyPressed(VK_E);
            if (eraserKey && !lastEraserKey && !strokeActive)
                eraserCheck.Checked = !eraserCheck.Checked;
            lastEraserKey = eraserKey;

            // Middle mouse = eyedropper from brush center (MaskOver or Buldozer focused).
            bool middleButton = IsKeyPressed(VK_MBUTTON);
            if (middleButton && !lastMiddleButton && !strokeActive)
                PickMaterialFromBrushCenter();
            lastMiddleButton = middleButton;

            // Tab cycles brush shape: Circle → Square → Organic → Circle ...
            bool tabKey = IsKeyPressed(VK_TAB);
            if (tabKey && !lastTabKey && !strokeActive && shapeCombo != null && shapeCombo.Items.Count > 0)
            {
                shapeCombo.SelectedIndex = (shapeCombo.SelectedIndex + 1) % shapeCombo.Items.Count;
                statusLabel.Text = T("Kształt pędzla: ", "Brush shape: ") + shapeCombo.SelectedItem;
            }
            lastTabKey = tabKey;

            int[] actionCodes = { VK_F6, VK_F7, VK_F9, VK_PRIOR, VK_NEXT };
            for (int actionIndex = 0; actionIndex < actionCodes.Length; actionIndex++)
            {
                bool pressed = IsKeyPressed(actionCodes[actionIndex]);
                if (pressed && !lastActionKeys[actionIndex] && !strokeActive)
                {
                    switch (actionIndex)
                    {
                        case 0: Export(); break;
                        case 1: ExportChanges(); break;
                        case 2: showTerrainPreviewCheck.Checked = !showTerrainPreviewCheck.Checked; break;
                        case 3: terrainRangeInput.Value = Math.Min(terrainRangeInput.Maximum, terrainRangeInput.Value + 10); break;
                        case 4: terrainRangeInput.Value = Math.Max(terrainRangeInput.Minimum, terrainRangeInput.Value - 10); break;
                    }
                }
                lastActionKeys[actionIndex] = pressed;
            }

            for (int index = 0; index < 9; index++)
            {
                bool pressed = IsKeyPressed(0x31 + index);
                if (pressed && !lastNumberKeys[index] && index < layerCombo.Items.Count)
                    layerCombo.SelectedIndex = index;
                lastNumberKeys[index] = pressed;
            }
        }

        private void ChangeRadius(int change)
        {
            int size = (int)radiusInput.Value;
            int steps = Math.Abs(change);
            for (int step = 0; step < steps; step++)
            {
                if (change > 0 && size < 256)
                    size *= 2;
                else if (change < 0 && size > 1)
                    size /= 2;
            }
            radiusInput.Value = size;
        }

        private void BrushSizeChanged(object sender, EventArgs e)
        {
            int requested = (int)radiusInput.Value;
            int chosen = requested;
            if ((chosen & (chosen - 1)) != 0)
            {
                if (requested > lastBrushSizeValue)
                    chosen = Math.Min(256, lastBrushSizeValue * 2);
                else
                    chosen = Math.Max(1, lastBrushSizeValue / 2);
                radiusInput.Value = chosen;
            }
            lastBrushSizeValue = chosen;
            UpdateBrushDisplay();
        }

        private void ChangeZoom(bool zoomIn)
        {
            decimal current = previewInput.Value;
            decimal change = Math.Max(1m, Math.Round(current * 0.04m));
            decimal changed = zoomIn ? current - change : current + change;
            previewInput.Value = Math.Max(previewInput.Minimum, Math.Min(previewInput.Maximum, changed));
            statusLabel.Text = T("Zoom podgladu: ", "Preview zoom: ") + previewInput.Value + " m";
        }

        private void ResetZoom()
        {
            previewInput.Value = Math.Max(previewInput.Minimum, Math.Min(previewInput.Maximum, settings.PreviewMeters));
            statusLabel.Text = T("Zoom podgladu zresetowany: ", "Preview zoom reset: ") + previewInput.Value + " m";
        }

        private void FormMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
                PickMaterialFromBrushCenter();
        }

        // Eyedropper: middle mouse button picks material under the brush center (live player position).
        private void PickMaterialFromBrushCenter()
        {
            if (mask == null)
                return;
            if (!hasPosition)
            {
                statusLabel.Text = T("Brak pozycji – nie można użyć pipety.", "No position – cannot use eyedropper.");
                return;
            }
            int x = (int)Math.Round(currentPosition.X * (mask.Width - 1) / settings.WorldWidth);
            int y = mask.Height - 1 - (int)Math.Round(currentPosition.Z * (mask.Height - 1) / settings.WorldHeight);
            x = Math.Max(0, Math.Min(mask.Width - 1, x));
            y = Math.Max(0, Math.Min(mask.Height - 1, y));
            int packed = mask.ReadPacked(x, y);
            for (int index = 0; index < layerCombo.Items.Count; index++)
            {
                TerrainLayer layer = (TerrainLayer)layerCombo.Items[index];
                int layerColor = (layer.Color.R << 16) | (layer.Color.G << 8) | layer.Color.B;
                if (layerColor == packed)
                {
                    layerCombo.SelectedIndex = index;
                    if (eraserCheck.Checked)
                        eraserCheck.Checked = false;
                    UpdateBrushDisplay();
                    statusLabel.Text = T("Pipeta (środek pędzla): ", "Eyedropper (brush center): ") + layer.Name
                        + "  RGB(" + layer.Color.R + ", " + layer.Color.G + ", " + layer.Color.B + ")";
                    return;
                }
            }
            statusLabel.Text = T("Kolor RGB(" + ((packed >> 16) & 255) + ", " + ((packed >> 8) & 255) + ", " + (packed & 255) + ") nie wystepuje w layers.cfg", "Color RGB(" + ((packed >> 16) & 255) + ", " + ((packed >> 8) & 255) + ", " + (packed & 255) + ") is not present in layers.cfg");
        }

        private void FormMouseWheel(object sender, MouseEventArgs e)
        {
            ChangeRadius(e.Delta > 0 ? 1 : -1);
        }

        private void GlobalMouseWheel(int delta)
        {
            if (!NativeMethods.IsBuldozerForeground() || IsDisposed || Disposing)
                return;
            int steps = Math.Max(1, Math.Abs(delta) / 120);
            int change = delta > 0 ? steps : -steps;
            BeginInvoke((MethodInvoker)delegate { ChangeRadius(change); });
        }

        private void FlushWorking()
        {
            mask.Flush();
            statusLabel.Text = T("Zapisano kopie robocza: ", "Working copy saved: ") + DateTime.Now.ToString("HH:mm:ss");
        }

        private void ClearWorkingMask()
        {
            DialogResult answer = MessageBox.Show(this,
                T("OSTRZEZENIE: wszystkie zmiany malowania w masce roboczej zostana utracone. Maska robocza zostanie odtworzona z maski zrodlowej. Czy kontynuowac?", "WARNING: all painted changes in the working mask will be lost. The working mask will be recreated from the source mask. Continue?"),
                T("Wyczysc maske robocza", "Clear working mask"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
                return;
            try
            {
                if (strokeActive)
                    EndStroke();
                mask.Dispose();
                mask = null;
                if (File.Exists(settings.WorkingMaskPath))
                    File.Delete(settings.WorkingMaskPath);
                File.Copy(settings.MaskPath, settings.WorkingMaskPath, false);
                Application.Restart();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "MaskOver", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ChangePreset()
        {
            string activePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "active_preset.txt");
            try
            {
                if (File.Exists(activePath))
                    File.Delete(activePath);
                Application.Restart();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "MaskOver", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveSettings(string key, string value, string secondKey = null, string secondValue = null)
        {
            string path = configPath;
            List<string> lines = File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : new List<string>();
            SetSettingLine(lines, key, value);
            if (secondKey != null) SetSettingLine(lines, secondKey, secondValue);
            File.WriteAllLines(path, lines.ToArray());
        }

        private bool EnsureMaskTileSettings()
        {
            if (settings.MaskTilePixels > 0 && settings.MaskTileOverlapPixels >= 0 && settings.MaskTileOverlapPixels < settings.MaskTilePixels && settings.MaskTilesInRow > 0 && settings.MaskTileMaxColors > 0)
                return true;
            using (MaskTileSettingsDialog dialog = new MaskTileSettingsDialog(settings.MaskTilePixels, settings.MaskTileOverlapPixels, settings.MaskTilesInRow, settings.MaskTileMaxColors))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return false;
                settings.MaskTilePixels = dialog.TilePixels;
                settings.MaskTileOverlapPixels = dialog.OverlapPixels;
                settings.MaskTilesInRow = dialog.TilesInRow;
                settings.MaskTileMaxColors = dialog.MaxColors;
                SaveSettings("MaskTilePixels", settings.MaskTilePixels.ToString(), "MaskTileOverlapPixels", settings.MaskTileOverlapPixels.ToString());
                SaveSettings("MaskTilesInRow", settings.MaskTilesInRow.ToString(), "MaskTileMaxColors", settings.MaskTileMaxColors.ToString());
                lastPreview = DateTime.MinValue;
                maskTileColorCheckDirty = true;
                return true;
            }
        }

        private void UpdateMaskTileColorCheck()
        {
            if (!checkMaskTileColorsCheck.Checked || !hasPosition || maskTileColorCheckRunning)
                return;
            if (strokeActive)
                return;
            if (settings.MaskTilePixels < 1 || settings.MaskTilesInRow < 1 || settings.MaskTileMaxColors < 1)
                return;
            int tileX;
            int tileY;
            TerrainPreviewBuilder.GetMaskTileCoordinates(mask, currentPosition.X, currentPosition.Z, settings.WorldWidth, settings.WorldHeight,
                settings.MaskTilePixels, settings.MaskTileOverlapPixels, settings.MaskTilesInRow, out tileX, out tileY);
            if (!maskTileColorCheckDirty && tileX == lastMaskTileX && tileY == lastMaskTileY)
                return;
            lastMaskTileX = tileX;
            lastMaskTileY = tileY;
            maskTileColorCheckDirty = false;
            maskTileColorCheckRunning = true;
            maskTileColorStatusLabel.Text = T("Kolory tile'a: sprawdzanie...", "Tile colors: checking...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                int count = CountMaskTileColors(tileX, tileY);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        maskTileColorCheckRunning = false;
                        bool exceeded = count > settings.MaskTileMaxColors;
                        maskTileColorStatusLabel.Text = exceeded
                            ? T("UWAGA: tile ma " + count + " kolorów (limit " + settings.MaskTileMaxColors + ")", "WARNING: tile has " + count + " colors (limit " + settings.MaskTileMaxColors + ")")
                            : T("Kolory tile'a: " + count + "/" + settings.MaskTileMaxColors, "Tile colors: " + count + "/" + settings.MaskTileMaxColors);
                        maskTileColorStatusLabel.ForeColor = exceeded ? Color.OrangeRed : Color.LightGreen;
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        private int CountMaskTileColors(int tileX, int tileY)
        {
            int advance = Math.Max(1, settings.MaskTilePixels - settings.MaskTileOverlapPixels);
            int minX = Math.Max(0, tileX * advance);
            int minY = Math.Max(0, tileY * advance);
            int maxX = Math.Min(mask.Width - 1, minX + settings.MaskTilePixels - 1);
            int maxY = Math.Min(mask.Height - 1, minY + settings.MaskTilePixels - 1);
            HashSet<int> colors = new HashSet<int>();
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    colors.Add(mask.ReadPacked(x, y));
            return colors.Count;
        }

        private static void SetSettingLine(List<string> lines, string key, string value)
        {
            for (int index = 0; index < lines.Count; index++)
            {
                if (lines[index].TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[index] = key + "=" + value;
                    return;
                }
            }
            lines.Add(key + "=" + value);
        }

        private void Export()
        {
            FlushWorking();
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "BMP 24-bit|*.bmp|PNG|*.png";
                dialog.FileName = "mask_export.bmp";
                dialog.InitialDirectory = GetPresetDirectory();
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                string destination = Path.GetFullPath(dialog.FileName);
                if (!IsInsidePreset(destination))
                {
                    MessageBox.Show(this, T("Eksport musi zostać zapisany w katalogu aktualnego presetu.", "The export must be saved in the current preset directory."), "MaskOver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Cursor = Cursors.WaitCursor;
                try
                {
                    if (Path.GetExtension(destination).Equals(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Bitmap image = mask.ToBitmap())
                            image.Save(destination, ImageFormat.Png);
                    }
                    else
                        ReplaceWithWorkingCopy(destination);
                    statusLabel.Text = T("Wyeksportowano: ", "Exported: ") + destination;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, T("Eksport", "Export"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
        }

        private void ExportChanges()
        {
            FlushWorking();
            List<TerrainLayer> usedLayers = new List<TerrainLayer>();
            HashSet<int> changedColors = null;
            using (BusyDialog busy = new BusyDialog(T("Analizowanie zmienionych pikseli...", "Scanning changed pixels...")))
            {
                busy.Start(delegate { return mask.FindChangedColors(originalMask); });
                busy.ShowDialog(this);
                if (busy.Error != null)
                {
                    MessageBox.Show(this, busy.Error.Message, T("Eksport zmian", "Export changes"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                changedColors = (HashSet<int>)busy.Result;
            }
            foreach (TerrainLayer layer in layers)
            {
                int color = (layer.Color.R << 16) | (layer.Color.G << 8) | layer.Color.B;
                if (changedColors.Contains(color))
                    usedLayers.Add(layer);
            }
            if (usedLayers.Count == 0)
            {
                MessageBox.Show(this, T("Nie ma jeszcze żadnych zmian odpowiadających warstwom z layers.cfg.", "There are no changes matching the layers in layers.cfg yet."), T("Eksport zmian", "Export changes"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (ExportChangesDialog dialog = new ExportChangesDialog(usedLayers, "mask_changes"))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Options == null)
                    return;
                ExportChangesOptions options = dialog.Options;
                using (BusyDialog busy = new BusyDialog(T("Eksportowanie zmian...", "Exporting changes...")))
                {
                    busy.Start(delegate { return ExportSelectedChanges(options, usedLayers); });
                    busy.ShowDialog(this);
                    if (busy.Error != null)
                    {
                        MessageBox.Show(this, busy.Error.Message, T("Eksport zmian", "Export changes"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        statusLabel.Text = T("Eksport zmian nie powiodl sie.", "Export changes failed.");
                    }
                    else
                    {
                        long totalChanged = (long)busy.Result;
                        statusLabel.Text = T("Wyeksportowano zmiany: ", "Exported changes: ") + totalChanged + T(" pikseli", " pixels");
                    }
                }
            }
        }

        private long ExportSelectedChanges(ExportChangesOptions options, List<TerrainLayer> usedLayers)
        {
            long totalChanged = 0;
            if (options.SeparateLayers)
            {
                foreach (TerrainLayer layer in usedLayers)
                {
                    int color = (layer.Color.R << 16) | (layer.Color.G << 8) | layer.Color.B;
                    if (!options.Colors.Contains(color))
                        continue;
                    HashSet<int> oneColor = new HashSet<int>();
                    oneColor.Add(color);
                    string destination = Path.Combine(GetPresetDirectory(), SafeExportName(options.BaseName + "_" + layer.Name) + (options.Png ? ".png" : ".bmp"));
                    totalChanged += ExportChangesFile(destination, oneColor, options.BackgroundMode, options.Png);
                }
            }
            else
            {
                string destination = Path.Combine(GetPresetDirectory(), SafeExportName(options.BaseName) + (options.Png ? ".png" : ".bmp"));
                totalChanged = ExportChangesFile(destination, options.Colors, options.BackgroundMode, options.Png);
            }
            return totalChanged;
        }

        private long ExportChangesFile(string destination, HashSet<int> colors, int backgroundMode, bool png)
        {
            string temporary = Path.Combine(GetPresetDirectory(), ".maskover_export.tmp.bmp");
            if (File.Exists(temporary)) File.Delete(temporary);
            File.Copy(settings.WorkingMaskPath, temporary, true);
            long changed;
            using (BmpSurface output = new BmpSurface(temporary, true))
                changed = mask.WriteSelectedDifferencesAgainst(originalMask, output, colors, backgroundMode);
            if (png)
            {
                using (BmpSurface exported = new BmpSurface(temporary, false))
                using (Bitmap image = exported.ToBitmap())
                    image.Save(destination, ImageFormat.Png);
                File.Delete(temporary);
            }
            else
            {
                ReplaceFile(temporary, destination);
            }
            return changed;
        }

        private void ReplaceWithWorkingCopy(string destination)
        {
            string temporary = Path.Combine(GetPresetDirectory(), ".maskover_full.tmp.bmp");
            if (File.Exists(temporary)) File.Delete(temporary);
            File.Copy(settings.WorkingMaskPath, temporary, true);
            ReplaceFile(temporary, destination);
        }

        private void ReplaceFile(string temporary, string destination)
        {
            string backup = destination + ".maskover.bak";
            if (File.Exists(destination))
            {
                if (File.Exists(backup)) File.Delete(backup);
                File.Replace(temporary, destination, backup, true);
            }
            else
                File.Move(temporary, destination);
        }

        private string GetPresetDirectory()
        {
            return Path.GetDirectoryName(configPath);
        }

        private bool IsInsidePreset(string path)
        {
            string root = Path.GetFullPath(GetPresetDirectory()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeExportName(string value)
        {
            StringBuilder result = new StringBuilder();
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char character in value)
            {
                bool forbidden = false;
                foreach (char invalidCharacter in invalid)
                    if (character == invalidCharacter) forbidden = true;
                result.Append(forbidden ? '_' : character);
            }
            return result.Length == 0 ? "export" : result.ToString();
        }

        private void ClosingForm(object sender, FormClosingEventArgs e)
        {
            timer.Stop();
            wheelHook.Dispose();
            showBuldozerBrushCheck.Checked = false;
            WriteBrushState();
            showTerrainPreviewCheck.Checked = false;
            WriteTerrainPreview(true);
            if (strokeActive)
                EndStroke();
            if (mask != null)
            {
                mask.Flush();
                mask.Dispose();
            }
            originalMask.Dispose();
            if (satellite != null) satellite.Dispose();
            if (!String.IsNullOrEmpty(satelliteTemporaryPath) && File.Exists(satelliteTemporaryPath))
            {
                try { File.Delete(satelliteTemporaryPath); }
                catch { }
            }
        }

        private static string PrepareRasterForBmp(string path)
        {
            if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                return path;
            string directory = Path.Combine(Path.GetTempPath(), "MaskOver");
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".bmp");
            using (Bitmap source = new Bitmap(path))
            using (Bitmap converted = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(converted))
            {
                graphics.DrawImageUnscaled(source, 0, 0);
                converted.Save(temporary, ImageFormat.Bmp);
            }
            return temporary;
        }
    }
}
