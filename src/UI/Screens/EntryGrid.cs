using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Grid where Enter moves to the next useful cell instead of the next row.</summary>
    class EntryGrid : DataGridView
    {
        public event EventHandler EnterPressed;

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Enter && (keyData & (Keys.Shift | Keys.Control | Keys.Alt)) == 0)
            {
                if (EnterPressed != null) EnterPressed(this, EventArgs.Empty);
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override bool ProcessDataGridViewKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift && !e.Control && !e.Alt)
            {
                if (EnterPressed != null) EnterPressed(this, EventArgs.Empty);
                return true;
            }
            return base.ProcessDataGridViewKey(e);
        }
    }
}
