using System;

namespace Stores
{
    /// <summary>One line of a write-off (table wo_menu).</summary>
    class WoLine
    {
        public int Id;            // wo_menu.id (0 = new line)
        public int ItemNo;
        public double Qty;
        public int Reason;        // 0 = the document's reason
        public int SaleLine;      // sale_menu.auto_seq once posted
        public double Cost;       // FIFO cost of the line when it was posted
    }
}
