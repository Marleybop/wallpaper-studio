using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WallpaperStudioUploader;

public enum ItemKind { Wall, Floor, Ceiling, Spray }

public static class Kinds
{
    // The Workshop ItemType, which is also the item's Workshop tag.
    public static string ItemType(ItemKind k) => k switch
    {
        ItemKind.Wall => "Wallpaper Wall",
        ItemKind.Floor => "Wallpaper Flooring",
        ItemKind.Ceiling => "Wallpaper Ceiling",
        _ => "Spray Can Decal",
    };

    public static string Label(ItemKind k) => k switch
    {
        ItemKind.Wall => "Wallpaper: wall",
        ItemKind.Floor => "Wallpaper: floor",
        ItemKind.Ceiling => "Wallpaper: ceiling",
        _ => "Spray can decal",
    };

    // The word Wallpaper Studio's wpstudio.add takes (sprays aren't managed by the plugin).
    public static string? PluginKind(ItemKind k) => k switch
    {
        ItemKind.Wall => "wall",
        ItemKind.Floor => "floor",
        ItemKind.Ceiling => "ceiling",
        _ => null,
    };

    public static bool TryParse(string s, out ItemKind k)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "wall": k = ItemKind.Wall; return true;
            case "floor": case "flooring": k = ItemKind.Floor; return true;
            case "ceiling": k = ItemKind.Ceiling; return true;
            case "spray": case "decal": k = ItemKind.Spray; return true;
        }
        k = ItemKind.Wall; return false;
    }
}

public sealed class BuildOptions
{
    public ItemKind Kind = ItemKind.Wall;
    public int Size = 2048;        // texture size: 2048 (best) or 1024 (smaller download)
    public int Tile = 1;           // wallpapers: repeat the image N×N times
    public float Gloss = 0.25f;    // wallpapers: 0 = matte, 1 = glossy
    public float Bump = 1.0f;      // wallpapers: depth of the generated surface texture, 0 = flat
    public ulong AuthorId;         // your SteamID (written to the manifest)
}

/// Turns one image into a Rust Workshop folder: textures, icons and manifest.txt.
public static class ItemBuilder
{
    public static readonly string[] Outputs =
        { "_MainTex0.png", "_BumpMap0.png", "_SpecGlossMap0.png", "_OcclusionMap0.png", "icon.png", "icon_background.png", "manifest.txt" };

    public static void Build(string imagePath, string outDir, BuildOptions o, Action<string>? log = null)
    {
        Directory.CreateDirectory(outDir);
        foreach (var f in Outputs) { var p = Path.Combine(outDir, f); if (File.Exists(p)) File.Delete(p); }
        using var src = LoadImage(imagePath);
        if (o.Kind == ItemKind.Spray) BuildSpray(src, outDir, o, log);
        else BuildWallpaper(src, outDir, o, log);
    }

    public static Bitmap LoadImage(string path)
    {
        // Copy into memory so the source file isn't locked while we work.
        using var fs = File.OpenRead(path);
        using var img = Image.FromStream(fs);
        var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(img, 0, 0, img.Width, img.Height);
        return bmp;
    }

