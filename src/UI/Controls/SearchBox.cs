using System.Drawing.Drawing2D;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Large Windows 11 search field: rounded white box, a search icon, grey hint text, and an accent
    /// line at the bottom while it has the focus.</summary>
    class SearchBox : RoundPanel
    {
        public readonly TextBox Box = new TextBox();
        Label icon = new Label();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public SearchBox(string hint)
        {
            Height = 46; Cursor = Cursors.IBeam; Radius = 8;
            Box.BorderStyle = BorderStyle.None; Box.Font = new Font(Theme.FontName, 12.5f); Box.BackColor = Theme.Card; Box.ForeColor = Theme.Text;
            icon.Text = ""; icon.Font = Theme.IconFont(13f); icon.ForeColor = Theme.Muted; icon.AutoSize = false; icon.TextAlign = ContentAlignment.MiddleCenter;
            icon.BackColor = Theme.Card; icon.Cursor = Cursors.IBeam;
            Controls.Add(Box); Controls.Add(icon);
            Box.HandleCreated += delegate { SendMessage(Box.Handle, 0x1501 /*EM_SETCUEBANNER*/, (IntPtr)1, hint); };
            Box.GotFocus += delegate { BorderColor = Theme.BorderStrong; Invalidate(); };
            Box.LostFocus += delegate { BorderColor = Theme.Border; Invalidate(); };
            icon.Click += delegate { Box.Focus(); };
            Click += delegate { Box.Focus(); };
        }

        // ---- one box, several things to look for: a small list at the end of the box
        Label chooser; string[] names, hints; Action<int> chosen;
        const int ChooserW = 138;
        public int Choice { get; private set; }

        /// <summary>What can be searched: names and the hint of each; changed is called when the user picks another.</summary>
        public void SetChoices(string[] itemNames, string[] itemHints, int index, Action<int> changed)
        {
            names = itemNames; hints = itemHints; chosen = changed;
            chooser = new Label(); chooser.AutoSize = false; chooser.TextAlign = ContentAlignment.MiddleCenter; chooser.Cursor = Cursors.Hand;
            chooser.Font = Theme.FontBold; chooser.ForeColor = Theme.Primary; chooser.BackColor = Theme.Select;
            Controls.Add(chooser);
            ContextMenuStrip menu = new ContextMenuStrip(); menu.RightToLeft = Lang.English ? RightToLeft.No : RightToLeft.Yes; menu.Font = Theme.Font;
            for (int i = 0; i < names.Length; i++) { int n = i; menu.Items.Add(Lang.T(names[i]), null, delegate { Choose(n, true); Box.Focus(); }); }
            chooser.Click += delegate { menu.Show(chooser, new Point(0, chooser.Height)); };
            Disposed += delegate { menu.Dispose(); };
            Choose(Math.Max(0, Math.Min(names.Length - 1, index)), false);
        }

        void Choose(int i, bool tell)
        {
            Choice = i;
            chooser.Text = Lang.T(names[i]) + "  ▾";
            SetHint(hints[i]);
            if (tell && chosen != null) chosen(i);
        }

        void SetHint(string h)
        {
            if (Box.IsHandleCreated) SendMessage(Box.Handle, 0x1501, (IntPtr)1, Lang.T(h));
            else Box.HandleCreated += delegate { SendMessage(Box.Handle, 0x1501, (IntPtr)1, Lang.T(h)); };
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            icon.Bounds = new Rectangle(Width - 44, 4, 36, Height - 8);
            int left = 14;
            if (chooser != null) { chooser.Bounds = new Rectangle(8, 8, ChooserW, Height - 16); left = ChooserW + 16; }
            Box.Bounds = new Rectangle(left, (Height - Box.PreferredHeight) / 2 + 1, Width - left - 48, Box.PreferredHeight);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            if (!Box.Focused) return;
            // Fluent focus: an accent underline inside the rounded box
            Graphics g = e.Graphics;
            using (GraphicsPath clip = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius))
            {
                g.SetClip(clip);
                using (SolidBrush b = new SolidBrush(Theme.Primary)) g.FillRectangle(b, 0, Height - 3, Width, 2);
                g.ResetClip();
            }
        }
    }
}
