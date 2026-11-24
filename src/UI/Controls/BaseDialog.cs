using System.Collections.Generic;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Base for dialogs: RTL, brand font, Esc closes.</summary>
    class BaseDialog : Form
    {
        public BaseDialog()
        {
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = false;
            Font = Theme.Font;
            BackColor = Theme.Bg;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            Icon = Theme.AppIcon;
        }

        protected override void OnLoad(EventArgs e)
        {
            Lang.Apply(this);            // English first: turning right-to-left off recreates window handles
            ThemePainter.Attach(this);   // dark theme: the controls the dialog built
            base.OnLoad(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected FlowLayoutPanel ButtonBar(params Button[] buttons)
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.Dock = DockStyle.Bottom;
            f.FlowDirection = FlowDirection.LeftToRight;
            f.Height = 52;
            f.Padding = new Padding(8);
            f.BackColor = Theme.Card;
            foreach (Button b in buttons) f.Controls.Add(b);
            return f;
        }

        /// <summary>Simple two column form layout (label | control).</summary>
        protected TableLayoutPanel FormTable()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.Padding = new Padding(16, 12, 16, 8);
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.AutoScroll = true;
            return t;
        }

        protected void AddRow(TableLayoutPanel t, string label, Control c)
        {
            Label l = UI.Lbl(label);
            l.ForeColor = Theme.Text;
            l.Anchor = AnchorStyles.Right | AnchorStyles.Left;
            l.Margin = new Padding(4, 8, 4, 4);
            int r = t.RowCount;
            t.RowCount = r + 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(l, 0, r);
            if (c != null)
            {
                c.Anchor = AnchorStyles.Right | AnchorStyles.Left;
                t.Controls.Add(c, 1, r);
            }
        }
    }
}
