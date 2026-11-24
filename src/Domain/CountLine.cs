using System;

namespace Stores
{
    /// <summary>One item of a stock count: frozen book balance and the counted quantity (null = not counted yet).</summary>
    class CountLine
    {
        public int Id;              // count_menu.id (0 = new line, not saved yet)
        public int ItemNo;
        public double Book;
        public double? Counted;
        public bool Added;          // added by hand during the count (was not in the generated list)
        public bool Dirty;          // counted quantity changed since loading / saving

        /// <summary>Counted − book (0 while not counted). Negative = shortage, positive = surplus.</summary>
        public double Diff { get { return Counted.HasValue ? Math.Round(Counted.Value - Book, 6) : 0; } }
    }
}
