using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace WallpaperStudioUploader;

/// Base for owner-drawn controls: double-buffered, repaints when the theme changes,
/// and raises Click from the mouse-up position (works the same however the click arrives).
public abstract class Owned : Control
{
    protected bool hover, down;

    protected Owned()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
        Font = Theme.Body();
        Theme.Changed += OnTheme;
    }

    void OnTheme() { if (!IsDisposed) Invalidate(); }
    protected override void Dispose(bool disposing) { if (disposing) Theme.Changed -= OnTheme; base.Dispose(disposing); }

    protected Color Behind => Parent?.BackColor ?? Theme.P.Bg;
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool tap = down && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
        down = false; Invalidate();
        base.OnMouseUp(e);
        if (tap && Enabled) { OnMouseClick(e); OnClick(e); }
    }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Behind);
        Theme.Hq(e.Graphics);
        Draw(e.Graphics);
    }

    protected abstract void Draw(Graphics g);
}

/// A panel coloured from the palette, with optional border lines.
public class Surface : Panel
{
    public enum Fill { Bg, Surface, Panel }
    public Fill Kind { get; set; }
    public AnchorStyles Lines { get; set; }

    public Surface(Fill kind = Fill.Bg, AnchorStyles lines = AnchorStyles.None)
    {
        Kind = kind; Lines = lines; DoubleBuffered = true; ResizeRedraw = true;
        Recolour();
        Theme.Changed += Recolour;
    }

    void Recolour()
    {
        if (IsDisposed) return;
        BackColor = Kind switch { Fill.Surface => Theme.P.Surface, Fill.Panel => Theme.P.Panel, _ => Theme.P.Bg };
        Invalidate(true);
    }

    protected override void Dispose(bool disposing) { if (disposing) Theme.Changed -= Recolour; base.Dispose(disposing); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Lines == AnchorStyles.None) return;
        using var pen = new Pen(Theme.P.Border);
        if (Lines.HasFlag(AnchorStyles.Top)) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        if (Lines.HasFlag(AnchorStyles.Bottom)) e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        if (Lines.HasFlag(AnchorStyles.Left)) e.Graphics.DrawLine(pen, 0, 0, 0, Height);
        if (Lines.HasFlag(AnchorStyles.Right)) e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
    }
}

public enum TextRole { Text, Text2, Muted, Link, Ok, Error }

/// Text drawn in a palette colour. Wrap = true grows its height to fit.
public sealed class TextLabel : Owned
{
    public TextRole Role { get; set; } = TextRole.Text;
    public bool Wrap { get; set; }
    public ContentAlignment Align { get; set; } = ContentAlignment.MiddleLeft;

    public TextLabel(string text = "", Font? font = null, TextRole role = TextRole.Text)
    {
        Text = text; Font = font ?? Theme.Body(); Role = role; Height = Font.Height + 4;
    }

    public static Color ColourOf(TextRole r) => r switch
    {
        TextRole.Text2 => Theme.P.Text2, TextRole.Muted => Theme.P.Muted, TextRole.Link => Theme.P.Link,
        TextRole.Ok => Theme.P.Ok, TextRole.Error => Theme.P.Error, _ => Theme.P.Text,
    };

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); Invalidate(); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Fit(); }
    void Fit()
    {
        if (!Wrap || Width <= 0) return;
        int h = TextRenderer.MeasureText(Text, Font, new Size(Width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + 2;
        if (Height != h) Height = h;
    }

    protected override void Draw(Graphics g)
    {
        var f = Wrap ? TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top
                     : Align switch
                     {
                         ContentAlignment.MiddleRight => TextFormatFlags.Right | TextFormatFlags.VerticalCenter,
                         ContentAlignment.MiddleCenter => TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
                         _ => TextFormatFlags.Left | TextFormatFlags.VerticalCenter,
                     };
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ColourOf(Role), f | TextFormatFlags.NoPadding | (Wrap ? 0 : TextFormatFlags.EndEllipsis));
    }
}

public enum ButtonKind { Primary, Secondary, Ghost, Danger }

public sealed class FlatButton : Owned
{
    public ButtonKind Kind { get; set; }
    public Action<Graphics, RectangleF, Color>? Icon { get; set; }

    public FlatButton(string text, ButtonKind kind = ButtonKind.Secondary, int height = 36)
    {
        Text = text; Kind = kind; Height = height; Cursor = Cursors.Hand;
        Font = kind == ButtonKind.Primary ? Theme.Semi(10f) : Theme.Medium(9.25f);
        FitWidth();
    }

