using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WallpaperStudioUploader;

/// One set of colours. Values come from the Marleybop brand kit (tokens/marleybop.tokens.json).
public sealed class Palette
{
    public bool Dark;
    public Color Bg, Surface, Panel, Border, BorderStrong, Text, Text2, Muted, Link, Ok, Error, SelBg, SelText, Hover;
    public static readonly Color Rust = Theme.Hex(0xC8452A), RustDark = Theme.Hex(0xA33620), RustLight = Theme.Hex(0xE2683F);
    public static readonly Color Ink = Theme.Hex(0x16181B), Paper = Theme.Hex(0xF6F5F2), Graphite = Theme.Hex(0x3A3E44);

    public static readonly Palette Light = new()
    {
        Dark = false, Bg = Theme.Hex(0xF6F5F2), Surface = Color.White, Panel = Theme.Hex(0xE6E4DF), Border = Theme.Hex(0xE0DDD7),
        BorderStrong = Theme.Hex(0xD6D3CC), Text = Theme.Hex(0x16181B), Text2 = Theme.Hex(0x3A3E44), Muted = Theme.Hex(0x5A6068),
        Link = Theme.Hex(0xA33620), Ok = Theme.Hex(0x3F7530), Error = Theme.Hex(0xB3261E), SelBg = Theme.Hex(0x16181B),
        SelText = Theme.Hex(0xF6F5F2), Hover = Theme.Hex(0xEDEBE6),
    };
    public static readonly Palette DarkP = new()
    {
        Dark = true, Bg = Theme.Hex(0x111214), Surface = Theme.Hex(0x1C1E21), Panel = Theme.Hex(0x26292D), Border = Theme.Hex(0x2B2E33),
        BorderStrong = Theme.Hex(0x3A3E44), Text = Theme.Hex(0xF6F5F2), Text2 = Theme.Hex(0xB9BDC2), Muted = Theme.Hex(0x8C9198),
        Link = Theme.Hex(0xE2683F), Ok = Theme.Hex(0x6DB04A), Error = Theme.Hex(0xFF8A80), SelBg = Theme.Hex(0xF6F5F2),
        SelText = Theme.Hex(0x16181B), Hover = Theme.Hex(0x23262A),
    };
}

/// Brand colours, fonts and drawing helpers. Light and dark; follows Windows unless the user picks one.
static class Theme
{
    public static Palette P { get; private set; } = Palette.DarkP;
    public static event Action? Changed;

    public static void Apply(string mode)   // "system" | "dark" | "light"
    {
        P = mode switch { "light" => Palette.Light, "dark" => Palette.DarkP, _ => SystemIsLight() ? Palette.Light : Palette.DarkP };
        Changed?.Invoke();
    }

    /// Windows' app theme. Missing setting (or no Windows 10+) → dark, the brand default.
    public static bool SystemIsLight()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    public static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    public static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    public static Color Alpha(Color c, int a) => Color.FromArgb(a, c);

    // ---------- fonts (bundled: Space Grotesk, Inter, JetBrains Mono; all SIL OFL) ----------

    static readonly PrivateFontCollection fonts = new();
    [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, out uint pcFonts);

