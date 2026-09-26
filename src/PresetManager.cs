using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MaskOver
{
    internal sealed class PresetInfo
    {
        public string Name;
        public string DirectoryPath;
        public string ConfigPath;

        public override string ToString()
        {
            return Name;
        }
    }

    internal static class PresetManager
    {
        public static string PresetsDirectory(string baseDirectory)
        {
            return Path.Combine(baseDirectory, "presets");
        }

        public static PresetInfo SelectOrCreate(Form owner, string baseDirectory)
        {
            Form startupOwner = null;
            Form dialogOwner = owner;
            if (dialogOwner == null)
            {
                startupOwner = new Form();
                startupOwner.ShowInTaskbar = false;
                startupOwner.FormBorderStyle = FormBorderStyle.None;
                startupOwner.StartPosition = FormStartPosition.Manual;
                startupOwner.Location = new System.Drawing.Point(-32000, -32000);
                startupOwner.Size = new System.Drawing.Size(1, 1);
                startupOwner.Opacity = 0.0;
                startupOwner.TopMost = true;
                startupOwner.Show();
                dialogOwner = startupOwner;
            }
            try
            {
                Directory.CreateDirectory(PresetsDirectory(baseDirectory));
                while (true)
                {
                    List<PresetInfo> presets = List(baseDirectory);
                    using (PresetSelectionDialog dialog = new PresetSelectionDialog(presets, ReadActiveName(baseDirectory)))
                    {
                        DialogResult result = dialog.ShowDialog(dialogOwner);
                        if (result == DialogResult.OK && dialog.SelectedPreset != null)
                        {
                            WriteActiveName(baseDirectory, dialog.SelectedPreset.Name);
                            return dialog.SelectedPreset;
                        }
                        if (result != DialogResult.Retry)
                            return null;
                    }

                    PresetInfo created = null;
                    try
                    {
                        created = CreateInteractive(dialogOwner, baseDirectory);
                    }
                    catch (Exception ex)
                    {
                        Log(baseDirectory, "Preset creation failed: " + ex);
                        MessageBox.Show(dialogOwner, Locale.T("Nie udało się utworzyć presetu:\r\n" + ex.Message + "\r\n\r\nSzczegóły zapisano w startup.log.", "The preset could not be created:\r\n" + ex.Message + "\r\n\r\nDetails were saved to startup.log."), "MaskOver", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    if (created != null)
                    {
                        WriteActiveName(baseDirectory, created.Name);
                        return created;
                    }
                }
            }
            finally
            {
                if (startupOwner != null)
                {
                    startupOwner.Close();
                    startupOwner.Dispose();
                }
            }
        }

        private static List<PresetInfo> List(string baseDirectory)
        {
            List<PresetInfo> result = new List<PresetInfo>();
            string root = PresetsDirectory(baseDirectory);
            foreach (string directory in Directory.GetDirectories(root))
            {
                string config = Path.Combine(directory, "preset.ini");
                if (!File.Exists(config))
                    continue;
                AppSettings settings = AppSettings.Load(config);
                string name = String.IsNullOrWhiteSpace(settings.PresetName) ? Path.GetFileName(directory) : settings.PresetName;
                result.Add(new PresetInfo { Name = name, DirectoryPath = directory, ConfigPath = config });
            }
            result.Sort(delegate(PresetInfo left, PresetInfo right) { return StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name); });
            return result;
        }

        private static PresetInfo CreateInteractive(Form owner, string baseDirectory)
        {
            string name = TextPrompt.Show(owner, Locale.T("Nazwa nowego presetu:", "New preset name:"), "MaskOver", "Mapa1");
            if (String.IsNullOrWhiteSpace(name))
                return null;

            string maskSource = SelectFile(owner, Locale.T("Wybierz maskę źródłową BMP lub PNG", "Select source BMP or PNG mask"), "Maska (*.bmp;*.png)|*.bmp;*.png|BMP (*.bmp)|*.bmp|PNG (*.png)|*.png");
            if (String.IsNullOrEmpty(maskSource))
                return null;
            string layersSource = SelectFile(owner, Locale.T("Wybierz layers.cfg dla tego presetu", "Select layers.cfg for this preset"), "CFG (*.cfg)|*.cfg|Wszystkie pliki (*.*)|*.*");
            if (String.IsNullOrEmpty(layersSource))
                return null;
            string satelliteSource = SelectFile(owner, Locale.T("Wybierz satelitę BMP lub PNG (Anuluj = bez satelity)", "Select BMP or PNG satellite (Cancel = no satellite)"), "Satelita (*.bmp;*.png)|*.bmp;*.png|BMP (*.bmp)|*.bmp|PNG (*.png)|*.png");
            string profileDirectory = FindDefaultProfileDirectory();
            if (String.IsNullOrEmpty(profileDirectory))
                profileDirectory = SelectFolder(owner, Locale.T("Wybierz katalog profilu Buldozera używany przez $profile:", "Select the Buldozer profile directory used by $profile:"));
            if (String.IsNullOrEmpty(profileDirectory))
            {
                MessageBox.Show(owner, Locale.T("Katalog profilu Buldozera jest wymagany, aby pozycja mogła być odczytywana.", "The Buldozer profile directory is required to read the live position."), "MaskOver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            int maskWidth;
            int maskHeight;
            using (Bitmap selectedMask = new Bitmap(maskSource))
            {
                maskWidth = selectedMask.Width;
                maskHeight = selectedMask.Height;
            }
            if (!String.IsNullOrEmpty(satelliteSource))
            using (Bitmap selectedSatellite = new Bitmap(satelliteSource))
            {
                if (maskWidth != selectedSatellite.Width || maskHeight != selectedSatellite.Height)
                    throw new InvalidDataException(Locale.T("Maska i satelita mają różne wymiary.", "The mask and satellite have different dimensions."));
            }
            LayersParser.Parse(layersSource);

            // World size in meters (Terrain Builder Mapframe). Not the mask pixel resolution.
            // Default to mask pixel size when square (common 1 m/px custom maps); otherwise Chernarus-like 15360.
            string defaultWorld = maskWidth == maskHeight && maskWidth > 0
                ? maskWidth.ToString(CultureInfo.InvariantCulture)
                : "15360";
            string worldWidthText = TextPrompt.Show(owner,
                Locale.T(
                    "Szerokość terenu w metrach (WorldWidth).\r\nZ Terrain Builder → Mapframe Properties.\r\n\r\nMaska: " + maskWidth + " px\r\nDomyślnie: " + defaultWorld + " m",
                    "Terrain width in meters (WorldWidth).\r\nFrom Terrain Builder → Mapframe Properties.\r\n\r\nMask size: " + maskWidth + " px\r\nDefault: " + defaultWorld + " m"),
                "MaskOver – World width", defaultWorld);
            if (String.IsNullOrWhiteSpace(worldWidthText))
                return null;
            string worldHeightText = TextPrompt.Show(owner,
                Locale.T(
                    "Wysokość terenu w metrach (WorldHeight).\r\nZ Terrain Builder → Mapframe Properties.\r\n\r\nMaska: " + maskHeight + " px\r\nDomyślnie: " + defaultWorld + " m",
                    "Terrain height in meters (WorldHeight).\r\nFrom Terrain Builder → Mapframe Properties.\r\n\r\nMask size: " + maskHeight + " px\r\nDefault: " + defaultWorld + " m"),
                "MaskOver – World height", String.IsNullOrWhiteSpace(worldWidthText) ? defaultWorld : worldWidthText.Trim());
            if (String.IsNullOrWhiteSpace(worldHeightText))
                return null;
            double worldWidth;
            double worldHeight;
            if (!Double.TryParse(worldWidthText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out worldWidth) || worldWidth < 1.0)
            {
                MessageBox.Show(owner, Locale.T("Nieprawidłowa szerokość terenu.", "Invalid terrain width."), "MaskOver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            if (!Double.TryParse(worldHeightText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out worldHeight) || worldHeight < 1.0)
            {
                MessageBox.Show(owner, Locale.T("Nieprawidłowa wysokość terenu.", "Invalid terrain height."), "MaskOver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            string safeName = SafeDirectoryName(name);
            string presetDirectory = Path.Combine(PresetsDirectory(baseDirectory), safeName);
            int suffix = 2;
            while (Directory.Exists(presetDirectory))
            {
                presetDirectory = Path.Combine(PresetsDirectory(baseDirectory), safeName + "_" + suffix.ToString(CultureInfo.InvariantCulture));
                suffix++;
            }
            Directory.CreateDirectory(presetDirectory);
            try
            {
                string workingPath = Path.Combine(presetDirectory, "mask_work.bmp");
                string storedMaskPath = maskSource;
                if (Path.GetExtension(maskSource).Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    storedMaskPath = Path.Combine(presetDirectory, "mask_source.bmp");
                    ConvertImageToBmp(maskSource, storedMaskPath);
                }
                File.Copy(storedMaskPath, workingPath, false);
                string satellitePath = String.IsNullOrEmpty(satelliteSource) ? "" : Path.GetFullPath(satelliteSource);
                string configPath = Path.Combine(presetDirectory, "preset.ini");
                string bridgePath = String.IsNullOrEmpty(profileDirectory) ? "" : Path.Combine(profileDirectory, "MaskOver.cursor");
                string brushPath = String.IsNullOrEmpty(profileDirectory) ? "" : Path.Combine(profileDirectory, "MaskOver.brush");
                string terrainPath = String.IsNullOrEmpty(profileDirectory) ? "" : Path.Combine(profileDirectory, "MaskOver.terrain");
                List<string> lines = new List<string>();
                lines.Add("PresetName=" + name);
                lines.Add("MaskPath=" + Path.GetFullPath(storedMaskPath));
                lines.Add("SatellitePath=" + satellitePath);
                lines.Add("LayersPath=" + Path.GetFullPath(layersSource));
                lines.Add("WorkingMaskPath=" + workingPath);
                lines.Add("BridgePath=" + bridgePath);
                lines.Add("BrushStatePath=" + brushPath);
                lines.Add("TerrainPreviewPath=" + terrainPath);
                lines.Add("Language=en");
                lines.Add("WorldWidth=" + worldWidth.ToString(CultureInfo.InvariantCulture));
                lines.Add("WorldHeight=" + worldHeight.ToString(CultureInfo.InvariantCulture));
                lines.Add("PreviewMeters=600");
                lines.Add("TerrainPreviewMeters=100");
                lines.Add("BrushRadius=8");
                lines.Add("BrushStrength=100");
                lines.Add("MaskOpacity=0.58");
                File.WriteAllLines(configPath, lines.ToArray(), Encoding.UTF8);
                return new PresetInfo { Name = name, DirectoryPath = presetDirectory, ConfigPath = configPath };
            }
            catch
            {
                throw;
            }
        }

        private static void Log(string baseDirectory, string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(baseDirectory, "startup.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string SelectFile(Form owner, string title, string filter = "BMP (*.bmp)|*.bmp|Wszystkie pliki (*.*)|*.*")
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = title;
                dialog.Filter = filter;
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : "";
            }
        }

        private static void ConvertImageToBmp(string sourcePath, string destinationPath)
        {
            using (Bitmap source = new Bitmap(sourcePath))
            using (Bitmap converted = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(converted))
            {
                graphics.DrawImageUnscaled(source, 0, 0);
                converted.Save(destinationPath, ImageFormat.Bmp);
            }
        }

        private static string SelectFolder(Form owner, string description)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = description;
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : "";
            }
        }

        public static string FindDefaultProfileDirectory()
        {
            const string defaultProfile = @"P:\Buldozer";
            return Directory.Exists(defaultProfile) ? defaultProfile : "";
        }

        private static string SafeDirectoryName(string value)
        {
            StringBuilder result = new StringBuilder();
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char character in value.Trim())
            {
                bool forbidden = false;
                foreach (char invalidCharacter in invalid)
                    if (character == invalidCharacter) forbidden = true;
                result.Append(forbidden ? '_' : character);
            }
            return result.Length == 0 ? "Preset" : result.ToString();
        }

        private static string ActivePath(string baseDirectory)
        {
            return Path.Combine(baseDirectory, "active_preset.txt");
        }

        private static string ReadActiveName(string baseDirectory)
        {
            string path = ActivePath(baseDirectory);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }

        private static void WriteActiveName(string baseDirectory, string name)
        {
            File.WriteAllText(ActivePath(baseDirectory), name ?? "", Encoding.UTF8);
        }
    }

    internal sealed class PresetSelectionDialog : Form
    {
        private readonly ComboBox presetCombo;
        public PresetInfo SelectedPreset { get; private set; }

        public PresetSelectionDialog(List<PresetInfo> presets, string activeName)
        {
            Text = "Select map preset";
            Width = 460;
            Height = 185;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(22, 24, 28);
            ForeColor = Color.FromArgb(235, 238, 245);
            Font = new Font("Segoe UI", 10.0f);

            Label label = new Label
            {
                Text = "Map preset:",
                AutoSize = true,
                Left = 15,
                Top = 22,
                ForeColor = Color.FromArgb(230, 234, 242)
            };
            presetCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Left = 120,
                Top = 18,
                Width = 300,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(38, 42, 50),
                ForeColor = Color.FromArgb(235, 238, 245),
                Font = new Font("Segoe UI", 10.0f)
            };
            foreach (PresetInfo preset in presets) presetCombo.Items.Add(preset);
            for (int index = 0; index < presetCombo.Items.Count; index++)
                if (((PresetInfo)presetCombo.Items[index]).Name.Equals(activeName, StringComparison.CurrentCultureIgnoreCase)) presetCombo.SelectedIndex = index;
            if (presetCombo.SelectedIndex < 0 && presetCombo.Items.Count > 0) presetCombo.SelectedIndex = 0;

            Button select = MakeDialogButton("Select", 220, 95, 95);
            select.DialogResult = DialogResult.OK;
            Button create = MakeDialogButton("New...", 325, 95, 95);
            create.BackColor = Color.FromArgb(55, 60, 70);
            create.Click += delegate { DialogResult = DialogResult.Retry; Close(); };
            select.Click += delegate { SelectedPreset = presetCombo.SelectedItem as PresetInfo; if (SelectedPreset == null) DialogResult = DialogResult.Retry; };

            Controls.Add(label);
            Controls.Add(presetCombo);
            Controls.Add(select);
            Controls.Add(create);
            AcceptButton = select;
        }

        private static Button MakeDialogButton(string text, int left, int top, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Left = left;
            button.Top = top;
            button.Width = width;
            button.Height = 32;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Color.FromArgb(45, 105, 170);
            button.ForeColor = Color.White;
            button.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            return button;
        }
    }

    internal static class TextPrompt
    {
        public static string Show(Form owner, string prompt, string title, string initial)
        {
            using (Form dialog = new Form())
            using (TextBox input = new TextBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                const int contentWidth = 460;
                dialog.Text = title;
                dialog.ClientSize = new Size(contentWidth + 28, 220);
                dialog.StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
                dialog.TopMost = true;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.BackColor = Color.FromArgb(22, 24, 28);
                dialog.ForeColor = Color.FromArgb(235, 238, 245);
                dialog.Font = new Font("Segoe UI", 10.0f);

                Label label = new Label
                {
                    Text = prompt,
                    Left = 14,
                    Top = 14,
                    Width = contentWidth,
                    AutoSize = false,
                    MaximumSize = new Size(contentWidth, 0),
                    AutoEllipsis = false,
                    ForeColor = Color.FromArgb(230, 234, 242)
                };
                // Measure wrapped text height so the dialog fits the message.
                Size measured = TextRenderer.MeasureText(prompt, dialog.Font, new Size(contentWidth, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                label.Height = Math.Max(40, measured.Height + 4);

                input.Left = 14;
                input.Top = label.Bottom + 12;
                input.Width = contentWidth;
                input.Height = 28;
                input.Text = initial;
                input.Font = new Font("Segoe UI", 11.0f);
                input.BackColor = Color.FromArgb(38, 42, 50);
                input.ForeColor = Color.FromArgb(235, 238, 245);
                input.BorderStyle = BorderStyle.FixedSingle;

                int buttonTop = input.Bottom + 16;
                ok.Text = "OK";
                ok.DialogResult = DialogResult.OK;
                ok.Width = 100;
                ok.Height = 32;
                ok.Left = contentWidth + 14 - 210;
                ok.Top = buttonTop;
                ok.FlatStyle = FlatStyle.Flat;
                ok.FlatAppearance.BorderSize = 0;
                ok.BackColor = Color.FromArgb(45, 105, 170);
                ok.ForeColor = Color.White;
                ok.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Width = 100;
                cancel.Height = 32;
                cancel.Left = contentWidth + 14 - 100;
                cancel.Top = buttonTop;
                cancel.FlatStyle = FlatStyle.Flat;
                cancel.FlatAppearance.BorderSize = 0;
                cancel.BackColor = Color.FromArgb(55, 60, 70);
                cancel.ForeColor = Color.White;
                cancel.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);

                dialog.ClientSize = new Size(contentWidth + 28, buttonTop + 48);
                dialog.Controls.Add(label);
                dialog.Controls.Add(input);
                dialog.Controls.Add(ok);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                return dialog.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : "";
            }
        }
    }
}
