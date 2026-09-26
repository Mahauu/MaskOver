using System;
using System.Windows.Forms;

namespace MaskOver
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = SelfTest.Run();
                return;
            }
            if (args.Length > 3 && args[0].Equals("--render-preview", StringComparison.OrdinalIgnoreCase))
            {
                double x = Double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
                double z = Double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                string projectDirectory = System.IO.Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar)).FullName;
                AppSettings settings = AppSettings.Load(System.IO.Path.Combine(projectDirectory, "MaskOver.ini"));
                using (BmpSurface mask = new BmpSurface(settings.WorkingMaskPath, false))
                using (BmpSurface satellite = String.IsNullOrWhiteSpace(settings.SatellitePath) ? null : new BmpSurface(settings.SatellitePath, false))
                using (System.Drawing.Bitmap bitmap = (satellite ?? mask).RenderPreview(mask, x, z, settings.WorldWidth, settings.WorldHeight, settings.PreviewMeters, 600, satellite == null ? 1.0 : settings.MaskOpacity))
                    bitmap.Save(args[3], System.Drawing.Imaging.ImageFormat.Png);
                Environment.ExitCode = 0;
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), Locale.T("MaskOver - blad", "MaskOver - error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
