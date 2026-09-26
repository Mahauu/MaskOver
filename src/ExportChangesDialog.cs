using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MaskOver
{
    internal sealed class ExportChangesOptions
    {
        public int BackgroundMode;
        public bool SeparateLayers;
        public bool Png;
        public string BaseName;
        public readonly HashSet<int> Colors = new HashSet<int>();
    }

    internal sealed class ExportChangesDialog : Form
    {
        private readonly CheckedListBox layers;
        private readonly ComboBox background;
        private readonly ComboBox format;
        private readonly CheckBox separate;
        private readonly TextBox baseName;
        private readonly List<TerrainLayer> sourceLayers;
        public ExportChangesOptions Options { get; private set; }

        public ExportChangesDialog(List<TerrainLayer> availableLayers, string defaultName)
        {
            sourceLayers = availableLayers;
            Text = "Export changes";
            Width = 530;
            Height = 560;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            BackColor = Color.FromArgb(22, 24, 28);
            ForeColor = Color.FromArgb(235, 238, 245);
            Font = new Font("Segoe UI", 10.0f);

            Label backgroundLabel = new Label { Text = "Background:", Left = 15, Top = 18, AutoSize = true, ForeColor = Color.FromArgb(230, 234, 242) };
            background = new ComboBox { Left = 140, Top = 14, Width = 350, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            background.BackColor = Color.FromArgb(38, 42, 50);
            background.ForeColor = Color.FromArgb(235, 238, 245);
            background.Items.Add("Black");
            background.Items.Add("White");
            background.Items.Add("Original source mask");
            background.SelectedIndex = 0;

            Label layersLabel = new Label { Text = "Layers to export:", Left = 15, Top = 55, AutoSize = true, ForeColor = Color.FromArgb(230, 234, 242) };
            layers = new CheckedListBox
            {
                Left = 15,
                Top = 78,
                Width = 480,
                Height = 280,
                CheckOnClick = true,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 26,
                BackColor = Color.FromArgb(38, 42, 50),
                ForeColor = Color.FromArgb(235, 238, 245),
                BorderStyle = BorderStyle.FixedSingle
            };
            layers.DrawItem += DrawLayerItem;
            foreach (TerrainLayer layer in availableLayers)
                layers.Items.Add(layer, true);

            Label formatLabel = new Label { Text = "Format:", Left = 15, Top = 375, AutoSize = true, ForeColor = Color.FromArgb(230, 234, 242) };
            format = new ComboBox { Left = 140, Top = 371, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            format.BackColor = Color.FromArgb(38, 42, 50);
            format.ForeColor = Color.FromArgb(235, 238, 245);
            format.Items.Add("BMP");
            format.Items.Add("PNG");
            format.SelectedIndex = 0;
            separate = new CheckBox
            {
                Text = "Separate file for each layer",
                Left = 280,
                Top = 373,
                AutoSize = true,
                ForeColor = Color.FromArgb(230, 234, 242)
            };
            Label nameLabel = new Label { Text = "Base name:", Left = 15, Top = 418, AutoSize = true, ForeColor = Color.FromArgb(230, 234, 242) };
            baseName = new TextBox
            {
                Left = 140,
                Top = 414,
                Width = 350,
                Text = defaultName,
                BackColor = Color.FromArgb(38, 42, 50),
                ForeColor = Color.FromArgb(235, 238, 245),
                BorderStyle = BorderStyle.FixedSingle
            };

            Button ok = MakeButton("OK", 300, 465, 90);
            ok.DialogResult = DialogResult.OK;
            ok.Click += Confirm;
            Button cancel = MakeButton("Cancel", 400, 465, 90);
            cancel.BackColor = Color.FromArgb(55, 60, 70);
            cancel.DialogResult = DialogResult.Cancel;

            Controls.Add(backgroundLabel);
            Controls.Add(background);
            Controls.Add(layersLabel);
            Controls.Add(layers);
            Controls.Add(formatLabel);
            Controls.Add(format);
            Controls.Add(separate);
            Controls.Add(nameLabel);
            Controls.Add(baseName);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static Button MakeButton(string text, int left, int top, int width)
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

        private void DrawLayerItem(object sender, DrawItemEventArgs e)
        {
            Color bg = (e.State & DrawItemState.Selected) != 0
                ? Color.FromArgb(45, 105, 170)
                : Color.FromArgb(38, 42, 50);
            using (SolidBrush bgBrush = new SolidBrush(bg))
                e.Graphics.FillRectangle(bgBrush, e.Bounds);
            if (e.Index < 0 || e.Index >= sourceLayers.Count)
                return;
            TerrainLayer layer = sourceLayers[e.Index];
            bool checkedState = layers.GetItemChecked(e.Index);
            Rectangle check = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 5, 15, 15);
            ControlPaint.DrawCheckBox(e.Graphics, check, checkedState ? ButtonState.Checked : ButtonState.Normal);
            Rectangle swatch = new Rectangle(e.Bounds.Left + 26, e.Bounds.Top + 5, 30, 15);
            using (Brush brush = new SolidBrush(layer.Color))
                e.Graphics.FillRectangle(brush, swatch);
            using (Pen border = new Pen(Color.FromArgb(200, 205, 215)))
                e.Graphics.DrawRectangle(border, swatch);
            using (Brush textBrush = new SolidBrush(Color.FromArgb(235, 238, 245)))
                e.Graphics.DrawString(layer.ToString(), e.Font, textBrush, e.Bounds.Left + 62, e.Bounds.Top + 4);
        }

        private void Confirm(object sender, EventArgs e)
        {
            if (String.IsNullOrWhiteSpace(baseName.Text))
            {
                MessageBox.Show(this, "Enter an export name.", "MaskOver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            Options = new ExportChangesOptions();
            Options.BackgroundMode = background.SelectedIndex;
            Options.SeparateLayers = separate.Checked;
            Options.Png = format.SelectedIndex == 1;
            Options.BaseName = baseName.Text.Trim();
            for (int index = 0; index < layers.Items.Count; index++)
                if (layers.GetItemChecked(index))
                {
                    TerrainLayer layer = sourceLayers[index];
                    Options.Colors.Add((layer.Color.R << 16) | (layer.Color.G << 8) | layer.Color.B);
                }
            if (Options.Colors.Count == 0)
            {
                MessageBox.Show(this, "Select at least one layer.", "MaskOver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        }
    }
}
