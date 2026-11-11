using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace WallpaperStudioUploader;

/// Everything about one item that the editor can change. Saved as edit.json next to the item's source image,
/// so re-opening an item restores exactly how it was made.
public sealed class EditState
{
    public ulong Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public ItemKind Kind { get; set; } = ItemKind.Wall;
    public Visibility Visibility { get; set; } = Visibility.Hidden;
    public int Size { get; set; } = 2048;

    public string SourceFile { get; set; } = "";     // file name inside the item folder (source.png, …)
    public string? SourceUrl { get; set; }
    public bool Upscaled { get; set; }                // use source-upscaled.png instead of the source

    // crop: centre of the square (0..1 of the image) and zoom (1 = biggest square that fits)
    public float CropX { get; set; } = 0.5f;
    public float CropY { get; set; } = 0.5f;
    public float Zoom { get; set; } = 1f;
    public bool FitWhole { get; set; } = true;        // sprays: keep the whole image instead of cropping

    // adjust (-100..100, sharpen 0..100)
    public int Brightness { get; set; }
    public int Contrast { get; set; }
    public int Saturation { get; set; }
    public int Warmth { get; set; }
    public int Sharpen { get; set; }

    public int Seamless { get; set; }                 // 0 = off, 1..100 = blend width

    // wallpaper look
    public int Tile { get; set; } = 1;
    public int Gloss { get; set; } = 25;
    public int Bump { get; set; } = 100;

    public EditState Clone() => (EditState)MemberwiseClone();
    public string Json() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    public static EditState? Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<EditState>(File.ReadAllText(path)) : null; }
        catch { return null; }
    }

    /// Things that change the image (for knowing when the preview must be re-made).
    public string ImageKey() => $"{SourceFile}|{Upscaled}|{CropX:0.###}|{CropY:0.###}|{Zoom:0.###}|{FitWhole}|{Kind == ItemKind.Spray}|{Brightness}|{Contrast}|{Saturation}|{Warmth}|{Sharpen}|{Seamless}";
}

/// The editor's image pipeline: source → crop → colour → seamless → sharpen → square texture.
public static class ImageOps
{
    /// The square the crop selects, in source pixels.
    public static RectangleF CropRect(Size src, EditState e)
    {
        float side = Math.Min(src.Width, src.Height) / Math.Clamp(e.Zoom, 1f, 8f);
        float cx = Math.Clamp(e.CropX * src.Width, side / 2, src.Width - side / 2);
        float cy = Math.Clamp(e.CropY * src.Height, side / 2, src.Height - side / 2);
        return new RectangleF(cx - side / 2, cy - side / 2, side, side);
    }

