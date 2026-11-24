using System.Collections.Generic;
using System;

namespace Stores
{
    /// <summary>A physical stock count session «الجرد الفعلي» of one store (tables count_head / count_menu).
    /// The book balance of every item is frozen when the count is created; posting turns the differences
    /// into a count-shortage issue (sale.doc_kind 2) and a count-surplus receipt (shop.doc_kind 2).</summary>
    class CountDoc
    {
        public int Id;
        public string No = "";
        public DateTime Date = DateTime.Today;
        public int Store;
        public int TypeNo;          // 0 = all item types
        public string Note = "";
        public int Status;          // 0 open, 1 posted (locked)
        public string User = "", PostedBy = "";
        public DateTime Created, PostedAt;
        public int SaleId, ShopId;  // adjustment documents created by posting (0 = none)
        public string SaleNo = "", ShopNo = "";
        public List<CountLine> Lines = new List<CountLine>();
        public List<int> Removed = new List<int>();   // ids of manually added lines removed before saving

        public bool Posted { get { return Status == 1; } }
    }
}
