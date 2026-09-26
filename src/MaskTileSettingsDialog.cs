using System;
using System.Drawing;
using System.Windows.Forms;

namespace MaskOver
{
    internal sealed class MaskTileSettingsDialog : Form
    {
        private readonly NumericUpDown tilePixels;
        private readonly NumericUpDown overlapPixels;
        private readonly NumericUpDown tilesInRow;
        private readonly NumericUpDown maxColors;

        public int TilePixels { get { return (int)tilePixels.Value; } }
        public int OverlapPixels { get { return (int)overlapPixels.Value; } }
        public int TilesInRow { get { return (int)tilesInRow.Value; } }
        public int MaxColors { get { return (int)maxColors.Value; } }

        public MaskTileSettingsDialog(int currentTilePixels, int currentOverlapPixels, int currentTilesInRow, int currentMaxColors)
        {
            Text = Locale.T("Parametry tile maski", "Mask tile parameters");
            Width = 440;
            Height = 255;
            StartPosition = FormStartPosition.CenterParent;
            TopMost = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(22, 24, 28);
            ForeColor = Color.FromArgb(235, 238, 245);
            Font = new Font("Segoe UI", 10.0f);

            Label info = new Label();
            info.Left = 12;
            info.Top = 12;
            info.Width = 400;
            info.Height = 38;
            info.ForeColor = Color.FromArgb(230, 234, 242);
            info.Text = Locale.T("Wpisz wartości z Terrain Builder > Mapframe Properties.", "Enter values from Terrain Builder > Mapframe Properties.");

            tilePixels = AddNumber(Locale.T("Rozmiar tile maski [px]:", "Mask tile size [px]:"), currentTilePixels > 0 ? currentTilePixels : 512, 44);
            overlapPixels = AddNumber(Locale.T("Actual overlap [px]:", "Actual overlap [px]:"), currentTilePixels > 0 ? currentOverlapPixels : 0, 78);
            tilesInRow = AddNumber(Locale.T("Tiles in row:", "Tiles in row:"), currentTilesInRow > 0 ? currentTilesInRow : 1, 112);
            maxColors = AddNumber(Locale.T("Maks. kolorów na tile:", "Max colors per tile:"), currentMaxColors > 0 ? currentMaxColors : 16, 146);

            Button ok = new Button();
            ok.Text = Locale.T("Zapisz", "Save");
            ok.Left = 230;
            ok.Top = 198;
            ok.Width = 90;
            ok.Height = 30;
            ok.FlatStyle = FlatStyle.Flat;
            ok.FlatAppearance.BorderSize = 0;
            ok.BackColor = Color.FromArgb(45, 105, 170);
            ok.ForeColor = Color.White;
            ok.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            ok.Click += delegate
            {
                if (OverlapPixels >= TilePixels || TilesInRow < 1)
                {
                    MessageBox.Show(this, Locale.T("Overlap musi być mniejszy od rozmiaru tile'a, a Tiles in row musi być większe od zera.", "Overlap must be smaller than the tile size, and Tiles in row must be greater than zero."), "MaskOver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            Button cancel = new Button();
            cancel.Text = Locale.T("Anuluj", "Cancel");
            cancel.Left = 328;
            cancel.Top = 198;
            cancel.Width = 90;
            cancel.Height = 30;
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.FlatAppearance.BorderSize = 0;
            cancel.BackColor = Color.FromArgb(55, 60, 70);
            cancel.ForeColor = Color.White;
            cancel.DialogResult = DialogResult.Cancel;

            Controls.Add(info);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private NumericUpDown AddNumber(string caption, int value, int top)
        {
            Label label = new Label();
            label.Text = caption;
            label.Left = 12;
            label.Top = top + 3;
            label.Width = 220;
            NumericUpDown number = new NumericUpDown();
            number.Left = 240;
            number.Top = top;
            number.Width = 90;
            number.Minimum = 0;
            number.Maximum = 65536;
            number.Value = Math.Max((int)number.Minimum, Math.Min((int)number.Maximum, value));
            Controls.Add(label);
            Controls.Add(number);
            return number;
        }
    }
}
