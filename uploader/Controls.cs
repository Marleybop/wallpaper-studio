using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WallpaperStudioUploader;

/// Base for owner-drawn controls: double buffered, paints its parent's colour behind rounded shapes.
public abstract class Owned : Control
{
    protected bool hover, down;

    protected Owned()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        // We raise Click ourselves from the button-up position (see OnMouseUp) rather than WinForms'
        // check of the live cursor position, so clicks behave the same however they arrive.
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
        Font = Theme.Base; ForeColor = Theme.Text;
    }

    protected Color Behind => Parent?.BackColor ?? Theme.Bg;
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

public enum ButtonKind { Primary, Secondary, Ghost }

public sealed class FlatButton : Owned
{
    public ButtonKind Kind { get; set; } = ButtonKind.Secondary;
    public Action<Graphics, RectangleF, Color>? Icon { get; set; }

    public FlatButton(string text, ButtonKind kind = ButtonKind.Secondary)
    {
        Text = text; Kind = kind; Height = 34; Cursor = Cursors.Hand;
        Font = kind == ButtonKind.Primary ? Theme.Semi : Theme.Base;
        Width = TextRenderer.MeasureText(text, Font).Width + 32;
    }

    public void FitWidth() => Width = TextRenderer.MeasureText(Text, Font).Width + 32 + (Icon != null ? 24 : 0);

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Color fill, text, border = Color.Empty;
        switch (Kind)
        {
            case ButtonKind.Primary:
                fill = !Enabled ? Theme.Raised : down ? Theme.Accent : hover ? Theme.AccentHover : Theme.Accent;
                text = Enabled ? Theme.AccentText : Theme.Faint; break;
            case ButtonKind.Secondary:
                fill = !Enabled ? Theme.Panel : down ? Theme.Raised : hover ? Theme.Hover : Theme.Raised;
                text = Enabled ? Theme.Text : Theme.Faint; border = Theme.Border; break;
            default:
                fill = hover && Enabled ? Theme.Raised : Behind;
                text = Enabled ? (hover ? Theme.Text : Theme.Muted) : Theme.Faint; break;
        }
        Theme.FillRound(g, fill, r, 6);
        if (border != Color.Empty) Theme.StrokeRound(g, border, r, 6);

        var ts = TextRenderer.MeasureText(Text, Font);
        int iconW = Icon != null ? 22 : 0;
        int x = (Width - ts.Width - iconW) / 2;
        if (Icon != null) Icon(g, new RectangleF(x, (Height - 16) / 2f, 16, 16), text);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x + iconW, 0, ts.Width + 2, Height), text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }
}

/// Lays its visible children out top to bottom at full width and sizes itself to fit.
public class Stack : Panel
{
    public int Gap { get; set; } = 10;
    public int TopOffset { get; set; }

    public Stack() { DoubleBuffered = true; BackColor = Theme.Bg; }

    protected override void OnLayout(LayoutEventArgs e)
    {
        int y = Padding.Top + TopOffset, w = ClientSize.Width - Padding.Horizontal;
        bool any = false;
        foreach (Control c in Controls)
        {
            if (!c.Visible) continue;
            c.SetBounds(Padding.Left + c.Margin.Left, y + c.Margin.Top, w - c.Margin.Horizontal, c.Height);
            y += c.Margin.Vertical + c.Height + Gap; any = true;
        }
        int h = (any ? y - Gap : y) + Padding.Bottom;
        if (Dock != DockStyle.Fill && Height != h) Height = h;
        base.OnLayout(e);
    }
}

/// A rounded panel with a numbered title.
public sealed class Card : Stack
{
    public string Title { get; set; }
    public int Step { get; set; }
    public string? Note { get; set; }

    public Card(string title, int step = 0)
    {
        Title = title; Step = step;
        BackColor = Theme.Panel; Padding = new Padding(16, 14, 16, 16); TopOffset = 32; Gap = 12;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        Theme.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, Theme.Panel, r, 10);
        Theme.StrokeRound(g, Theme.Border, r, 10);
        float x = Padding.Left;
        if (Step > 0)
        {
            var c = new RectangleF(x, Padding.Top, 22, 22);
            using (var b = new SolidBrush(Theme.AccentDim)) g.FillEllipse(b, c);
            TextRenderer.DrawText(g, Step.ToString(), Theme.Semi, Rectangle.Round(c), Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += 30;
        }
        TextRenderer.DrawText(g, Title, Theme.CardTitle, new Point((int)x, Padding.Top + 1), Theme.Text);
        if (!string.IsNullOrEmpty(Note))
        {
            var sz = TextRenderer.MeasureText(Note, Theme.Small);
            TextRenderer.DrawText(g, Note, Theme.Small, new Point(Width - Padding.Right - sz.Width, Padding.Top + 4), Theme.Faint);
        }
    }
}