    public static void LoadFonts()
    {
        var asm = typeof(Theme).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            var data = new byte[s.Length];
            s.ReadExactly(data);
            IntPtr mem = Marshal.AllocCoTaskMem(data.Length);   // kept for the app's lifetime, as GDI needs it
            Marshal.Copy(data, 0, mem, data.Length);
            fonts.AddMemoryFont(mem, data.Length);
            AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, out _);   // so TextRenderer (GDI) can use it too
        }
    }

    static FontFamily Family(string name) =>
        fonts.Families.FirstOrDefault(f => f.Name == name) ?? new FontFamily(name.StartsWith("JetBrains") ? "Consolas" : "Segoe UI");

    static readonly Dictionary<string, Font> cache = new();
    static Font Get(string family, float size, FontStyle style = FontStyle.Regular)
    {
        string key = $"{family}|{size}|{style}";
        if (!cache.TryGetValue(key, out var f)) cache[key] = f = new Font(Family(family), size, style, GraphicsUnit.Point);
        return f;
    }

    public static Font Body(float size = 9.75f) => Get("Inter", size);
    public static Font Medium(float size = 9.75f) => Get("Inter Medium", size);
    public static Font Semi(float size = 9.75f) => Get("Inter SemiBold", size);
    public static Font Display(float size = 13f) => Get("Space Grotesk", size, FontStyle.Bold);
    public static Font Mono(float size = 9f) => Get("JetBrains Mono Medium", size);

    // ---------- drawing ----------

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

    public static void FillRound(Graphics g, Color c, RectangleF r, float rad) { using var p = Round(r, rad); using var b = new SolidBrush(c); g.FillPath(b, p); }
    public static void StrokeRound(Graphics g, Color c, RectangleF r, float rad, float w = 1f) { using var p = Round(r, rad); using var pen = new Pen(c, w); g.DrawPath(pen, p); }

    public static void Hq(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }

    public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter) =>
        TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

    // ---------- the brand marks ----------

    static readonly (string pts, int col)[] Peaks =
    {
        ("10,100 38,22 38,100", 0xB9BDC2), ("38,22 60,66 38,100", 0xC8452A), ("60,66 60,100 38,100", 0x8C9198),
        ("60,66 82,22 82,100", 0xD7DADD), ("60,66 82,100 60,100", 0xA3A8AE), ("82,22 110,100 82,100", 0x8C9198),
    };
    static readonly (string pts, int col)[] Arrow =
    {
        ("60,22 90,56 60,56", 0xA3A8AE), ("60,22 60,56 30,56", 0xD7DADD), ("50,56 70,56 50,96", 0xB9BDC2),
        ("70,56 70,96 50,96", 0x8C9198), ("60,22 70,34 60,34", 0xC8452A), ("60,22 60,34 50,34", 0xE2683F),
    };

    static void Polys(Graphics g, RectangleF r, (string pts, int col)[] polys, float scale = 1f, float ox = 0, float oy = 0)
    {
        float k = r.Width / 120f;
        foreach (var (pts, col) in polys)
        {
            var p = pts.Split(' ').Select(s => s.Split(',')).Select(a =>
                new PointF(r.X + (float.Parse(a[0]) * scale + ox) * k, r.Y + (float.Parse(a[1]) * scale + oy) * k)).ToArray();
            using var b = new SolidBrush(Hex(col));
            g.FillPolygon(b, p);
        }
    }

    /// The Uploader's product icon: a faceted arrow on the Ink tile.
    public static void UploaderIcon(Graphics g, RectangleF r, bool edge)
    {
        FillRound(g, Palette.Ink, r, r.Width * 0.233f);
        if (edge) StrokeRound(g, Palette.Graphite, RectangleF.Inflate(r, -0.5f, -0.5f), r.Width * 0.233f, 1.2f);
        Polys(g, r, Arrow);
    }

    /// The Marleybop mark: Twin Peaks on the Ink tile.
    public static void Mark(Graphics g, RectangleF r)
    {
        FillRound(g, Palette.Ink, r, r.Width * 0.233f);
        Polys(g, r, Peaks, 0.75f, 15, 14);
    }

    public static Icon AppIcon()
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp)) { Hq(g); UploaderIcon(g, new RectangleF(0, 0, 64, 64), false); }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ---------- Windows window chrome ----------

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);

    public static void TitleBar(Form f)
    {
        if (!f.IsHandleCreated) return;
        int dark = P.Dark ? 1 : 0;
        DwmSetWindowAttribute(f.Handle, 20, ref dark, 4);
        int caption = P.Surface.R | (P.Surface.G << 8) | (P.Surface.B << 16);
        DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);
    }

    public static void Scrollbars(Control c)
    {
        void Set() => SetWindowTheme(c.Handle, P.Dark ? "DarkMode_Explorer" : "Explorer", null);
        if (c.IsHandleCreated) Set(); else c.HandleCreated += (_, _) => Set();
    }
}
