using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System;

namespace Stores
{
    /// <summary>
    /// Issue          = إخراج مخزني     (table sale, doc_kind 0)  — to a cost centre (project, department…)
    /// PurchaseReturn = مردود مشتريات   (table sale, doc_kind 1)  — back to the supplier
    /// Receipt        = إدخال مخزني     (table shop, type 1 import / 2 local)
    /// Return         = مردود إخراج مخزني (table shop, type 3)   — back from a cost centre
    /// Transfer       = نقل مخزني       (table trans)
    /// Reserve        = حجز مواد لمركز كلفة (table reserve) — no stock movement
    /// </summary>
    enum DocKind { Issue, Receipt, Return, Transfer, PurchaseReturn, Reserve }

    class DocLine
    {
        public int LineId;      // shop_menu / sale_menu auto number (0 = new line)
        public int ItemNo;
        public double Qty;
        public double Price;
        public double Used;     // receipts: quantity already issued from this line (qty_sale)
        public int Store;       // receipts: the line's own store (0 = the document's), see Posting.Parts
    }

    class Doc
    {
        public DocKind Kind;
        public int Id;
        public string No = "";
        public DateTime Date = DateTime.Today;
        public int Person;
        public int Project;
        public int Store;
        public int Store2;      // transfer: destination store
        public int RType = 2;   // receipt type: 1 import, 2 local
        public int Status = 1;  // reservation: 1 active, 0 cancelled
        public string Note = "";
        public List<DocLine> Lines = new List<DocLine>();
        public string Fingerprint;
    }
}