/// Small muted caption above a control.
public sealed class Caption : Label
{
    public Caption(string text)
    {
        Text = text; AutoSize = false; Height = 18; Font = Theme.Small; ForeColor = Theme.Muted; BackColor = Theme.Panel;
        Margin = new Padding(0, 0, 0, -6);
    }
}

/// Wrapped muted help text.
public sealed class Hint : Label
{
    public Hint(string text = "")
    {
        Text = text; AutoSize = false; Font = Theme.Small; ForeColor = Theme.Faint; BackColor = Theme.Panel; Height = 34;
        Margin = new Padding(0, -4, 0, 0);
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Fit(); }

    void Fit()
    {
        if (Width <= 0) return;
        int h = TextRenderer.MeasureText(Text, Font, new Size(Width, 0), TextFormatFlags.WordBreak).Height + 2;
        if (Height != h) Height = h;
    }
}

/// A text box in a rounded dark field that lights up when focused.
public sealed class Field : Panel
{
    public readonly TextBox Box;
    bool focused;

    public Field(string placeholder = "", bool multiline = false, int height = 36)
    {
        DoubleBuffered = true; BackColor = Theme.Panel; Height = height; Cursor = Cursors.IBeam;
        Box = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = Theme.Base,
            PlaceholderText = placeholder, Multiline = multiline,
        };
        if (multiline) { Box.ScrollBars = ScrollBars.None; Box.AcceptsReturn = true; }
        Controls.Add(Box);
        Box.GotFocus += (_, _) => { focused = true; Invalidate(); };
        Box.LostFocus += (_, _) => { focused = false; Invalidate(); };
        Click += (_, _) => Box.Focus();
    }

    [AllowNull]
    public override string Text { get => Box.Text; set => Box.Text = value ?? ""; }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (Box == null) { base.OnLayout(e); return; }   // layout can run before the constructor sets Box
        int padX = 11;
        if (Box.Multiline) Box.SetBounds(padX, 8, Width - padX * 2, Height - 14);
        else Box.SetBounds(padX, (Height - Box.PreferredHeight) / 2 + 1, Width - padX * 2, Box.PreferredHeight);
        base.OnLayout(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Theme.Panel); Theme.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, Theme.Input, r, 6);
        Theme.StrokeRound(g, focused ? Theme.Accent : Theme.Border, r, 6, focused ? 1.4f : 1f);
    }
}

/// A row of mutually exclusive options.
public sealed class Segmented : Owned
{
    string[] items;
    int selected, hot = -1;
    public event Action<int>? Changed;

    public Segmented(params string[] items) { this.items = items; Height = 34; Cursor = Cursors.Hand; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get => selected; set { if (selected == value) return; selected = value; Invalidate(); Changed?.Invoke(value); } }

