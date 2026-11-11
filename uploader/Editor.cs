using System.Drawing.Drawing2D;

namespace WallpaperStudioUploader;

public enum StageMode { Crop, Result, Tiled }

/// The image stage: crop square over the source (drag to move, scroll to zoom), the finished result,
/// or a 3×3 tiling check. Empty, it's a drop zone.
public sealed class ImageStage : Owned
{
    public Bitmap? Source;          // the source image, shrunk for speed
    public Bitmap? Result;          // the pipeline's output preview (square)
    public EditState? State;
    public StageMode Mode = StageMode.Crop;
    public bool ShowRepeat;         // Look tab: show the wallpaper's Repeat setting
    public string Busy = "";
    public event Action? CropChanged;
    public event Action<string>? FileDropped;
    public event Action<string>? TextDropped;
    public event Action<Image>? ImageDropped;
    public event Action? Browse;

    bool dragOver, dragging;
    PointF dragStart; float startX, startY;

    public ImageStage()
    {
        AllowDrop = true;
        DragEnter += (_, e) =>
        {
            var d = e.Data;
            if (d != null && (d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent(DataFormats.UnicodeText) || d.GetDataPresent(DataFormats.Bitmap)))
            { e.Effect = DragDropEffects.Copy; dragOver = true; Invalidate(); }
        };
        DragLeave += (_, _) => { dragOver = false; Invalidate(); };
        DragDrop += (_, e) =>
        {
            dragOver = false; Invalidate();
            var d = e.Data; if (d == null) return;
            if ((d.GetData(DataFormats.UnicodeText) ?? d.GetData(DataFormats.Text)) is string t && t.Trim().StartsWith("http", StringComparison.OrdinalIgnoreCase)) TextDropped?.Invoke(t.Trim().Split('\n')[0].Trim());
            else if (d.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) FileDropped?.Invoke(f[0]);
            else if (d.GetData(DataFormats.Bitmap) is Image img) ImageDropped?.Invoke(img);
        };
        Click += (_, _) => { if (Source == null) Browse?.Invoke(); };
    }

    bool Cropping => Source != null && State != null && Mode == StageMode.Crop && !(State.Kind == ItemKind.Spray && State.FitWhole);

    // where the source is drawn
    RectangleF SourceRect()
    {
        if (Source == null) return RectangleF.Empty;
        float pad = 20, k = Math.Min((Width - pad * 2) / Source.Width, (Height - pad * 2) / Source.Height);
        float w = Source.Width * k, h = Source.Height * k;
        return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
    }