    public void FitWidth() => Width = TextRenderer.MeasureText(Text, Font).Width + 28 + (Icon != null ? 22 : 0);

    protected override void Draw(Graphics g)
    {
        var p = Theme.P;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Color fill, text, border = Color.Empty;
        switch (Kind)
        {
            case ButtonKind.Primary:
                fill = !Enabled ? p.Panel : down ? Palette.RustDark : hover ? Theme.Mix(Palette.Rust, Color.White, 0.08f) : Palette.Rust;
                text = Enabled ? Color.White : p.Muted; break;
            case ButtonKind.Secondary:
                fill = !Enabled ? p.Bg : down ? p.Panel : hover ? p.Hover : p.Surface;
                text = Enabled ? p.Text : p.Muted; border = p.BorderStrong; break;
            case ButtonKind.Danger:
                fill = hover && Enabled ? Theme.Alpha(p.Error, 28) : Behind;
                text = Enabled ? p.Error : p.Muted; break;
            default:
                fill = hover && Enabled ? p.Hover : Behind;
                text = Enabled ? (hover ? p.Text : p.Text2) : p.Muted; break;
        }
        Theme.FillRound(g, fill, r, 8);
        if (border != Color.Empty) Theme.StrokeRound(g, border, r, 8);
        var ts = TextRenderer.MeasureText(Text, Font);
        int iw = Icon != null ? 22 : 0, x = (Width - ts.Width - iw) / 2;
        Icon?.Invoke(g, new RectangleF(x, (Height - 16) / 2f, 16, 16), text);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x + iw, 0, ts.Width + 2, Height), text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }
}

/// Mutually exclusive options; the selected one is drawn inverted (Ink on light, Paper on dark).
public sealed class Segmented : Owned
{
    string[] items;
    int selected, hot = -1;
    public event Action<int>? Changed;

    public Segmented(params string[] items) { this.items = items; Height = 36; Cursor = Cursors.Hand; Font = Theme.Medium(9.25f); }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get => selected; set { if (selected == value) return; selected = value; Invalidate(); Changed?.Invoke(value); } }
    public void SetQuiet(int i) { selected = i; Invalidate(); }
    public void SetItems(params string[] it) { items = it; Invalidate(); }

    RectangleF Seg(int i) { float w = (Width - 8f) / items.Length; return new RectangleF(4 + i * w, 4, w, Height - 8); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = -1; for (int i = 0; i < items.Length; i++) if (Seg(i).Contains(e.Location)) h = i;
        if (h != hot) { hot = h; Invalidate(); }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e) { if (Enabled && hot >= 0) SelectedIndex = hot; base.OnMouseClick(e); }

    protected override void Draw(Graphics g)
    {
        var p = Theme.P;
        Theme.FillRound(g, p.Panel, new RectangleF(0, 0, Width, Height), 10);
        for (int i = 0; i < items.Length; i++)
        {
            var s = Seg(i);
            if (i == selected) Theme.FillRound(g, p.SelBg, s, 7);
            else if (i == hot && Enabled) Theme.FillRound(g, p.Hover, s, 7);
            var col = !Enabled ? p.Muted : i == selected ? p.SelText : p.Text2;
            TextRenderer.DrawText(g, items[i], i == selected ? Theme.Semi(9.25f) : Font, Rectangle.Round(s), col,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

/// A text box in a rounded field.
public sealed class Field : Panel
{
    public readonly TextBox Box;
    bool focused;

    public Field(string placeholder = "", bool multiline = false, int height = 40)
    {
        DoubleBuffered = true; Height = height; Cursor = Cursors.IBeam;
        Box = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Body(10f), PlaceholderText = placeholder, Multiline = multiline };
        if (multiline) { Box.AcceptsReturn = true; Box.ScrollBars = ScrollBars.None; }
        Controls.Add(Box);
        Box.GotFocus += (_, _) => { focused = true; Invalidate(); };
        Box.LostFocus += (_, _) => { focused = false; Invalidate(); };
        Click += (_, _) => Box.Focus();
        Recolour();
        Theme.Changed += Recolour;
    }

    void Recolour() { if (IsDisposed) return; Box.BackColor = Theme.P.Surface; Box.ForeColor = Theme.P.Text; Invalidate(); }
    protected override void Dispose(bool disposing) { if (disposing) Theme.Changed -= Recolour; base.Dispose(disposing); }

    [AllowNull]
    public override string Text { get => Box.Text; set => Box.Text = value ?? ""; }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (Box == null) { base.OnLayout(e); return; }
        if (Box.Multiline) Box.SetBounds(12, 9, Width - 24, Height - 16);
        else Box.SetBounds(12, (Height - Box.PreferredHeight) / 2 + 1, Width - 24, Box.PreferredHeight);
        base.OnLayout(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Theme.P.Bg); Theme.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, Theme.P.Surface, r, 10);
        Theme.StrokeRound(g, focused ? Palette.Rust : Theme.P.BorderStrong, r, 10, focused ? 1.5f : 1f);
    }
}

