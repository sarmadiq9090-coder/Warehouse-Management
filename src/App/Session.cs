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
    // ------------------------------------------------------------------ session / permissions
    static class Session
    {
        public static int UserNo;
        public static string UserName;
        static Dictionary<int, bool> perms = new Dictionary<int, bool>();

        // command numbers kept from the original program (table disply_command)
        public const int RecView = 11, RecEdit = 12, RecDelete = 13, RecAdd = 14;
        public const int IssView = 21, IssEdit = 22, IssDelete = 23, IssAdd = 24;
        public const int Transfer = 61, Setup = 71, Persons = 74, Items = 75, Reports = 81;

        public static bool IsAdmin { get { return UserNo == 1; } }

        /// <summary>Advanced mode = every screen + fast keyboard entry. Beginner mode = guided, simple screens.</summary>
        public static bool Advanced;
        public static void LoadMode() { Advanced = Db.GetSetting("mode_user" + UserNo, "beginner") == "advanced"; }
        public static void SetMode(bool advanced) { Advanced = advanced; Db.SetSetting("mode_user" + UserNo, advanced ? "advanced" : "beginner"); }


        public static void LoadPerms()
        {
            perms.Clear();
            if (UserNo < 1 || UserNo > 5) return;
            try
            {
                foreach (System.Data.DataRow r in Db.Query("SELECT command_no, user" + UserNo + " FROM disply_command").Rows)
                    perms[Db.I(r[0])] = Db.B(r[1]);
            }
            catch { }
        }

        public static bool Can(int cmd)
        {
            if (IsAdmin) return true;
            bool v;
            return perms.TryGetValue(cmd, out v) && v;
        }

        public static bool Demand(int cmd)
        {
            if (Can(cmd)) return true;
            UI.Warn("ليس لديك صلاحية لهذه العملية.");
            return false;
        }
    }
}