    RectangleF CropOnScreen()
    {
        var sr = SourceRect();
        var cr = ImageOps.CropRect(Source!.Size, State!);
        float k = sr.Width / Source.Width;
        return new RectangleF(sr.X + cr.X * k, sr.Y + cr.Y * k, cr.Width * k, cr.Height * k);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Cropping || e.Button != MouseButtons.Left) return;
        dragging = true; dragStart = e.Location; startX = State!.CropX; startY = State.CropY; Capture = true;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Cropping) Cursor = dragging || CropOnScreen().Contains(e.Location) ? Cursors.SizeAll : Cursors.Default;
        else Cursor = Source == null ? Cursors.Hand : Cursors.Default;
        if (!dragging || State == null || Source == null) return;
        var sr = SourceRect();
        State.CropX = Math.Clamp(startX + (e.X - dragStart.X) / sr.Width, 0, 1);
        State.CropY = Math.Clamp(startY + (e.Y - dragStart.Y) / sr.Height, 0, 1);
        ClampCrop();
        Invalidate(); CropChanged?.Invoke();
    }

    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; base.OnMouseUp(e); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Cropping) return;
        State!.Zoom = Math.Clamp(State.Zoom * (e.Delta > 0 ? 1.1f : 1 / 1.1f), 1f, 8f);
        ClampCrop();
        Invalidate(); CropChanged?.Invoke();
    }

    // keep the stored centre where the square actually is, so drags don't feel "sticky" at the edges
    void ClampCrop()
    {
        var cr = ImageOps.CropRect(Source!.Size, State!);
        State!.CropX = (cr.X + cr.Width / 2) / Source.Width;
        State.CropY = (cr.Y + cr.Height / 2) / Source.Height;
    }

    protected override void Draw(Graphics g)
    {
        var p = Theme.P;
        var full = new RectangleF(0, 0, Width, Height);
        using (var clip = Theme.Round(full, 14))
        {
            var st = g.Save();
            g.SetClip(clip);
            using (var b = new SolidBrush(Palette.Ink)) g.FillRectangle(b, full);
            if (Source == null) DrawEmpty(g);
            else if (Cropping) DrawCrop(g);
            else if (Result != null) DrawResult(g, Mode == StageMode.Tiled && State?.Kind != ItemKind.Spray);
            if (!string.IsNullOrEmpty(Busy)) Badge(g, Busy, top: true, warn: true);
            g.Restore(st);
        }
        if (p.Dark) Theme.StrokeRound(g, p.Border, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 14);
    }

    void DrawEmpty(Graphics g)
    {
        var inner = new RectangleF(18, 18, Width - 36, Height - 36);
        using (var pen = new Pen(dragOver || hover ? Palette.Rust : Palette.Graphite, 1.5f) { DashPattern = new[] { 5f, 4f } })
        using (var path = Theme.Round(inner, 10)) g.DrawPath(pen, path);
        float cx = Width / 2f, cy = Height / 2f;
        using (var pen = new Pen(dragOver || hover ? Palette.Rust : Theme.Hex(0x8C9198), 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            g.DrawLine(pen, cx, cy - 64, cx, cy - 30);
            g.DrawLines(pen, new[] { new PointF(cx - 13, cy - 51), new PointF(cx, cy - 64), new PointF(cx + 13, cy - 51) });
            g.DrawLines(pen, new[] { new PointF(cx - 22, cy - 36), new PointF(cx - 22, cy - 22), new PointF(cx + 22, cy - 22), new PointF(cx + 22, cy - 36) });
        }
        Center(g, "Drop an image here", Theme.Display(14f), cy - 6, Palette.Paper);
        Center(g, "or click to browse  ·  drag one from your browser  ·  Ctrl+V a link or image", Theme.Body(9f), cy + 30, Theme.Hex(0xB9BDC2));
    }

    void Center(Graphics g, string s, Font f, float y, Color c)
    {
        var sz = TextRenderer.MeasureText(s, f);
        TextRenderer.DrawText(g, s, f, new Point((int)(Width / 2f - sz.Width / 2f), (int)y), c, TextFormatFlags.NoPadding);
    }

    void DrawCrop(Graphics g)
    {
        var sr = SourceRect();
        g.DrawImage(Source!, sr);
        var c = CropOnScreen();
        using (var shade = new Region(sr))
        {
            shade.Exclude(c);
            using var b = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            g.FillRegion(b, shade);
        }
        using (var pen = new Pen(Palette.Paper, 2f)) g.DrawRectangle(pen, c.X, c.Y, c.Width, c.Height);
        using (var thin = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
            for (int i = 1; i < 3; i++)
            {
                g.DrawLine(thin, c.X + c.Width * i / 3, c.Y, c.X + c.Width * i / 3, c.Bottom);
                g.DrawLine(thin, c.X, c.Y + c.Height * i / 3, c.Right, c.Y + c.Height * i / 3);
            }
        using (var b = new SolidBrush(Palette.Paper))
            foreach (var pt in new[] { new PointF(c.X, c.Y), new PointF(c.Right, c.Y), new PointF(c.X, c.Bottom), new PointF(c.Right, c.Bottom) })
                g.FillRectangle(b, pt.X - 5, pt.Y - 5, 10, 10);
        Badge(g, "Drag to move  ·  scroll to zoom", top: false);
    }

    void DrawResult(Graphics g, bool tiled)
    {
        float pad = 20, side = Math.Min(Width, Height) - pad * 2;
        var r = new RectangleF((Width - side) / 2, (Height - side) / 2, side, side);
        if (State?.Kind == ItemKind.Spray) Checker(g, r);
        int reps = tiled ? 3 : ShowRepeat && State != null && State.Kind != ItemKind.Spray ? Math.Max(1, State.Tile) : 1;
        float t = side / reps;
        for (int y = 0; y < reps; y++) for (int x = 0; x < reps; x++) g.DrawImage(Result!, new RectangleF(r.X + x * t, r.Y + y * t, t + 0.5f, t + 0.5f));
        Badge(g, tiled ? "Tiling check: 3 × 3" : State?.Kind == ItemKind.Spray ? "Spray decal" : reps > 1 ? $"One panel, repeated {reps} × {reps}" : "Result", top: false);
    }

    static void Checker(Graphics g, RectangleF r)
    {
        using var a = new SolidBrush(Theme.Hex(0x2A2C2F));
        using var b = new SolidBrush(Theme.Hex(0x222427));
        g.FillRectangle(a, r);
        const float s = 12;
        for (float y = r.Y; y < r.Bottom; y += s)
            for (float x = r.X + (((int)((y - r.Y) / s)) % 2 == 0 ? 0 : s); x < r.Right; x += s * 2)
                g.FillRectangle(b, x, y, Math.Min(s, r.Right - x), Math.Min(s, r.Bottom - y));
    }

    void Badge(Graphics g, string text, bool top, bool warn = false)
    {
        var f = Theme.Mono(8.5f);
        var sz = TextRenderer.MeasureText(text, f);
        var b = new RectangleF(Width - sz.Width - 30, top ? 12 : Height - 38, sz.Width + 18, 26);
        Theme.FillRound(g, Color.FromArgb(215, 17, 18, 20), b, 7);
        TextRenderer.DrawText(g, text, f, Rectangle.Round(b), warn ? Palette.RustLight : Palette.Paper, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
