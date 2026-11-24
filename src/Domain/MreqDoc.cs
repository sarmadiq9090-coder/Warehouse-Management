using System.Collections.Generic;
using System;

namespace Stores
{
    /// <summary>A material requisition «طلب صرف مواد» (table mreq): a cost centre asks the store for materials;
    /// it is approved (or rejected) before the store issues it, and the issues made from it are linked to it.</summary>
    class MreqDoc
    {
        public int No;                       // mreq_no (0 = new)
        public string MenuNo = "";           // the number shown to the user
        public DateTime Date = DateTime.Today;
        public int Project, Store;
        public string RequestedBy = "", Notes = "", UserName = "", ApprovedBy = "", RejectReason = "", ClosedBy = "";
        public int Status;                   // see Requisitions.Draft … Cancelled
        public DateTime ApprovedAt = DateTime.MinValue;
        public double Value;                 // estimated value when it was sent (qty × last receipt cost)
        public List<MreqLine> Lines = new List<MreqLine>();
    }
}
