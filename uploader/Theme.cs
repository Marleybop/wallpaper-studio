using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WallpaperStudioUploader;

/// Colours and fonts. Dark, with the same green as the Wallpaper Studio plugin.
static class Theme
{
    public static readonly Color Bg = Hex(0x131416);
    public static readonly Color Panel = Hex(0x1c1e21);
    public static readonly Color Raised = Hex(0x26292d);
    public static readonly Color Hover = Hex(0x2e3237);
    public static readonly Color Input = Hex(0x101113);
    public static readonly Color Border = Hex(0x2f3338);
    public static readonly Color BorderHi = Hex(0x4a4f56);
    public static readonly Color Text = Hex(0xebe9e6);
    public static readonly Color Muted = Hex(0x9aa0a6);
    public static readonly Color Faint = Hex(0x666b72);
    public static readonly Color Accent = Hex(0x8cc63f);
    public static readonly Color AccentHover = Hex(0xa2d95a);
    public static readonly Color AccentDim = Hex(0x2a3a18);
    public static readonly Color AccentText = Hex(0x11160a);
    public static readonly Color Danger = Hex(0xe0614a);
    public static readonly Color Warn = Hex(0xe0b04a);

    public static readonly Font Base = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.75f);
    public static readonly Font Semi = new("Segoe UI Semibold", 9.75f);
    public static readonly Font CardTitle = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Header = new("Segoe UI Semibold", 15f);
    public static readonly Font Mono = new("Consolas", 9.5f);
    public static readonly Font MonoBig = new("Consolas", 12f, FontStyle.Bold);

    public static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    public static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
    {
        using var p = Round(r, rad); using var b = new SolidBrush(c); g.FillPath(b, p);
    }

    public static void StrokeRound(Graphics g, Color c, RectangleF r, float rad, float w = 1f)
    {
        using var p = Round(r, rad); using var pen = new Pen(c, w); g.DrawPath(pen, p);
    }

    public static void Hq(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    // ---- Windows dark mode bits ----
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);

    public static void DarkTitleBar(Form f)
    {
        int on = 1;
        DwmSetWindowAttribute(f.Handle, 20, ref on, 4);                 // immersive dark mode
        int caption = Bg.R | (Bg.G << 8) | (Bg.B << 16);
        DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);            // caption colour (Windows 11)
    }

    public static void DarkScrollbars(Control c)
    {
        if (c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
        else c.HandleCreated += (_, _) => SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
    }

    /// The app icon, drawn rather than shipped: a green rounded tile with a brick pattern.
    public static Icon MakeIcon()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            Hq(g);
            FillRound(g, Accent, new RectangleF(2, 2, 60, 60), 14);
            Glyphs.Bricks(g, new RectangleF(14, 16, 36, 32), AccentText, 2.6f);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}

/// Small vector icons drawn with GDI+.
static class Glyphs
{
    public static void Bricks(Graphics g, RectangleF r, Color c, float w = 1.6f)
    {
        using var pen = new Pen(c, w) { LineJoin = LineJoin.Round };
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        float row = r.Height / 3;
        for (int i = 1; i < 3; i++) g.DrawLine(pen, r.X, r.Y + row * i, r.Right, r.Y + row * i);
        for (int i = 0; i < 3; i++)
        {
            float off = i % 2 == 0 ? r.Width / 2 : r.Width / 4;
            for (float x = r.X + off; x < r.Right - 1; x += r.Width / 2)
                g.DrawLine(pen, x, r.Y + row * i, x, r.Y + row * (i + 1));
        }
    }

    public static void Floor(Graphics g, RectangleF r, Color c, float w = 1.6f) => Plane(g, r, c, w, false);
    public static void Ceiling(Graphics g, RectangleF r, Color c, float w = 1.6f) => Plane(g, r, c, w, true);

