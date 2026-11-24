using System;
using System.Drawing;
using System.Windows.Forms;

namespace Stores
{
    /// <summary>The reports screen in its own window (owner's request): reports can be viewed while an issue or a
    /// receipt stays open in the main window. One window; opening a report again brings it to the front.</summary>
    class ReportsWindow : Form
    {
        static ReportsWindow current;
        ReportsView view;

        ReportsWindow(Form main)
        {
            Text = "التقارير • " + Program.AppName;
            RightToLeft = RightToLeft.Yes; Font = Theme.Font; Icon = Theme.AppIcon; BackColor = Theme.Bg;
            StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = true; KeyPreview = true;
            Rectangle wa = Screen.FromControl(main).WorkingArea;
            Size = new Size(Math.Min(1300, wa.Width - 40), Math.Min(820, wa.Height - 40));
            MinimumSize = new Size(1000, 600);
            Padding = new Padding(12);
            main.FormClosed += delegate { if (!IsDisposed) Close(); };
            FormClosed += delegate { current = null; };
            Lang.Apply(this);   // English first: it may recreate handles the theme then paints
            ThemePainter.Attach(this);
        }

        void SetView(ReportsView v)
        {
            Control old = view;
            v.Dock = DockStyle.Fill;
            Controls.Add(v);
            view = v;
            if (old != null) { Controls.Remove(old); old.Dispose(); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Opens (or brings to the front) the reports window; with a key, that report is selected and shown.</summary>
        public static void Open(Form main, string key, int itemNo, int personNo, int projNo)
        {
            if (!Session.Demand(Session.Reports)) return;
            bool fresh = current == null || current.IsDisposed;
            if (fresh) current = new ReportsWindow(main);
            if (fresh || key != null)
            {
                ReportsView rv = new ReportsView(key);
                current.SetView(rv);
                if (!current.Visible) current.Show();
                rv.Preset(itemNo, personNo, projNo);
            }
            if (!current.Visible) current.Show();
            if (current.WindowState == FormWindowState.Minimized) current.WindowState = FormWindowState.Normal;
            current.Activate();
        }
    }
}
