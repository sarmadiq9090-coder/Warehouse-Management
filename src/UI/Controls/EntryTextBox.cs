using System.Runtime.InteropServices;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>
    /// Text box of the item entry bar: Enter, Esc and the arrow keys reach KeyDown (they are not swallowed by the form),
    /// optional grey placeholder text («cue banner»), and test hooks to type and press keys like a user.
    /// </summary>
    class EntryTextBox : TextBox
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        string cue;

        public string Cue
        {
            get { return cue; }
            set { cue = value; if (IsHandleCreated) ApplyCue(); }
        }

        void ApplyCue() { if (cue != null) SendMessage(Handle, 0x1501, (IntPtr)1, Lang.T(cue)); }   // EM_SETCUEBANNER, shown also while focused

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyCue(); }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Enter: case Keys.Escape: case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown:
                    if ((keyData & (Keys.Control | Keys.Alt)) == 0) return true;
                    break;
            }
            return base.IsInputKey(keyData);
        }

        /// <summary>Test hook: types the text one character at a time (KeyPress + text change, like the keyboard or a scanner).</summary>
        public void TypeText(string s, int msPerKey)
        {
            foreach (char c in s)
            {
                KeyPressEventArgs e = new KeyPressEventArgs(c);
                OnKeyPress(e);
                if (!e.Handled) { SelectedText = c.ToString(); }
                if (msPerKey > 0) { Application.DoEvents(); System.Threading.Thread.Sleep(msPerKey); }
            }
        }

        /// <summary>Test hook: presses a key (KeyDown).</summary>
        public void PressKey(Keys k) { OnKeyDown(new KeyEventArgs(k)); }
    }
}
