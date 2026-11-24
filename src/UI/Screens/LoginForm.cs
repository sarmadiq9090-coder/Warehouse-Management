using System.Collections.Generic;
using System.Data;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Windows.Forms;
using System;

namespace Stores
{
    // ================================================================== login
    class LoginForm : Form
    {
        ComboBox cmbUser; TextBox txtPass; Label lblErr; int fails;

        public LoginForm()
        {
            Text = Program.AppName + " - تسجيل الدخول";
            RightToLeft = RightToLeft.Yes;
            Font = Theme.Font;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(820, 480);
            BackColor = Theme.Bg;
            Icon = Theme.AppIcon;

            // right: brand panel (flat accent with soft shapes, Windows 11 style)
            Panel brand = new Panel(); brand.Dock = DockStyle.Right; brand.Width = 360; brand.BackColor = Theme.Primary;
            brand.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                Rectangle rc = brand.ClientRectangle;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(18, 255, 255, 255)))
                {
                    g.FillEllipse(b, -120, rc.Height - 220, 360, 360);
                    g.FillEllipse(b, rc.Width - 150, -110, 260, 260);
                }
                using (SolidBrush b = new SolidBrush(Color.FromArgb(40, Theme.Accent))) g.FillEllipse(b, 30, rc.Height - 120, 140, 140);
                Image logo = Theme.Logo;
                int top = 74;
                if (logo != null)
                {
                    int w = Math.Min(rc.Width - 120, logo.Width * 90 / Math.Max(1, logo.Height)); int h = logo.Height * w / Math.Max(1, logo.Width);
                    Rectangle lr = new Rectangle((rc.Width - w) / 2, top, w, h);
                    using (GraphicsPath gp = Theme.Round(Rectangle.Inflate(lr, 14, 14), 10)) using (SolidBrush wb = new SolidBrush(Color.White)) g.FillPath(wb, gp);
                    g.DrawImage(logo, lr);
                    top += h + 44;
                }
                else
                {
                    // no company logo set: the program icon on a white rounded plate
                    Rectangle plate = new Rectangle(rc.Width / 2 - 56, top, 112, 112);
                    using (GraphicsPath gp = Theme.Round(plate, 22)) using (SolidBrush wb = new SolidBrush(Color.White)) g.FillPath(wb, gp);
                    Image img = Theme.ProgramImage;
                    if (img != null) g.DrawImage(img, Rectangle.Inflate(plate, -14, -14));
                    top += 140;
                }
                TextFormatFlags c = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
                using (Font f1 = new Font(Theme.LatinFontName, 19f, FontStyle.Bold))
                using (Font f2 = new Font(Theme.FontName, 13f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, Program.AppName, f1, new Rectangle(20, top, rc.Width - 40, 76), Color.White, c);
                    int h1 = TextRenderer.MeasureText(g, Program.AppName, f1, new Size(rc.Width - 40, 200), c).Height;
                    TextRenderer.DrawText(g, Lang.English ? (Program.Company.Length > 0 ? Program.Company : Program.AppName) : Program.AppNameAr, f2, new Rectangle(20, top + h1 + 4, rc.Width - 40, 30), Color.FromArgb(230, 236, 248), c | TextFormatFlags.RightToLeft);
                    TextRenderer.DrawText(g, Lang.English ? "" : Program.Company, Theme.FontSmall, new Rectangle(20, top + h1 + 38, rc.Width - 40, 40), Color.FromArgb(190, 205, 232), c | TextFormatFlags.RightToLeft);
                }
            };

            // left: the sign-in card
            Panel form = new Panel(); form.Dock = DockStyle.Fill; form.BackColor = Theme.Bg;
            Panel card = UI.Card(); card.Bounds = new Rectangle(24, 34, 412, 412);
            Label title = new Label(); title.Text = "تسجيل الدخول"; title.Font = new Font(Theme.FontName, 18f, FontStyle.Bold); title.ForeColor = Theme.Text;
            title.AutoSize = false; title.Bounds = new Rectangle(36, 26, 340, 40); title.TextAlign = ContentAlignment.MiddleLeft;
            Label sub = Line("أدخل اسم المستخدم وكلمة المرور للمتابعة", 66, Theme.Muted); sub.Font = Theme.FontSmall;
            Label l1 = Line("اسم المستخدم", 104, Theme.Text);
            cmbUser = new ComboBox(); cmbUser.Font = new Font(Theme.FontName, 12f); cmbUser.Location = new Point(36, 128); cmbUser.Width = 340;
            cmbUser.DropDownStyle = ComboBoxStyle.DropDown;
            Label l2 = Line("كلمة المرور", 172, Theme.Text);
            txtPass = new TextBox(); txtPass.Font = new Font(Theme.FontName, 12f); txtPass.Location = new Point(36, 196); txtPass.Width = 340; txtPass.UseSystemPasswordChar = true;
            Button ok = UI.Btn("دخول", Theme.Primary, 340); ok.Location = new Point(36, 248); ok.Height = 42; ok.Font = new Font(Theme.FontName, 12f, FontStyle.Bold);
            lblErr = Line("", 296, Theme.Danger);
            // the network / server setup buttons live in the connection window (beginners clicked them here by mistake)
            LinkLabel conn = new LinkLabel(); conn.Text = "إعدادات الاتصال والسيرفر"; conn.AutoSize = false; conn.Bounds = new Rectangle(36, 330, 340, 24);
            conn.TextAlign = ContentAlignment.MiddleLeft; conn.Font = Theme.FontBold; conn.LinkColor = Theme.Primary; conn.ActiveLinkColor = Theme.PrimaryDark;
            conn.LinkBehavior = LinkBehavior.HoverUnderline; conn.TabStop = false;
            Label where = Line(Db.Describe, 358, Theme.Muted); where.Font = Theme.FontSmall; where.Height = 36; where.AutoEllipsis = true; where.TextAlign = ContentAlignment.TopLeft;
            card.Controls.AddRange(new Control[] { title, sub, l1, cmbUser, l2, txtPass, ok, lblErr, conn, where });
            LinkLabel lang = LangLink(); card.Controls.Add(lang); lang.BringToFront();   // above the title's empty part
            form.Controls.Add(card);

            Controls.Add(form); Controls.Add(brand);
            AcceptButton = ok;

            LoadUsers();
            ok.Click += delegate { DoLogin(); };
            conn.LinkClicked += delegate
            {
                using (ConnectionForm f = new ConnectionForm())
                {
                    if (f.ShowDialog(this) == DialogResult.OK) { Program.PrepareData(); Invalidate(true); where.Text = Db.Describe; LoadUsers(); txtPass.Focus(); }
                }
            };
            Shown += delegate { if (cmbUser.Text.Length > 0) txtPass.Focus(); else cmbUser.Focus(); };
        }

        protected override void OnLoad(EventArgs e) { base.OnLoad(e); Lang.Apply(this); }

        /// <summary>«English» / «عربي» at the top end of the card: the login screen reopens in that language
        /// (this computer's default, and the user's own choice after signing in).</summary>
        LinkLabel LangLink()
        {
            LinkLabel l = new LinkLabel(); l.Text = Lang.English ? "عربي" : "English";
            l.AutoSize = false; l.Bounds = new Rectangle(Lang.English ? 412 - 16 - 90 : 16, 6, 90, 20);
            l.TextAlign = ContentAlignment.MiddleRight;   // the card's end side in both languages
            l.Font = Lang.English ? Theme.Font : new Font(Theme.LatinFontName, Theme.Font.Size);
            l.LinkColor = Theme.Primary; l.ActiveLinkColor = Theme.PrimaryDark; l.LinkBehavior = LinkBehavior.HoverUnderline; l.TabStop = false;
            Lang.Keep(l);
            l.LinkClicked += delegate
            {
                Lang.English = !Lang.English;
                Lang.SetDefault(Lang.English);
                Lang.ChosenAtLogin = true;
                Settings.Set("LastUser", cmbUser.Text.Trim());
                DialogResult = DialogResult.Retry;
                Close();
            };
            return l;
        }

        static Label Line(string text, int y, Color color)
        {
            Label l = new Label(); l.Text = text; l.AutoSize = false; l.Bounds = new Rectangle(36, y, 340, 22); l.ForeColor = color; l.Font = Theme.Font;
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        void LoadUsers()
        {
            cmbUser.Items.Clear();
            try { foreach (DataRow r in Db.Query("SELECT user_name FROM pass ORDER BY pass_no").Rows) cmbUser.Items.Add(Db.S(r[0])); } catch { }
            string last = Settings.Get("LastUser", "");
            cmbUser.Text = last;
        }

        /// <summary>app_settings key (+ user number): "1" = this user must change the password at the next sign-in.</summary>
        public const string MustChangeKey = "must_change_password_user";

        void DoLogin()
        {
            string u = cmbUser.Text.Trim();
            if (u.Length == 0) { lblErr.Text = "أدخل اسم المستخدم"; return; }
            DataTable t = Db.Query("SELECT pass_no, user_name, pass FROM pass WHERE user_name = ?", u);
            if (t.Rows.Count == 0 || Db.S(t.Rows[0]["pass"]) != txtPass.Text)
            {
                fails++;
                lblErr.Text = "اسم المستخدم أو كلمة المرور غير صحيحة";
                txtPass.SelectAll(); txtPass.Focus();
                if (fails >= 5) { UI.Warn("تم تجاوز عدد المحاولات."); DialogResult = DialogResult.Cancel; Close(); }
                return;
            }
            Session.UserNo = Db.I(t.Rows[0]["pass_no"]);
            Session.UserName = Db.S(t.Rows[0]["user_name"]);
            // new data starts with user 1 / password 1: that known password must be replaced before the first use
            if (Db.GetSetting(MustChangeKey + Session.UserNo, "") == "1")
            {
                UI.Info("هذه أول مرة تدخل فيها إلى البيانات الجديدة.\nكلمة المرور الحالية «" + txtPass.Text + "» معروفة للجميع، لذلك يجب تغييرها الآن.");
                using (ChangePasswordForm f = new ChangePasswordForm(txtPass.Text))
                    if (f.ShowDialog(this) != DialogResult.OK) { Session.UserNo = 0; Session.UserName = null; txtPass.Clear(); txtPass.Focus(); return; }
                Db.SetSetting(MustChangeKey + Session.UserNo, "");
            }
            Session.LoadPerms(); Session.LoadMode(); Theme.LoadUser();
            Settings.Set("LastUser", Session.UserName); Settings.Save();
            Db.Log(null, "تسجيل دخول", Environment.MachineName);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
