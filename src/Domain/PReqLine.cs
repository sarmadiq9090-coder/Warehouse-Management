using System;

namespace Stores
{
    /// <summary>One line of a purchase request (table preq_menu).</summary>
    class PReqLine
    {
        public int LineId;          // preq_menu.id (0 = new)
        public int ItemNo;
        public double Qty;          // ordered
        public double Received;     // from the linked receipts (shop.preq_no)
        public double Remaining { get { return Math.Max(0, Qty - Received); } }
    }
}