/// Label + value on one line, track below. Double-click resets to the default.
public sealed class Slider : Owned
{
    public string Label { get; set; }
    public Func<int, string> Format { get; set; } = v => v > 0 ? $"+{v}" : v.ToString();
    public int Min { get; set; }
    public int Max { get; set; } = 100;
    public int Default { get; set; }
    int value;
    bool dragging;
    public event Action<int>? Changed;

    public Slider(string label, int min, int max, int def) { Label = label; Min = min; Max = max; Default = def; value = def; Height = 46; Cursor = Cursors.Hand; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get => value; set { value = Math.Clamp(value, Min, Max); if (this.value == value) return; this.value = value; Invalidate(); Changed?.Invoke(value); } }
    public void SetQuiet(int v) { value = Math.Clamp(v, Min, Max); Invalidate(); }

    RectangleF Track => new(9, 32, Width - 18, 4);
    void SetFromX(int x) { var t = Track; Value = Min + (int)MathF.Round(Math.Clamp((x - t.X) / t.Width, 0, 1) * (Max - Min)); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Clicks == 2) { Value = Default; return; } dragging = true; Capture = true; SetFromX(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; base.OnMouseUp(e); }

    protected override void Draw(Graphics g)
    {
        var p = Theme.P;
        TextRenderer.DrawText(g, Label, Theme.Medium(9.25f), new Point(0, 2), Enabled ? p.Text : p.Muted, TextFormatFlags.NoPadding);
        var v = Format(value);
        var vs = TextRenderer.MeasureText(v, Theme.Mono(8.5f), Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, v, Theme.Mono(8.5f), new Point(Width - vs.Width, 3), value == Default ? p.Muted : p.Link, TextFormatFlags.NoPadding);
        var t = Track;
        float zero = Min < 0 ? t.X + t.Width * (0 - Min) / (float)(Max - Min) : t.X;
        float x = t.X + t.Width * (value - Min) / (float)Math.Max(1, Max - Min);
        Theme.FillRound(g, p.Panel, t, 2);
        if (Enabled) Theme.FillRound(g, Palette.Rust, new RectangleF(Math.Min(zero, x), t.Y, Math.Max(2, Math.Abs(x - zero)), t.Height), 2);
        float k = hover || dragging ? 8.5f : 7.5f;
        using (var b = new SolidBrush(p.Surface)) g.FillEllipse(b, x - k, t.Y + 2 - k, k * 2, k * 2);
        using (var pen = new Pen(Enabled ? Palette.Rust : p.BorderStrong, 2f)) g.DrawEllipse(pen, x - k, t.Y + 2 - k, k * 2, k * 2);
    }
}

/// A tick box with a label, drawn as a full-width row.
public sealed class CheckRow : Owned
{
    bool on;
    public event Action<bool>? Changed;
    public CheckRow(string text) { Text = text; Height = 44; Cursor = Cursors.Hand; Font = Theme.Body(9.75f); }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked { get => on; set { if (on == value) return; on = value; Invalidate(); Changed?.Invoke(value); } }
    public void SetQuiet(bool v) { on = v; Invalidate(); }
    protected override void OnMouseClick(MouseEventArgs e) { Checked = !Checked; base.OnMouseClick(e); }

    protected override void Draw(Graphics g)
    {
        var p = Theme.P;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, hover ? p.Hover : p.Surface, r, 10);
        Theme.StrokeRound(g, p.Border, r, 10);
        var box = new RectangleF(13, (Height - 18) / 2f, 18, 18);
        if (on && Enabled)
        {
            Theme.FillRound(g, Palette.Rust, box, 4);
            using var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[] { new PointF(box.X + 4, box.Y + 9.5f), new PointF(box.X + 7.5f, box.Y + 13), new PointF(box.X + 14, box.Y + 5.5f) });
        }
        else Theme.StrokeRound(g, p.BorderStrong, box, 4, 1.5f);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(42, 0, Width - 50, Height), Enabled ? p.Text : p.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

/// Filter chips with counts (All 6, Walls 3, …). Selected chip is inverted.
public sealed class Chips : Owned
{
    (string label, int count)[] items = Array.Empty<(string, int)>();
    readonly List<RectangleF> boxes = new();
    int selected, hot = -1;
    public event Action<int>? Changed;

