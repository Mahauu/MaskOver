using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MaskOver
{
    internal sealed class BusyDialog : Form
    {
        private readonly Label label;
        private readonly ProgressBar progress;
        public object Result { get; private set; }
        public Exception Error { get; private set; }

        public BusyDialog(string message)
        {
            Text = "MaskOver";
            Width = 430;
            Height = 130;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            TopMost = true;
            BackColor = Color.FromArgb(22, 24, 28);
            ForeColor = Color.FromArgb(235, 238, 245);
            Font = new Font("Segoe UI", 10.0f);
            label = new Label
            {
                Text = message,
                Left = 15,
                Top = 18,
                AutoSize = true,
                ForeColor = Color.FromArgb(230, 234, 242)
            };
            progress = new ProgressBar
            {
                Left = 15,
                Top = 52,
                Width = 385,
                Height = 22,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30
            };
            Controls.Add(label);
            Controls.Add(progress);
        }

        public void Start(Func<object> operation)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Result = operation();
                }
                catch (Exception ex)
                {
                    Error = ex;
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate { DialogResult = DialogResult.OK; Close(); });
                }
                catch (InvalidOperationException)
                {
                }
            });
        }
    }
}
