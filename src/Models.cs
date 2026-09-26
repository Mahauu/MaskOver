using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace MaskOver
{
    internal sealed class AppSettings
    {
        public string PresetName = "";
        public string MaskPath = "";
        public string SatellitePath = "";
        public string LayersPath = "";
        public string WorkingMaskPath = "";
        public string BridgePath = "";
        public string BrushStatePath = "";
        public string TerrainPreviewPath = "";
        public string Language = "pl";
        public double WorldWidth = 15360.0;
        public double WorldHeight = 15360.0;
        public int PreviewMeters = 600;
        public int TerrainPreviewMeters = 100;
        public int MaskTilePixels;
        public int MaskTileOverlapPixels;
        public int MaskTilesInRow;
        public int MaskTileMaxColors;
        public int BrushRadius = 8;
        public int BrushStrength = 100;
        public double MaskOpacity = 0.58;

        public static AppSettings Load(string path)
        {
            AppSettings settings = new AppSettings();
            if (!File.Exists(path))
                return settings;

            string[] lines = File.ReadAllLines(path);
            foreach (string original in lines)
            {
                string line = original.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                int separator = line.IndexOf('=');
                if (separator < 1)
                    continue;
                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                double number;
                int integer;
                if (key.Equals("PresetName", StringComparison.OrdinalIgnoreCase)) settings.PresetName = value;
                else if (key.Equals("MaskPath", StringComparison.OrdinalIgnoreCase)) settings.MaskPath = value;
                else if (key.Equals("SatellitePath", StringComparison.OrdinalIgnoreCase)) settings.SatellitePath = value;
                else if (key.Equals("LayersPath", StringComparison.OrdinalIgnoreCase)) settings.LayersPath = value;
                else if (key.Equals("WorkingMaskPath", StringComparison.OrdinalIgnoreCase)) settings.WorkingMaskPath = value;
                else if (key.Equals("BridgePath", StringComparison.OrdinalIgnoreCase)) settings.BridgePath = value;
                else if (key.Equals("BrushStatePath", StringComparison.OrdinalIgnoreCase)) settings.BrushStatePath = value;
                else if (key.Equals("TerrainPreviewPath", StringComparison.OrdinalIgnoreCase)) settings.TerrainPreviewPath = value;
                else if (key.Equals("Language", StringComparison.OrdinalIgnoreCase)) settings.Language = value.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : "pl";
                else if (key.Equals("WorldWidth", StringComparison.OrdinalIgnoreCase) && Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) settings.WorldWidth = number;
                else if (key.Equals("WorldHeight", StringComparison.OrdinalIgnoreCase) && Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) settings.WorldHeight = number;
                else if (key.Equals("PreviewMeters", StringComparison.OrdinalIgnoreCase) && Int32.TryParse(value, out integer)) settings.PreviewMeters = integer;
                else if (key.Equals("TerrainPreviewMeters", StringComparison.OrdinalIgnoreCase) && Int32.TryParse(value, out integer)) settings.TerrainPreviewMeters = Math.Max(10, Math.Min(150, integer));
                else if (key.Equals("MaskTilePixels", StringComparison.OrdinalIgnoreCase) && Int32.TryParse(value, out integer)) settings.MaskTilePixels = Math.Max(0, integer);
                else if (key.Equals("MaskTileOverlapPixels", StringComparison.OrdinalIgnoreCase) && Int32.TryParse(value, out integer)) settings.MaskTileOverlapPixels = Math.Max(0, integer);
                else if (key.Equals("MaskTilesInRow", StringComparison.OrdinalIgnoreCase) && Int32.TryParse(value, out integer)) settings.MaskTilesInRow = Math.Max(0, integer);
                else if (key.Equals("MaskTileMaxColors", StringComparison.OrdinalIgnoreCase) && Int32.TryParse(value, out integer)) settings.MaskTileMaxColors = Math.Max(0, integer);
                else if (key.Equals("BrushRadius", StringComparison.OrdinalIgnoreCase) && Int32.TryParse(value, out integer)) settings.BrushRadius = integer;
                else if (key.Equals("BrushStrength", StringComparison.OrdinalIgnoreCase) && Int32.TryParse(value, out integer)) settings.BrushStrength = Math.Max(1, Math.Min(100, integer));
                else if (key.Equals("MaskOpacity", StringComparison.OrdinalIgnoreCase) && Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) settings.MaskOpacity = number;
            }
            return settings;
        }
    }

    internal sealed class TerrainLayer
    {
        public string Name;
        public Color Color;

        public TerrainLayer(string name, Color color)
        {
            Name = name;
            Color = color;
        }

        public override string ToString()
        {
            return Name + "  RGB(" + Color.R + ", " + Color.G + ", " + Color.B + ")";
        }
    }

    internal static class LayersParser
    {
        public static List<TerrainLayer> Parse(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(Locale.T("Nie znaleziono layers.cfg", "layers.cfg was not found"), path);
            string text = File.ReadAllText(path);
            Regex regex = new Regex(@"(?m)^\s*([A-Za-z_][A-Za-z0-9_]*)\s*\[\]\s*=\s*\{\s*\{?\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\}?\s*\}\s*;", RegexOptions.Compiled);
            List<TerrainLayer> result = new List<TerrainLayer>();
            foreach (Match match in regex.Matches(text))
            {
                int red = Int32.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                int green = Int32.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                int blue = Int32.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
                result.Add(new TerrainLayer(match.Groups[1].Value, Color.FromArgb(red, green, blue)));
            }
            if (result.Count == 0)
                throw new InvalidDataException(Locale.T("Nie znaleziono kolorow legendy w ", "No legend colors found in ") + path);
            return result;
        }
    }

    internal struct WorldPosition
    {
        public double X;
        public double Y;
        public double Z;
        public DateTime Timestamp;
        public string Source;
        public double DirectionX;
        public double DirectionZ;
        public bool HasDirection;

        public WorldPosition(double x, double y, double z, string source)
        {
            X = x;
            Y = y;
            Z = z;
            Timestamp = DateTime.UtcNow;
            Source = source;
            DirectionX = 0.0;
            DirectionZ = 1.0;
            HasDirection = false;
        }

        public double HeadingDegrees
        {
            get
            {
                double heading = Math.Atan2(DirectionX, DirectionZ) * 180.0 / Math.PI;
                while (heading < 0.0) heading += 360.0;
                while (heading >= 360.0) heading -= 360.0;
                return heading;
            }
        }
    }

    internal sealed class PixelDelta
    {
        public long Offset;
        public int Before;
        public int After;
    }

    internal sealed class StrokeHistory
    {
        public readonly List<PixelDelta> Pixels = new List<PixelDelta>();
        public readonly string LayerName;

        public StrokeHistory(string layerName)
        {
            LayerName = layerName;
        }
    }
}