    /// Runs the pipeline at n×n.
    public static Bitmap Render(Bitmap src, EditState e, int n)
    {
        var dst = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            using var attrs = new ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY);
            attrs.SetColorMatrix(ColourMatrix(e));
            if (e.Kind == ItemKind.Spray && e.FitWhole)
            {
                float k = (float)n / Math.Max(src.Width, src.Height);
                float w = src.Width * k, h = src.Height * k;
                g.DrawImage(src, new Rectangle((int)((n - w) / 2), (int)((n - h) / 2), (int)Math.Round(w), (int)Math.Round(h)),
                    0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
            }
            else
            {
                var r = CropRect(src.Size, e);
                g.DrawImage(src, new Rectangle(0, 0, n, n), r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel, attrs);
            }
        }
        if (e.Seamless > 0 && e.Kind != ItemKind.Spray) MakeSeamless(dst, e.Seamless / 100f);
        if (e.Sharpen > 0) SharpenInPlace(dst, e.Sharpen / 100f);
        return dst;
    }

    // ---------- colour ----------

    static ColorMatrix ColourMatrix(EditState e)
    {
        float b = e.Brightness / 100f * 0.25f;
        float c = 1f + e.Contrast / 100f * (e.Contrast > 0 ? 0.8f : 0.6f);
        float s = 1f + e.Saturation / 100f;
        float w = e.Warmth / 100f * 0.08f;
        const float lr = 0.2126f, lg = 0.7152f, lb = 0.0722f;
        // saturation
        float[,] m =
        {
            { lr * (1 - s) + s, lr * (1 - s),     lr * (1 - s),     0, 0 },
            { lg * (1 - s),     lg * (1 - s) + s, lg * (1 - s),     0, 0 },
            { lb * (1 - s),     lb * (1 - s),     lb * (1 - s) + s, 0, 0 },
            { 0, 0, 0, 1, 0 },
            { 0, 0, 0, 0, 1 },
        };
        // contrast around mid-grey, then brightness and warmth as offsets
        float t = 0.5f * (1 - c);
        var cm = new ColorMatrix();
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) cm[i, j] = m[i, j] * c;
        cm[3, 3] = 1; cm[4, 4] = 1;
        cm[4, 0] = t + b + w; cm[4, 1] = t + b; cm[4, 2] = t + b - w;
        return cm;
    }

    // ---------- seamless ----------

    /// Blends the image with a copy shifted by half its size, using the shifted copy near the edges
    /// (where it is continuous across the wrap) and the original in the middle. Result tiles without seams.
    static void MakeSeamless(Bitmap bmp, float strength)
    {
        int n = bmp.Width, h = n / 2;
        var px = Read(bmp);
        var outPx = new uint[px.Length];
        float band = Math.Clamp(strength, 0.05f, 1f) * 0.5f;   // fraction of the half-size used for blending
        Parallel.For(0, n, y =>
        {
            float wy = Weight(y, n, band);
            for (int x = 0; x < n; x++)
            {
                float wt = Math.Min(Weight(x, n, band), wy);   // 1 = original, 0 = shifted copy
                uint a = px[y * n + x], b = px[((y + h) % n) * n + (x + h) % n];
                outPx[y * n + x] = Lerp(b, a, wt);
            }
        });
        Write(bmp, outPx);
    }

    static float Weight(int i, int n, float band)
    {
        float d = Math.Min(i, n - 1 - i) / (n / 2f);   // 0 at the edge, 1 in the middle
        float t = Math.Clamp(d / band, 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    static uint Lerp(uint a, uint b, float t)
    {
        uint Ch(int sh) => (uint)Math.Clamp((int)MathF.Round(((a >> sh) & 255) * (1 - t) + ((b >> sh) & 255) * t), 0, 255) << sh;
        return Ch(24) | Ch(16) | Ch(8) | Ch(0);
    }

    // ---------- sharpen ----------

    static void SharpenInPlace(Bitmap bmp, float amount)
    {
        int w = bmp.Width, hgt = bmp.Height;
        var px = Read(bmp);
        var outPx = (uint[])px.Clone();
        float k = amount * 1.2f;
        Parallel.For(1, hgt - 1, y =>
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                uint c = px[i];
                int Ch(int sh)
                {
                    int ctr = (int)((c >> sh) & 255);
                    int nb = (int)((px[i - 1] >> sh) & 255) + (int)((px[i + 1] >> sh) & 255) + (int)((px[i - w] >> sh) & 255) + (int)((px[i + w] >> sh) & 255);
                    return Math.Clamp((int)MathF.Round(ctr + k * (4 * ctr - nb)), 0, 255);
                }
                outPx[i] = (c & 0xFF000000u) | ((uint)Ch(16) << 16) | ((uint)Ch(8) << 8) | (uint)Ch(0);
            }
        });
        Write(bmp, outPx);
    }

    // ---------- helpers ----------

    public static Bitmap Load(string path)
    {
        using var fs = File.OpenRead(path);
        using var img = Image.FromStream(fs);
        var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(img, 0, 0, img.Width, img.Height);
        return bmp;
    }

    /// A copy no bigger than max on its long side (for fast previews).
    public static Bitmap Shrink(Bitmap src, int max)
    {
        float k = Math.Min(1f, (float)max / Math.Max(src.Width, src.Height));
        if (k >= 1f) return new Bitmap(src);
        var b = new Bitmap(Math.Max(1, (int)(src.Width * k)), Math.Max(1, (int)(src.Height * k)), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(b);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, 0, 0, b.Width, b.Height);
        return b;
    }

    static uint[] Read(Bitmap bmp)
    {
        var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var px = new uint[bmp.Width * bmp.Height];
            var row = new int[bmp.Width];
            for (int y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, bmp.Width);
                Buffer.BlockCopy(row, 0, px, y * bmp.Width * 4, bmp.Width * 4);
            }
            return px;
        }
        finally { bmp.UnlockBits(d); }
    }

    static void Write(Bitmap bmp, uint[] px)
    {
        var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[bmp.Width];
            for (int y = 0; y < bmp.Height; y++)
            {
                Buffer.BlockCopy(px, y * bmp.Width * 4, row, 0, bmp.Width * 4);
                Marshal.Copy(row, 0, d.Scan0 + y * d.Stride, bmp.Width);
            }
        }
        finally { bmp.UnlockBits(d); }
    }
}
