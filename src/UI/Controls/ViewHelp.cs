using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System;

namespace Stores
{
    static class ViewHelp
    {
        public static Label Title(string text)
        {
            // Windows 11 page title: large semibold, neutral colour
            Label t = new Label(); t.Text = text; t.Font = Theme.FontTitle; t.ForeColor = Theme.Text; t.Dock = DockStyle.Top; t.Height = 42;
            t.TextAlign = ContentAlignment.MiddleLeft;
            return t;
        }
    }
}
