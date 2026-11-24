using System.Collections.Generic;
using System;

namespace Stores
{
    /// <summary>A write-off «سند إتلاف / شطب مواد» (table wo_head): damaged, expired, lost or obsolete stock taken out of a
    /// store. It moves no stock until it is approved; on approval it is posted as a sale row with doc_kind 3 (see WriteOffs).</summary>
    class WoDoc
    {
        public int No;                       // wo_no (0 = new)
        public string MenuNo = "";           // the number shown to the user
        public DateTime Date = DateTime.Today;
        public int Store;
        public int Reason = WriteOffs.Damaged;
        public string Notes = "", UserName = "", ApprovedBy = "", RejectReason = "";
        public int Status;                   // see WriteOffs.Draft … Cancelled
        public DateTime Created = DateTime.MinValue, ApprovedAt = DateTime.MinValue;
        public int SaleId;                   // the posted sale row (doc_kind 3), 0 before approval
        public string SaleNo = "";
        public double Value;                 // cost at FIFO when it was posted
        public List<WoLine> Lines = new List<WoLine>();
    }
}
