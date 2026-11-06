// The window shown while the server's files are read, so it's always clear what's happening.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace PriTaTor.UI
{
    public sealed class LoadingForm : Form
    {
        readonly Label step = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.BottomLeft };
        readonly Label detail = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 44, TextAlign = ContentAlignment.TopLeft };
        readonly ProgressBar bar = new ProgressBar { Dock = DockStyle.Top, Height = 8, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
        readonly DateTime started = DateTime.Now;
        readonly Timer clock = new Timer { Interval = 500 };
        bool everPreparing;

        /// <summary>Raised when the bar has a real count, for automated screenshots.</summary>
        public event Action<int, int> Counted;

        public LoadingForm(bool showCancel, Form owner = null)
        {
            Text = "PriTaTor Reborn";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
            ShowInTaskbar = owner == null;
            BackColor = Color.FromArgb(29, 34, 41);
            ForeColor = Color.FromArgb(232, 236, 233);
            Font = Ui.BaseFont;
            Padding = new Padding(22, 18, 22, 12);
            ClientSize = new Size(500, 196);
            UseWaitCursor = true;
            step.Text = "Starting up…";
            detail.ForeColor = Color.FromArgb(154, 168, 160);
            detail.Padding = new Padding(0, 6, 0, 0);

            var title = new Label { Text = "PriTaTor Reborn", Dock = DockStyle.Top, Height = 38, Font = new Font("Segoe UI Semibold", 18f), ForeColor = Color.White };
            var version = new Label { Text = "Priston Tale server manager and map editor", Dock = DockStyle.Top, Height = 24, ForeColor = detail.ForeColor };
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 28 };
            if (showCancel)
            {
                var cancel = new Button { Text = "Cancel", Dock = DockStyle.Right, Width = 84, FlatStyle = FlatStyle.Flat, ForeColor = ForeColor, UseWaitCursor = false, Cursor = Cursors.Default };
                cancel.FlatAppearance.BorderColor = Color.FromArgb(70, 80, 90);
                cancel.Click += (s, e) => Application.Exit();
                bottom.Controls.Add(cancel);
            }
            // docked controls stack from the last one added
            Controls.Add(bottom);
            Controls.Add(detail);
            Controls.Add(bar);
            Controls.Add(step);
            Controls.Add(version);
            Controls.Add(title);
            Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(70, 80, 90)))
                    e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            };
            clock.Tick += (s, e) => UpdateDetail();
            clock.Start();
            FormClosed += (s, e) => clock.Dispose();
        }

        string note = "";

        void UpdateDetail()
        {
            int secs = (int)(DateTime.Now - started).TotalSeconds;
            // past a few seconds, show the time so a slow disk doesn't look like a crash
            string time = secs >= 3 ? secs + " seconds so far. " : "";
            string text = time + note;
            if (detail.Text != text) detail.Text = text;
        }

        /// <summary>Safe to call from any thread. A total above zero shows a real count on the bar.</summary>
        public void Report(string message, int n = 0, int total = 0)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(() => Report(message, n, total))); }
                catch (InvalidOperationException) { }
                return;
            }
            step.Text = total > 0 ? message + "  (" + n + " of " + total + ")" : message;
            if (total > 0)
            {
                if (bar.Style != ProgressBarStyle.Continuous) bar.Style = ProgressBarStyle.Continuous;
                bar.Maximum = total;
                bar.Value = Math.Min(n, total);
                everPreparing = true;
                note = "Each map is only prepared once. Later starts skip this unless a map file changes.";
                Counted?.Invoke(n, total);
            }
            else
            {
                if (bar.Style != ProgressBarStyle.Marquee) bar.Style = ProgressBarStyle.Marquee;
                if (!everPreparing) note = "";
            }
            UpdateDetail();
            step.Update();
            detail.Update();
        }
    }
}
