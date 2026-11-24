using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System;

namespace Stores
{
    /// <summary>Progress window of the server setup. Shows: what is happening, a progress bar, elapsed time, the expected
    /// remaining time and a "do not close" note; a cancel button only while something that can be stopped is running
    /// (check, download, copy). Two ways to fill it: Step = the expected duration of a step and of everything after it (the bar
    /// and the remaining time are computed from these and from the real elapsed time, the bar never goes backwards);
    /// Progress = a known share. Not modal (the caller pumps messages), but its owner is disabled while it is open.</summary>
    class WaitForm : Form
    {
        [DllImport("user32.dll")] static extern bool EnableWindow(IntPtr h, bool enable);

        const int W = 560;
        Label lblTitle, lblStep, lblTime, lblNote;
        ProgressBar pb;
        Button bCancel;
        System.Windows.Forms.Timer tmr;
        DateTime start = DateTime.Now, stepStart = DateTime.Now;
        string step = "", detail;
        int stepSec, afterSec, shown;
        double share = -1, left = -1;   // Progress: known share 0..1, seconds left (-1 = not known)
        bool closing;
        IntPtr owner;

        /// <summary>The user pressed the cancel button.</summary>
        public bool CancelPressed { get; private set; }

        /// <summary>cancel: text of the cancel button (null = the work cannot be stopped).</summary>
        public WaitForm(string title, string note, string cancel)
        {
            Text = Program.AppName; RightToLeft = RightToLeft.Yes; Font = Theme.Font; Icon = Theme.AppIcon; BackColor = Theme.Card;
            FormBorderStyle = FormBorderStyle.FixedDialog; ControlBox = false; StartPosition = FormStartPosition.CenterScreen;
            int extra = Lang.English ? 20 : 0;   // the English step texts take one line more
            ClientSize = new Size(W, extra + (cancel == null ? 218 : 262)); TopMost = true; ShowInTaskbar = true;
            Panel band = new Panel(); band.Bounds = new Rectangle(0, 0, W, 5); band.BackColor = Theme.Accent; Controls.Add(band);
            lblTitle = L(new Rectangle(16, 14, W - 32, 30), Theme.FontBold, Theme.Primary); lblTitle.Text = title;
            lblStep = L(new Rectangle(16, 46, W - 32, 46 + extra), Theme.Font, Theme.Text);
            pb = new ProgressBar(); pb.Bounds = new Rectangle(16, 98 + extra, W - 32, 24); pb.Maximum = 1000;
            pb.Style = ProgressBarStyle.Marquee; pb.MarqueeAnimationSpeed = 30; Controls.Add(pb);
            lblTime = L(new Rectangle(16, 128 + extra, W - 32, 26), Theme.FontBold, Theme.Primary);
            lblNote = L(new Rectangle(16, 160 + extra, W - 32, 48), Theme.Font, Theme.IsDark ? Theme.Warning : Color.FromArgb(176, 64, 0)); lblNote.Text = note;
            if (cancel != null)
            {
                bCancel = UI.BtnLight(cancel, 170);
                bCancel.Location = new Point((W - bCancel.Width) / 2, 216 + extra);
                bCancel.Click += delegate { CancelPressed = true; bCancel.Enabled = false; };
                Controls.Add(bCancel);
            }
            tmr = new System.Windows.Forms.Timer(); tmr.Interval = 500; tmr.Tick += delegate { Draw(); }; tmr.Start();
            FormClosed += delegate { tmr.Stop(); tmr.Dispose(); };
            // Alt+F4 must not close it while the work goes on
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (!closing && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
            Draw();
        }

        Label L(Rectangle r, Font f, Color c)
        {
            Label l = new Label(); l.Bounds = r; l.Font = f; l.ForeColor = c; l.TextAlign = ContentAlignment.MiddleCenter; l.UseMnemonic = false; Controls.Add(l); return l;
        }

        protected override void OnLoad(EventArgs e)
        {
            Lang.Apply(this);
            ThemePainter.Attach(this);
            // a note of three lines (English): the window grows
            int more = TextRenderer.MeasureText(lblNote.Text, lblNote.Font, new Size(lblNote.Width - 8, 0), TextFormatFlags.WordBreak).Height - lblNote.Height;
            if (more > 0)
            {
                lblNote.Height += more;
                if (bCancel != null) bCancel.Top += more;
                ClientSize = new Size(ClientSize.Width, ClientSize.Height + more);
            }
            base.OnLoad(e);
        }

        /// <summary>Opens the window over its owner; the owner does not take clicks until this window is closed.</summary>
        public void ShowOver(IWin32Window o)
        {
            if (o != null) { owner = o.Handle; EnableWindow(owner, false); }
            Show(o);
        }

        void FreeOwner() { if (owner != IntPtr.Zero) { EnableWindow(owner, true); owner = IntPtr.Zero; } }

        public new void Close()
        {
            closing = true;
            FreeOwner();   // before closing: Windows then activates the owner
            base.Close();
        }

        protected override void Dispose(bool disposing) { FreeOwner(); base.Dispose(disposing); }

        /// <summary>New step: expected seconds for it and for everything after it.</summary>
        public void Step(string text, int seconds, int secondsAfter)
        {
            if (IsDisposed) return;
            if (share >= 0 || text != step || seconds != stepSec || secondsAfter != afterSec)
            { step = text; detail = null; stepSec = seconds; afterSec = secondsAfter; stepStart = DateTime.Now; share = -1; }
            Draw(); Application.DoEvents();
        }

        /// <summary>Changes the text under the current step without restarting its timing.</summary>
        public void Detail(string text) { if (IsDisposed) return; detail = text; Draw(); Application.DoEvents(); }

        /// <summary>A known share (0..1) of the work and the seconds left (-1 = not known).</summary>
        public void Progress(string text, double done, double secondsLeft)
        {
            if (IsDisposed) return;
            step = text; share = Math.Max(0, Math.Min(1, done)); left = secondsLeft;
            Draw();
        }

        /// <summary>Progress of the package download (SqlPackage.Download): percent, MB, speed, remaining time.</summary>
        public void Download(long done, long total, double speed, string error, int failures, int maxFailures)
        {
            string text = "تم تنزيل " + SqlPackage.Mb(done) + " من أصل " + SqlPackage.Mb(total) + " ميغابايت";
            if (done >= total) text += "\nجاري فحص الملف…";
            else if (error != null)
                text += "\n" + error + "، تعاد المحاولة تلقائيا" + (failures > 1 ? " (" + failures + "/" + maxFailures + ")" : "") + "…";
            else if (speed >= 1048576) text += "\nالسرعة: " + (speed / 1048576).ToString("0.0", CultureInfo.InvariantCulture) + " ميغابايت في الثانية";
            else if (speed > 0) text += "\nالسرعة: " + (speed / 1024).ToString("0", CultureInfo.InvariantCulture) + " كيلوبايت في الثانية";
            else text += "\nجاري الاتصال بموقع مايكروسوفت…";
            Progress(text, (double)done / total, error == null && speed > 0 && done < total ? (total - done) / speed : -1);
        }

        void Draw()
        {
            double el = (DateTime.Now - start).TotalSeconds;
            if (share >= 0)
            {
                int v = (int)(1000 * share);
                SetTexts(step, Percent(v / 10) + "     •     الوقت المنقضي: " + Clock(el) + (left >= 0 ? "     •     المتبقي المتوقع: " + Remaining(left) : ""));
                if (pb.Style != ProgressBarStyle.Continuous) pb.Style = ProgressBarStyle.Continuous;
                pb.Value = v;
                return;
            }
            double inStep = (DateTime.Now - stepStart).TotalSeconds;
            bool slow = stepSec > 0 && inStep > stepSec;
            string text = (detail ?? step) + (slow ? "\nهذه الخطوة أبطأ من المعتاد على هذا الحاسوب، وهذا طبيعي. يرجى الانتظار." : "");
            if (stepSec <= 0 && afterSec <= 0)
            {
                SetTexts(text, "الوقت المنقضي: " + Clock(el));
                if (pb.Style != ProgressBarStyle.Marquee) pb.Style = ProgressBarStyle.Marquee;
                return;
            }
            double rest = Math.Max(stepSec - inStep, Math.Min(stepSec, Math.Max(10, stepSec * 0.15)));
            double remaining = rest + afterSec;
            int target = (int)(1000 * el / (el + remaining));
            if (target > 990) target = 990;
            if (target > shown) shown = target;
            SetTexts(text, Percent(shown / 10) + "     •     الوقت المنقضي: " + Clock(el) + "     •     المتبقي المتوقع: " + Remaining(remaining));
            if (pb.Style != ProgressBarStyle.Continuous) pb.Style = ProgressBarStyle.Continuous;
            pb.Value = shown;
        }

        void SetTexts(string text, string time)
        {
            text = Lang.T(text); time = Lang.T(time);   // (compared with what is shown: no flicker)
            if (lblStep.Text != text) lblStep.Text = text;
            if (lblTime.Text != time) lblTime.Text = time;
        }

        static string Percent(int n) { return n + (Lang.English ? "%" : "٪"); }
        static string Clock(double s) { int t = (int)s; return (t / 60) + ":" + (t % 60).ToString("00"); }
        static string Remaining(double s)
        {
            if (s < 60) return "أقل من دقيقة";
            int n = (int)Math.Ceiling(s / 60);
            return "نحو " + (n == 1 ? "دقيقة" : n == 2 ? "دقيقتين" : n <= 10 ? n + " دقائق" : n + " دقيقة");
        }
    }
}