    RectangleF Seg(int i) { float w = (Width - 6f) / items.Length; return new RectangleF(3 + i * w, 3, w, Height - 6); }

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
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, Theme.Input, r, 7);
        Theme.StrokeRound(g, Theme.Border, r, 7);
        for (int i = 0; i < items.Length; i++)
        {
            var s = Seg(i);
            if (i == selected) { Theme.FillRound(g, Theme.AccentDim, s, 5); Theme.StrokeRound(g, Theme.Mix(Theme.AccentDim, Theme.Accent, 0.45f), s, 5); }
            else if (i == hot && Enabled) Theme.FillRound(g, Theme.Raised, s, 5);
            var col = !Enabled ? Theme.Faint : i == selected ? Theme.Accent : i == hot ? Theme.Text : Theme.Muted;
            TextRenderer.DrawText(g, items[i], i == selected ? Theme.Semi : Theme.Base, Rectangle.Round(s), col,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

/// Big picture tiles for choosing wall / floor / ceiling / spray.
public sealed class TypePicker : Owned
{
    static readonly (string label, Action<Graphics, RectangleF, Color, float> icon)[] Tiles =
    {
        ("Wall", Glyphs.Bricks), ("Floor", Glyphs.Floor), ("Ceiling", Glyphs.Ceiling), ("Spray", Glyphs.Spray),
    };
    int selected, hot = -1;
    public event Action<int>? Changed;

    public TypePicker() { Height = 92; Cursor = Cursors.Hand; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get => selected; set { if (selected == value) return; selected = value; Invalidate(); Changed?.Invoke(value); } }

    RectangleF Tile(int i) { float gap = 8, w = (Width - gap * 3 - 1) / 4f; return new RectangleF(0.5f + i * (w + gap), 0.5f, w, Height - 1); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = -1; for (int i = 0; i < 4; i++) if (Tile(i).Contains(e.Location)) h = i;
        if (h != hot) { hot = h; Invalidate(); }
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e) { if (hot >= 0) SelectedIndex = hot; base.OnMouseClick(e); }

    protected override void Draw(Graphics g)
    {
        for (int i = 0; i < 4; i++)
        {
            var t = Tile(i);
            bool sel = i == selected, h = i == hot;
            Theme.FillRound(g, sel ? Theme.AccentDim : h ? Theme.Raised : Theme.Input, t, 8);
            Theme.StrokeRound(g, sel ? Theme.Accent : h ? Theme.BorderHi : Theme.Border, t, 8, sel ? 1.5f : 1f);
            var col = sel ? Theme.Accent : h ? Theme.Text : Theme.Muted;
            float s = 30;
            Tiles[i].icon(g, new RectangleF(t.X + (t.Width - s) / 2, t.Y + 16, s, s * 0.86f), col, 1.7f);
            TextRenderer.DrawText(g, Tiles[i].label, sel ? Theme.Semi : Theme.Base,
                new Rectangle((int)t.X, (int)(t.Bottom - 32), (int)t.Width, 22), sel ? Theme.Text : col, TextFormatFlags.HorizontalCenter);
        }
    }
}

/// A labelled slider with its value shown on the right.
public sealed class Slider : Owned
{
    public string Label { get; set; }
    public Func<int, string> Format { get; set; } = v => v.ToString();
    public int Min { get; set; }
    public int Max { get; set; } = 100;
    int value;
    bool dragging;
    public event Action<int>? Changed;

    public Slider(string label, int min, int max, int value) { Label = label; Min = min; Max = max; this.value = value; Height = 44; Cursor = Cursors.Hand; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get => value; set { value = Math.Clamp(value, Min, Max); if (this.value == value) return; this.value = value; Invalidate(); Changed?.Invoke(value); } }

    RectangleF Track => new(8, 30, Width - 16, 4);

    void SetFromX(int x) { var t = Track; Value = Min + (int)MathF.Round(Math.Clamp((x - t.X) / t.Width, 0, 1) * (Max - Min)); }
    protected override void OnMouseDown(MouseEventArgs e) { dragging = true; Capture = true; SetFromX(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; base.OnMouseUp(e); }

    protected override void Draw(Graphics g)
    {
        TextRenderer.DrawText(g, Label, Theme.Base, new Point(0, 2), Enabled ? Theme.Text : Theme.Faint);
        var v = Format(value); var vs = TextRenderer.MeasureText(v, Theme.Semi);
        TextRenderer.DrawText(g, v, Theme.Semi, new Point(Width - vs.Width, 2), Enabled ? Theme.Accent : Theme.Faint);
        var t = Track;
        float f = (float)(value - Min) / Math.Max(1, Max - Min), x = t.X + t.Width * f;
        Theme.FillRound(g, Theme.Raised, t, 2);
        Theme.FillRound(g, Enabled ? Theme.Accent : Theme.Faint, new RectangleF(t.X, t.Y, Math.Max(4, x - t.X), t.Height), 2);
        float k = hover || dragging ? 9 : 8;
        using (var b = new SolidBrush(Enabled ? Theme.Text : Theme.Faint)) g.FillEllipse(b, x - k, t.Y + 2 - k, k * 2, k * 2);
        using (var b = new SolidBrush(Enabled ? Theme.Accent : Theme.Raised)) g.FillEllipse(b, x - 4, t.Y + 2 - 4, 8, 8);
    }
}

/// Label on the left, [-] value [+] on the right.
public sealed class Stepper : Owned
{
    public string Label { get; set; }
    public Func<int, string> Format { get; set; } = v => v.ToString();
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 8;
    int value;
    public event Action<int>? Changed;

    public Stepper(string label, int min, int max, int value) { Label = label; Min = min; Max = max; this.value = value; Height = 34; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get => value; set { value = Math.Clamp(value, Min, Max); if (this.value == value) return; this.value = value; Invalidate(); Changed?.Invoke(value); } }

    RectangleF Minus => new(Width - 120.5f, 0.5f, 34, Height - 1);
    RectangleF Plus => new(Width - 34.5f, 0.5f, 34, Height - 1);

    protected override void OnMouseMove(MouseEventArgs e) { Cursor = Minus.Contains(e.Location) || Plus.Contains(e.Location) ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnMouseMove(e); }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (Minus.Contains(e.Location)) Value--; else if (Plus.Contains(e.Location)) Value++;
        base.OnMouseClick(e);
    }

    protected override void Draw(Graphics g)
    {
        TextRenderer.DrawText(g, Label, Theme.Base, new Rectangle(0, 0, Width - 130, Height), Enabled ? Theme.Text : Theme.Faint, TextFormatFlags.VerticalCenter);
        var all = new RectangleF(Minus.X, 0.5f, Plus.Right - Minus.X, Height - 1);
        Theme.FillRound(g, Theme.Input, all, 6); Theme.StrokeRound(g, Theme.Border, all, 6);
        var p = PointToClient(MousePosition);
        foreach (var (r, s, can) in new[] { (Minus, "−", value > Min), (Plus, "+", value < Max) })
        {
            if (hover && r.Contains(p) && can) Theme.FillRound(g, Theme.Raised, r, 6);
            TextRenderer.DrawText(g, s, Theme.CardTitle, Rectangle.Round(r), can && Enabled ? Theme.Text : Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        TextRenderer.DrawText(g, Format(value), Theme.Semi, new Rectangle((int)Minus.Right, 0, (int)(Plus.X - Minus.Right), Height), Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// An on/off switch with a label.
public sealed class Toggle : Owned
{
    bool on;
    public event Action<bool>? Changed;
    public Toggle(string text) { Text = text; Height = 26; Cursor = Cursors.Hand; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked { get => on; set { if (on == value) return; on = value; Invalidate(); Changed?.Invoke(value); } }

    protected override void OnMouseClick(MouseEventArgs e) { if (Enabled) Checked = !Checked; base.OnMouseClick(e); }

    protected override void Draw(Graphics g)
    {
        var t = new RectangleF(0.5f, (Height - 20) / 2f, 36, 20);
        Theme.FillRound(g, on && Enabled ? Theme.Accent : Theme.Raised, t, 10);
        if (!on) Theme.StrokeRound(g, Theme.BorderHi, t, 10);
        float kx = on ? t.Right - 17 : t.X + 3;
        using (var b = new SolidBrush(on ? Theme.AccentText : Theme.Muted)) g.FillEllipse(b, kx, t.Y + 3, 14, 14);
        TextRenderer.DrawText(g, Text, Theme.Base, new Rectangle(46, 0, Width - 46, Height), Enabled ? Theme.Text : Theme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// A clickable header that shows or hides a section below it.
public sealed class Expander : Owned
{
    bool open;
    public string Sub { get; set; }
    public Control? Target { get; set; }

    public Expander(string text, string sub) { Text = text; Sub = sub; Height = 54; Cursor = Cursors.Hand; }

    public bool Open { get => open; set { open = value; if (Target != null) Target.Visible = value; Invalidate(); } }

    protected override void OnMouseClick(MouseEventArgs e) { Open = !Open; base.OnMouseClick(e); }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, hover ? Theme.Raised : Theme.Panel, r, 10);
        Theme.StrokeRound(g, Theme.Border, r, 10);
        Glyphs.Chevron(g, new RectangleF(12, (Height - 20) / 2f, 20, 20), Theme.Muted, open);
        TextRenderer.DrawText(g, Text, Theme.Semi, new Point(40, 9), Theme.Text);
        TextRenderer.DrawText(g, Sub, Theme.Small, new Point(40, 29), Theme.Faint);
    }
}

/// Steam connection indicator for the header.
public sealed class StatusPill : Owned
{
    public bool Good { get; set; }
    public StatusPill() { Height = 30; Font = Theme.Small; }

    public void Set(string text, bool good)
    {
        Text = text; Good = good;
        Width = TextRenderer.MeasureText(text, Font).Width + 38;
        Invalidate();
        Parent?.PerformLayout();
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, Theme.Panel, r, Height / 2f);
        Theme.StrokeRound(g, Theme.Border, r, Height / 2f);
        var c = Good ? Theme.Accent : Theme.Warn;
        using (var halo = new SolidBrush(Color.FromArgb(60, c))) g.FillEllipse(halo, 9, Height / 2f - 7, 14, 14);
        using (var b = new SolidBrush(c)) g.FillEllipse(b, 12, Height / 2f - 4, 8, 8);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(28, 0, Width - 30, Height), Theme.Text, TextFormatFlags.VerticalCenter);
    }
}

/// The big preview: a drop zone when empty, the image (on a checkerboard for transparency) when filled.
public sealed class PreviewBox : Owned
{
    Image? image;
    bool dragOver;
    public string Badge { get; set; } = "";
    public bool Busy { get; set; }
    public event Action<string>? FileDropped;
    public event Action<string>? TextDropped;    // e.g. an image dragged from a browser gives its link
    public event Action<Image>? ImageDropped;
    public event Action? Browse;

    public PreviewBox()
    {
        AllowDrop = true; Cursor = Cursors.Hand;
        DragEnter += (_, e) =>
        {
            var d = e.Data;
            if (d != null && (d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent(DataFormats.UnicodeText) || d.GetDataPresent(DataFormats.Text) || d.GetDataPresent(DataFormats.Bitmap)))
            { e.Effect = DragDropEffects.Copy; dragOver = true; Invalidate(); }
        };
        DragLeave += (_, _) => { dragOver = false; Invalidate(); };
        DragDrop += (_, e) =>
        {
            dragOver = false; Invalidate();
            var d = e.Data; if (d == null) return;
            // a browser drag carries a link; prefer it over its temporary file
            if ((d.GetData(DataFormats.UnicodeText) ?? d.GetData(DataFormats.Text)) is string t && t.Trim().StartsWith("http", StringComparison.OrdinalIgnoreCase)) TextDropped?.Invoke(t.Trim().Split('\n')[0].Trim());
            else if (d.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) FileDropped?.Invoke(f[0]);
            else if (d.GetData(DataFormats.Bitmap) is Image img) ImageDropped?.Invoke(img);
            else if ((d.GetData(DataFormats.UnicodeText) ?? d.GetData(DataFormats.Text)) is string t2) TextDropped?.Invoke(t2.Trim());
        };
        Click += (_, _) => Browse?.Invoke();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Image { get => image; set { var old = image; image = value; if (!ReferenceEquals(old, value)) old?.Dispose(); Cursor = value == null ? Cursors.Hand : Cursors.Default; Invalidate(); } }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, Theme.Panel, r, 12);

        if (image == null)
        {
            var inner = RectangleF.Inflate(r, -14, -14);
            Theme.FillRound(g, dragOver ? Theme.AccentDim : Theme.Input, inner, 10);
            using (var pen = new Pen(dragOver || hover ? Theme.Accent : Theme.BorderHi, 1.5f) { DashPattern = new[] { 5f, 4f } })
            using (var p = Theme.Round(inner, 10)) g.DrawPath(pen, p);
            float cx = Width / 2f, cy = Height / 2f;
            Glyphs.Upload(g, new RectangleF(cx - 22, cy - 62, 44, 44), dragOver || hover ? Theme.Accent : Theme.Muted, 2.4f);
            var big = "Drop an image here";
            var sz = TextRenderer.MeasureText(big, Theme.CardTitle);
            TextRenderer.DrawText(g, big, Theme.CardTitle, new Point((int)(cx - sz.Width / 2f), (int)cy - 4), Theme.Text);
            var small = "or click to browse  ·  drag one from your browser  ·  Ctrl+V a link or image";
            sz = TextRenderer.MeasureText(small, Theme.Small);
            TextRenderer.DrawText(g, small, Theme.Small, new Point((int)(cx - sz.Width / 2f), (int)cy + 22), Theme.Muted);
            Theme.StrokeRound(g, Theme.Border, r, 12);
            return;
        }

        // fit the image, square-ish, centred
        float pad = 16, avail = Math.Min(Width - pad * 2, Height - pad * 2);
        float scale = Math.Min(avail / image.Width, avail / image.Height);
        float w = image.Width * scale, h = image.Height * scale;
        var dst = new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
        using (var clip = Theme.Round(dst, 6))
        {
            var state = g.Save();
            g.SetClip(clip);
            DrawChecker(g, dst);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, dst);
            g.Restore(state);
        }
        Theme.StrokeRound(g, Theme.Border, dst, 6);

        if (!string.IsNullOrEmpty(Badge) || Busy)
        {
            string text = Busy ? "Loading…" : Badge;
            var sz = TextRenderer.MeasureText(text, Theme.Small);
            var b = new RectangleF(dst.X + 10, dst.Y + 10, sz.Width + 18, 24);
            Theme.FillRound(g, Color.FromArgb(200, 16, 17, 19), b, 12);
            TextRenderer.DrawText(g, text, Theme.Small, Rectangle.Round(b), Busy ? Theme.Warn : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        Theme.StrokeRound(g, dragOver ? Theme.Accent : Theme.Border, r, 12, dragOver ? 2 : 1);
    }

    static void DrawChecker(Graphics g, RectangleF r)
    {
        using var a = new SolidBrush(Theme.Hex(0x2a2c2f));
        using var b = new SolidBrush(Theme.Hex(0x222427));
        g.FillRectangle(a, r);
        const float s = 12;
        for (float y = r.Y; y < r.Bottom; y += s)
            for (float x = r.X + (((int)((y - r.Y) / s)) % 2 == 0 ? 0 : s); x < r.Right; x += s * 2)
                g.FillRectangle(b, x, y, s, s);
    }
}

/// Shows the uploaded item's ID with quick actions.
public sealed class ResultCard : Panel
{
    readonly Label title = new() { AutoSize = true, Font = Theme.Semi, ForeColor = Theme.Text, BackColor = Theme.AccentDim };
    readonly Label id = new() { AutoSize = true, Font = Theme.MonoBig, ForeColor = Theme.Accent, BackColor = Theme.AccentDim };
    public readonly FlatButton CopyId = new("Copy ID");
    public readonly FlatButton CopyCommand = new("Copy add command");
    public readonly FlatButton OpenPage = new("Open Workshop page");

    public ResultCard()
    {
        DoubleBuffered = true; BackColor = Theme.AccentDim; Height = 64; Visible = false;
        Controls.AddRange(new Control[] { title, id, CopyId, CopyCommand, OpenPage });
        foreach (var b in new[] { CopyId, CopyCommand, OpenPage }) b.Height = 32;
    }

    public void Show(string heading, ulong itemId, bool canAdd)
    {
        title.Text = heading; id.Text = itemId.ToString(); CopyCommand.Visible = canAdd; Visible = true; PerformLayout(); Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        title.Location = new Point(52, 10);
        id.Location = new Point(50, 31);
        int x = Width - 14;
        foreach (var b in new[] { OpenPage, CopyCommand, CopyId })
        {
            if (!b.Visible) continue;
            x -= b.Width; b.Location = new Point(x, (Height - b.Height) / 2); x -= 8;
        }
        base.OnLayout(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Parent?.BackColor ?? Theme.Bg); Theme.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, Theme.AccentDim, r, 10);
        Theme.StrokeRound(g, Theme.Mix(Theme.AccentDim, Theme.Accent, 0.5f), r, 10);
        using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, 14, Height / 2f - 13, 26, 26);
        Glyphs.Check(g, new RectangleF(18, Height / 2f - 9, 18, 18), Theme.AccentText, 2.4f);
    }
}

/// Thin progress line.
public sealed class ProgressLine : Owned
{
    float value;
    public ProgressLine() { Height = 4; }
    public float Value { get => value; set { this.value = Math.Clamp(value, 0, 1); Invalidate(); } }
    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, Theme.Raised, r, 2);
        if (value > 0) Theme.FillRound(g, Theme.Accent, new RectangleF(0, 0, Math.Max(4, Width * value), Height), 2);
    }
}