    static Bitmap Resize(Image src, Rectangle srcRect, int w, int h, Color? background = null)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(background ?? Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        using var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY);   // no dark fringe at the edges
        g.DrawImage(src, new Rectangle(0, 0, w, h), srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height, GraphicsUnit.Pixel, attrs);
        return bmp;
    }

    // ---------- wallpapers ----------

    public sealed class WallpaperMaps
    {
        public int N;
        public uint[] Col = Array.Empty<uint>(), Nrm = Array.Empty<uint>(), Spec = Array.Empty<uint>(), Ao = Array.Empty<uint>();
    }

    /// The four wallpaper maps at size n. Blur radii and bump strength scale with n, so a small
    /// preview looks like the full-size upload (at 2048 this matches make_wallpaper.py).
    public static WallpaperMaps ComputeWallpaper(Bitmap src, BuildOptions o, int n, Action<string>? log = null)
    {
        float k = n / 2048f;
        int side = Math.Min(src.Width, src.Height);
        var crop = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);   // centre square
        int tiles = Math.Max(1, o.Tile), cell = Math.Max(1, n / tiles);

        log?.Invoke($"Colour map {n}×{n}" + (tiles > 1 ? $", tiled {tiles}×{tiles}" : ""));
        using var tile = Resize(src, crop, cell, cell);
        using var col = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(col))
        {
            g.Clear(Color.Black);
            for (int y = 0; y < n; y += cell)
                for (int x = 0; x < n; x += cell)
                    g.DrawImageUnscaled(tile, x, y);
        }
        var px = ReadPixels(col);
        for (int i = 0; i < px.Length; i++) px[i] |= 0xFF000000u;   // wallpapers are opaque

        // Height from brightness, softened so it reads as surface texture rather than noise.
        var lum = new float[n * n];
        for (int i = 0; i < lum.Length; i++)
        {
            uint p = px[i];
            lum[i] = (0.299f * ((p >> 16) & 255) + 0.587f * ((p >> 8) & 255) + 0.114f * (p & 255)) / 255f;
        }
        var hgt = Blur(lum, n, Math.Max(0.4f, 1.5f * k));

        log?.Invoke("Normal map");
        var nrm = new uint[n * n];
        float bump = o.Bump * 2f / k;   // per-pixel slope grows as pixels get bigger
        Parallel.For(0, n, y =>
        {
            int yu = (y - 1 + n) % n, yd = (y + 1) % n;
            for (int x = 0; x < n; x++)
            {
                int xl = (x - 1 + n) % n, xr = (x + 1) % n;
                float dx = (hgt[y * n + xr] - hgt[y * n + xl]) * bump;
                float dy = (hgt[yu * n + x] - hgt[yd * n + x]) * bump;   // OpenGL style, Y up
                float nx = -dx, ny = -dy, nz = 1f, len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                nrm[y * n + x] = Argb(255, B01(nx / len * 0.5f + 0.5f), B01(ny / len * 0.5f + 0.5f), B01(nz / len * 0.5f + 0.5f));
            }
        });

        log?.Invoke("Specular/gloss map");
        float mean = lum.Average();
        var spec = new uint[n * n];
        const byte specGrey = 25;   // plain non-metal surface
        for (int i = 0; i < spec.Length; i++)
            spec[i] = Argb(B01(o.Gloss + (lum[i] - mean) * 0.15f), specGrey, specGrey, specGrey);

        log?.Invoke("Occlusion map");
        var wide = Blur(hgt, n, 12f * k);
        var ao = new uint[n * n];
        for (int i = 0; i < ao.Length; i++)
        {
            byte v = B01(Math.Clamp(0.85f + (hgt[i] - wide[i]) * 1.5f, 0.6f, 1f));
            ao[i] = Argb(255, v, v, v);
        }
        return new WallpaperMaps { N = n, Col = px, Nrm = nrm, Spec = spec, Ao = ao };
    }

    static void BuildWallpaper(Bitmap src, string outDir, BuildOptions o, Action<string>? log)
    {
        var m = ComputeWallpaper(src, o, o.Size, log);
        int n = m.N;
        SaveRaw(m.Col, n, Path.Combine(outDir, "_MainTex0.png"));
        SaveRaw(m.Nrm, n, Path.Combine(outDir, "_BumpMap0.png"));
        SaveRaw(m.Spec, n, Path.Combine(outDir, "_SpecGlossMap0.png"));
        SaveRaw(m.Ao, n, Path.Combine(outDir, "_OcclusionMap0.png"));

        using var col = ToBitmap(m.Col, n);
        using var icon = Resize(col, new Rectangle(0, 0, n, n), 512, 512);
        icon.Save(Path.Combine(outDir, "icon.png"), ImageFormat.Png);
        icon.Save(Path.Combine(outDir, "icon_background.png"), ImageFormat.Png);

        WriteManifest(outDir, o, new()
        {
            ["_MainTex"] = "_MainTex0.png", ["_OcclusionMap"] = "_OcclusionMap0.png",
            ["_SpecGlossMap"] = "_SpecGlossMap0.png", ["_BumpMap"] = "_BumpMap0.png",
        }, cutoff: 0.0);
    }

    // ---------- preview ----------

    /// What the preview pane shows: the colour texture as it will be uploaded (cropped and tiled for
    /// wallpapers, fitted with transparency for sprays), made at a small size.
    public static Bitmap Preview(Bitmap src, BuildOptions o, int n = 640)
    {
        if (o.Kind == ItemKind.Spray) return SprayDecal(src, n);
        int side = Math.Min(src.Width, src.Height);
        var crop = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
        int tiles = Math.Max(1, o.Tile), cell = Math.Max(1, n / tiles);
        using var tile = Resize(src, crop, cell, cell);
        var col = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(col);
        g.Clear(Color.Black);
        for (int y = 0; y < n; y += cell)
            for (int x = 0; x < n; x += cell)
                g.DrawImageUnscaled(tile, x, y);
        return col;
    }

    // ---------- sprays ----------

    /// Fit the whole image into an n×n square (no cropping), keeping its transparency.
    public static Bitmap SprayDecal(Bitmap src, int n)
    {
        float scale = (float)n / Math.Max(src.Width, src.Height);
        int w = Math.Max(1, (int)MathF.Round(src.Width * scale)), h = Math.Max(1, (int)MathF.Round(src.Height * scale));
        using var fitted = Resize(src, new Rectangle(0, 0, src.Width, src.Height), w, h);
        var dec = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dec);
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImageUnscaled(fitted, (n - w) / 2, (n - h) / 2);
        return dec;
    }

    static void BuildSpray(Bitmap src, string outDir, BuildOptions o, Action<string>? log)
    {
        int n = o.Size;
        log?.Invoke($"Decal {n}×{n} (whole image fitted, transparency kept)");
        using var dec = SprayDecal(src, n);
        dec.Save(Path.Combine(outDir, "_MainTex0.png"), ImageFormat.Png);

        using var icon = Resize(dec, new Rectangle(0, 0, n, n), 512, 512);
        icon.Save(Path.Combine(outDir, "icon.png"), ImageFormat.Png);
        using var iconBg = Resize(dec, new Rectangle(0, 0, n, n), 512, 512, Color.FromArgb(255, 44, 46, 42));
        iconBg.Save(Path.Combine(outDir, "icon_background.png"), ImageFormat.Png);   // the Workshop preview

        // Sprays carry only the colour map. _Cutoff matches what existing spray uploads use.
        WriteManifest(outDir, o, new() { ["_MainTex"] = "_MainTex0.png" }, cutoff: 1.0);
    }

    // ---------- manifest ----------

    static void WriteManifest(string outDir, BuildOptions o, Dictionary<string, string> textures, double cutoff)
    {
        static JsonObject Rgba(double r, double g, double b, double a) => new() { ["r"] = r, ["g"] = g, ["b"] = b, ["a"] = a };
        var tex = new JsonObject();
        foreach (var kv in textures) tex[kv.Key] = kv.Value;
        var manifest = new JsonObject
        {
            ["Version"] = 3,
            ["ItemType"] = Kinds.ItemType(o.Kind),
            ["AuthorId"] = o.AuthorId,
            ["Groups"] = new JsonArray(new JsonObject
            {
                ["Textures"] = tex,
                ["Floats"] = new JsonObject
                {
                    ["_Cutoff"] = cutoff, ["_BumpScale"] = 1.0, ["_Glossiness"] = 1.0, ["_OcclusionStrength"] = 1.0,
                    ["_DetailNormalMapScale"] = 2.0, ["_DetailOcclusionStrength"] = 0.489,
                    ["_DetailOverlaySmoothness"] = 1.0, ["_DetailOverlaySpecular"] = 1.0,
                },
                ["Colors"] = new JsonObject
                {
                    ["_Color"] = Rgba(1, 1, 1, 1), ["_SpecColor"] = Rgba(1, 1, 1, 1), ["_EmissionColor"] = Rgba(0, 0, 0, 1),
                },
            }),
            ["PublishDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
        };
        File.WriteAllText(Path.Combine(outDir, "manifest.txt"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    // ---------- pixel helpers ----------

    static byte B01(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
    static uint Argb(byte a, byte r, byte g, byte b) => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    static uint[] ReadPixels(Bitmap bmp)
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

    static void WritePixels(Bitmap bmp, uint[] px)
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

    static Bitmap ToBitmap(uint[] px, int n)
    {
        var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        WritePixels(bmp, px);
        return bmp;
    }

    static void SaveRaw(uint[] px, int n, string path)
    {
        using var bmp = ToBitmap(px, n);
        bmp.Save(path, ImageFormat.Png);
    }

    // Separable Gaussian blur that wraps at the edges, so tiled wallpapers stay seamless.
    static float[] Blur(float[] src, int n, float sigma)
    {
        int r = (int)MathF.Ceiling(sigma * 3);
        var k = new float[2 * r + 1];
        float sum = 0;
        for (int i = -r; i <= r; i++) sum += k[i + r] = MathF.Exp(-(i * i) / (2 * sigma * sigma));
        for (int i = 0; i < k.Length; i++) k[i] /= sum;

        var tmp = new float[n * n];
        var dst = new float[n * n];
        Parallel.For(0, n, y =>
        {
            int row = y * n;
            for (int x = 0; x < n; x++)
            {
                float a = 0;
                for (int i = -r; i <= r; i++) a += src[row + ((x + i) % n + n) % n] * k[i + r];
                tmp[row + x] = a;
            }
        });
        Parallel.For(0, n, y =>
        {
            for (int x = 0; x < n; x++)
            {
                float a = 0;
                for (int i = -r; i <= r; i++) a += tmp[(((y + i) % n + n) % n) * n + x] * k[i + r];
                dst[y * n + x] = a;
            }
        });
        return dst;
    }
}