    public Chips() { Height = 30; Cursor = Cursors.Hand; Font = Theme.Medium(9f); }
    public int SelectedIndex => selected;
    public void SetItems((string, int)[] it) { items = it; LayoutChips(); Invalidate(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutChips(); }

    void LayoutChips()
    {
        boxes.Clear();
        float x = 0, y = 0;
        foreach (var (l, c) in items)
        {
            float w = TextRenderer.MeasureText($"{l} {c}", Font).Width + 14;
            if (x + w > Width && x > 0) { x = 0; y += 34; }
            boxes.Add(new RectangleF(x, y, w, 28));
            x += w + 6;
        }
        int h = (int)(boxes.Count > 0 ? boxes[^1].Bottom + 2 : 30);
        if (Height != h) Height = h;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = boxes.FindIndex(b => b.Contains(e.Location));
        if (h != hot) { hot = h; Invalidate(); }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e) { if (hot >= 0 && hot != selected) { selected = hot; Invalidate(); Changed?.Invoke(selected); } base.OnMouseClick(e); }

    protected override void Draw(Graphics g)
    {
        LayoutChips();
        var p = Theme.P;
        for (int i = 0; i < items.Length; i++)
        {
            var b = boxes[i];
            bool sel = i == selected;
            Theme.FillRound(g, sel ? p.SelBg : i == hot ? p.Hover : p.Panel, b, 7);
            var (l, c) = items[i];
            TextRenderer.DrawText(g, $"{l} {c}", sel ? Theme.Semi(9f) : Font, Rectangle.Round(b), sel ? p.SelText : p.Text2,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

/// One row in the library list.
public sealed class ItemRow : Owned
{
    public LibItem? Item { get; private set; }
    public bool Selected { get; set; }
    public ItemRow() { Height = 68; Cursor = Cursors.Hand; }
    public void Bind(LibItem it) { Item = it; Invalidate(); }

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t.ToLocalTime();
        if (d.TotalMinutes < 2) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        if (d.TotalDays < 14) return $"{(int)d.TotalDays} days ago";
        if (d.TotalDays < 60) return $"{(int)(d.TotalDays / 7)} weeks ago";
        return t.ToLocalTime().ToString("d MMM yyyy");
    }

    public static string VisName(Visibility v) => v switch
    {
        Visibility.Public => "Public", Visibility.Unlisted => "Unlisted", Visibility.FriendsOnly => "Friends", _ => "Hidden",
    };

    protected override void Draw(Graphics g)
    {
        var p = Theme.P;
        if (Item == null) return;
        var r = new RectangleF(0.5f, 2.5f, Width - 1, Height - 5);
        if (Selected)
        {
            Theme.FillRound(g, p.Surface, r, 12);
            Theme.StrokeRound(g, p.BorderStrong, r, 12);
            using var b = new SolidBrush(Palette.Rust);
            g.FillRectangle(b, 1, 14, 3, Height - 28);
        }
        else if (hover) Theme.FillRound(g, p.Hover, r, 12);

        var thumb = new RectangleF(12, (Height - 48) / 2f, 48, 48);
        using (var clip = Theme.Round(thumb, 8))
        {
            var st = g.Save(); g.SetClip(clip);
            if (Item.Thumb != null) g.DrawImage(Item.Thumb, thumb);
            else { using var b = new SolidBrush(p.Panel); g.FillRectangle(b, thumb); }
            g.Restore(st);
        }
        string meta = $"{Kinds.Label(Item.Kind).Replace("Wallpaper: ", "").Replace("Spray can decal", "Spray")} · {VisName(Item.Visibility)} · {Ago(Item.Updated)}";
        meta = char.ToUpper(meta[0]) + meta[1..];
        TextRenderer.DrawText(g, Item.Title, Selected ? Theme.Semi(10f) : Theme.Medium(10f), new Rectangle(72, 13, Width - 80, 22), p.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, meta, Theme.Body(8.75f), new Rectangle(72, 36, Width - 80, 20), p.Text2, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

/// Thin progress bar.
public sealed class ProgressLine : Owned
{
    float value;
    public ProgressLine() { Height = 3; }
    public float Value { get => value; set { this.value = Math.Clamp(value, 0, 1); Invalidate(); } }
    protected override void Draw(Graphics g)
    {
        if (value <= 0) return;
        using var b = new SolidBrush(Palette.Rust);
        g.FillRectangle(b, 0, 0, Width * value, Height);
    }
}
