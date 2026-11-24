using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System;

namespace Stores
{
    class ComboItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public ComboItem(int id, string name) { Id = id; Name = name; }
        public override string ToString() { return Name; }
    }

    class UserError : Exception
    {
        public UserError(string msg) : base(msg) { }
    }

    interface IView
    {
        bool CanLeave();
    }
}