    static void Plane(Graphics g, RectangleF r, Color c, float w, bool flip)
    {
        // a tiled plane in perspective
        float inset = r.Width * 0.24f;
        PointF Near(float t) => new(r.X + r.Width * t, flip ? r.Y : r.Bottom);
        PointF Far(float t) => new(r.X + inset + (r.Width - 2 * inset) * t, flip ? r.Bottom : r.Y);
        using var pen = new Pen(c, w) { LineJoin = LineJoin.Round };
        g.DrawPolygon(pen, new[] { Near(0), Near(1), Far(1), Far(0) });
        for (int i = 1; i < 3; i++) g.DrawLine(pen, Near(i / 3f), Far(i / 3f));
        for (int i = 1; i < 3; i++)
        {
            float t = i / 3f; t = t * t * 0.4f + t * 0.6f;
            float y = flip ? r.Bottom - (r.Height * (1 - t)) : r.Y + r.Height * t;
            float half = (r.Width - 2 * inset * (1 - t)) / 2;
            g.DrawLine(pen, r.X + r.Width / 2 - half, y, r.X + r.Width / 2 + half, y);
        }
    }

    public static void Spray(Graphics g, RectangleF r, Color c, float w = 1.6f)
    {
        using var pen = new Pen(c, w) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        var body = new RectangleF(r.X + r.Width * 0.18f, r.Y + r.Height * 0.38f, r.Width * 0.36f, r.Height * 0.62f);
        using (var p = Theme.Round(body, 3)) g.DrawPath(pen, p);
        g.DrawRectangle(pen, body.X + body.Width * 0.25f, body.Y - r.Height * 0.12f, body.Width * 0.5f, r.Height * 0.12f);
        g.DrawRectangle(pen, body.X + body.Width * 0.35f, body.Y - r.Height * 0.24f, body.Width * 0.3f, r.Height * 0.12f);
        // spray mist
        using var dot = new SolidBrush(c);
        float sx = body.Right + r.Width * 0.06f, sy = body.Y - r.Height * 0.18f;
        var pts = new[] { (0.10f, -0.10f), (0.22f, -0.20f), (0.20f, 0.02f), (0.34f, -0.08f), (0.32f, 0.12f), (0.42f, -0.24f), (0.40f, 0.02f) };
        foreach (var (dx, dy) in pts) g.FillEllipse(dot, sx + dx * r.Width - w, sy + dy * r.Height - w, w * 2, w * 2);
    }

    public static void Upload(Graphics g, RectangleF r, Color c, float w = 2f)
    {
        using var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float cx = r.X + r.Width / 2;
        g.DrawLine(pen, cx, r.Y + r.Height * 0.12f, cx, r.Y + r.Height * 0.68f);
        g.DrawLines(pen, new[] { new PointF(cx - r.Width * 0.24f, r.Y + r.Height * 0.34f), new PointF(cx, r.Y + r.Height * 0.10f), new PointF(cx + r.Width * 0.24f, r.Y + r.Height * 0.34f) });
        g.DrawLines(pen, new[] { new PointF(r.X + r.Width * 0.08f, r.Y + r.Height * 0.66f), new PointF(r.X + r.Width * 0.08f, r.Bottom - r.Height * 0.06f), new PointF(r.Right - r.Width * 0.08f, r.Bottom - r.Height * 0.06f), new PointF(r.Right - r.Width * 0.08f, r.Y + r.Height * 0.66f) });
    }

    public static void Check(Graphics g, RectangleF r, Color c, float w = 2f)
    {
        using var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[] { new PointF(r.X + r.Width * 0.18f, r.Y + r.Height * 0.52f), new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.76f), new PointF(r.X + r.Width * 0.84f, r.Y + r.Height * 0.26f) });
    }

    public static void Chevron(Graphics g, RectangleF r, Color c, bool down, float w = 1.6f)
    {
        using var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = Math.Min(r.Width, r.Height) * 0.28f;
        if (down) g.DrawLines(pen, new[] { new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2) });
        else g.DrawLines(pen, new[] { new PointF(cx - s / 2, cy - s), new PointF(cx + s / 2, cy), new PointF(cx - s / 2, cy + s) });
    }
}
