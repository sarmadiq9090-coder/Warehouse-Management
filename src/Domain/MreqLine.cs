using System;

namespace Stores
{
    /// <summary>One line of a material requisition (table mreq_menu).</summary>
    class MreqLine
    {
        public int Id;            // mreq_menu.id (0 = new line)
        public int ItemNo;
        public double Qty;        // requested
        public double Issued;     // issued so far (from the issues linked to the request)
        public double Remaining { get { return Math.Max(0, Qty - Issued); } }
    }
}
