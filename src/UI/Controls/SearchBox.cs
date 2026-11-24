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

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            icon.Bounds = new Rectangle(Width - 44, 4, 36, Height - 8);
            Box.Bounds = new Rectangle(14, (Height - Box.PreferredHeight) / 2 + 1, Width - 14 - 48, Box.PreferredHeight);
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
