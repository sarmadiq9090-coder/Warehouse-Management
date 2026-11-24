using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Draws the program icon (warehouse shelf with boxes, brand blue + green) and writes a multi-size .ico.
// Usage: MakeIcon.exe <out.ico> [preview.png]
static class MakeIcon
{
    static readonly Color Blue = Color.FromArgb(47, 86, 163), BlueDark = Color.FromArgb(32, 62, 124), Green = Color.FromArgb(1, 165, 82);
    static readonly Color Box1 = Color.FromArgb(255, 255, 255), Box2 = Color.FromArgb(214, 228, 248), Tape = Color.FromArgb(1, 165, 82);

    static GraphicsPath Round(RectangleF r, float rad)
    {
        GraphicsPath p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    static void Box(Graphics g, float x, float y, float w, float h, Color fill, float s)
    {
        using (SolidBrush b = new SolidBrush(fill)) g.FillRectangle(b, x, y, w, h);
        if (s >= 0.18f)   // tape across the lid on bigger sizes
            using (SolidBrush t = new SolidBrush(Tape)) g.FillRectangle(t, x + w * 0.42f, y, w * 0.16f, h * 0.38f);
        using (Pen p = new Pen(BlueDark, Math.Max(1f, 2.2f * s))) g.DrawRectangle(p, x, y, w, h);
    }

    static Bitmap Draw(int n)
    {
        Bitmap bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        float s = n / 256f;
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            RectangleF bg = new RectangleF(4 * s, 4 * s, 248 * s, 248 * s);
            using (GraphicsPath p = Round(bg, Math.Max(2f, 44 * s)))
            using (LinearGradientBrush lb = new LinearGradientBrush(bg, Color.FromArgb(58, 104, 190), Blue, 90f))
                g.FillPath(lb, p);
            // green floor band
            using (GraphicsPath p = Round(new RectangleF(4 * s, 196 * s, 248 * s, 56 * s), Math.Max(2f, 44 * s)))
            using (Region clip = new Region(Round(bg, Math.Max(2f, 44 * s))))
            {
                g.SetClip(clip, CombineMode.Replace);
                using (SolidBrush gb = new SolidBrush(Green)) g.FillRectangle(gb, 4 * s, 204 * s, 248 * s, 48 * s);
                g.ResetClip();
            }
            // shelf
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(235, 242, 252)))
            {
                g.FillRectangle(sh, 34 * s, 176 * s, 188 * s, 12 * s);   // bottom shelf
                g.FillRectangle(sh, 34 * s, 104 * s, 188 * s, 9 * s);    // middle shelf
                g.FillRectangle(sh, 34 * s, 40 * s, 12 * s, 148 * s);    // posts
                g.FillRectangle(sh, 210 * s, 40 * s, 12 * s, 148 * s);
            }
            // boxes
            Box(g, 56 * s, 122 * s, 64 * s, 54 * s, Box1, s);
            Box(g, 128 * s, 132 * s, 70 * s, 44 * s, Box2, s);
            Box(g, 66 * s, 58 * s, 56 * s, 46 * s, Box2, s);
            Box(g, 132 * s, 52 * s, 62 * s, 52 * s, Box1, s);
        }
        return bmp;
    }

    static void Main(string[] args)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        List<byte[]> pngs = new List<byte[]>();
        foreach (int n in sizes)
            using (Bitmap b = Draw(n)) using (MemoryStream ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
        using (FileStream f = File.Create(args[0]))
        using (BinaryWriter w = new BinaryWriter(f))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset); offset += pngs[i].Length;
            }
            foreach (byte[] p in pngs) w.Write(p);
        }
        if (args.Length > 1)
        {
            // preview: all sizes side by side on white and on dark
            using (Bitmap pv = new Bitmap(760, 560))
            using (Graphics g = Graphics.FromImage(pv))
            {
                g.Clear(Color.White);
                using (SolidBrush dk = new SolidBrush(Color.FromArgb(32, 32, 32))) g.FillRectangle(dk, 0, 280, 760, 280);
                using (Bitmap big = Draw(256)) { g.DrawImage(big, 10, 12); g.DrawImage(big, 10, 292); }
                int x = 290;
                foreach (int n in new[] { 16, 24, 32, 48, 64, 128 })
                    using (Bitmap b = Draw(n)) { g.DrawImageUnscaled(b, x, 140 - n / 2); g.DrawImageUnscaled(b, x, 420 - n / 2); x += n + 22; }
                pv.Save(args[1], ImageFormat.Png);
            }
        }
    }
}
