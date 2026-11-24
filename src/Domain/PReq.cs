using System.Collections.Generic;
using System;

namespace Stores
{
    /// <summary>A purchase request «طلب شراء» (table preq). Not a stock movement: nothing is posted, balances never change.
    /// Status: 0 مسودة، 1 مُرسل، 2 مستلم جزئيا، 3 مكتمل، 9 ملغى (2 / 3 follow the linked receipts, see PurchaseRequests).</summary>
    class PReq
    {
        public int Id;
        public string No = "";
        public DateTime Date = DateTime.Today;
        public int Store;           // 0 = not tied to one store
        public int Status;
        public int Supplier;        // person (optional)
        public string Note = "";
        public string User = "";
        public List<PReqLine> Lines = new List<PReqLine>();

        public bool Open { get { return Status == 0 || Status == 1 || Status == 2; } }
    }
}
