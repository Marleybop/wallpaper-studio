namespace WallpaperStudioUploader;

/// Base for the app's small windows: themed, centred on the main window, Esc closes.
public class ThemedDialog : Form
{
    protected readonly Surface Body = new(Surface.Fill.Surface) { Dock = DockStyle.Fill, Padding = new Padding(24) };

    public ThemedDialog(string title, int w, int h)
    {
        Text = title; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(w, h); Font = Theme.Body(); KeyPreview = true;
        Controls.Add(Body);
        HandleCreated += (_, _) => Theme.TitleBar(this);
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }

    protected FlatButton AddButtons(string okText, ButtonKind okKind, Action? onOk = null)
    {
        var ok = new FlatButton(okText, okKind, 40);
        var cancel = new FlatButton("Cancel", ButtonKind.Secondary, 40);
        ok.Location = new Point(ClientSize.Width - 24 - ok.Width, ClientSize.Height - 24 - ok.Height);
        cancel.Location = new Point(ok.Left - 8 - cancel.Width, ok.Top);
        ok.Click += (_, _) => { onOk?.Invoke(); DialogResult = DialogResult.OK; Close(); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && ActiveControl is not TextBoxBase { Multiline: true }) { e.Handled = true; onOk?.Invoke(); DialogResult = DialogResult.OK; Close(); } };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Body.Controls.Add(ok); Body.Controls.Add(cancel);
        return ok;
    }
}

/// "Are you sure?" with a heading, a few label/value rows, optional extra text and an optional tick box.
public sealed class ConfirmDialog : ThemedDialog
{
    public readonly CheckRow? Check;

    ConfirmDialog(string heading, (string k, string v)[] rows, string? note, string okText, ButtonKind kind, string? check, bool checkOn)
        : base("Confirm", 480, 150 + rows.Length * 28 + (note != null ? 52 : 0) + (check != null ? 56 : 0))
    {
        int y = 22;
        Body.Controls.Add(new TextLabel(heading, Theme.Display(13f)) { Bounds = new Rectangle(24, y, 432, 28) }); y += 40;
        foreach (var (k, v) in rows)
        {
            Body.Controls.Add(new TextLabel(k, Theme.Body(9.5f), TextRole.Text2) { Bounds = new Rectangle(24, y, 120, 24) });
            Body.Controls.Add(new TextLabel(v, Theme.Semi(9.5f)) { Bounds = new Rectangle(150, y, 306, 24) });
            y += 28;
        }
        if (note != null) { var n = new TextLabel(note, Theme.Body(9.25f), TextRole.Text2) { Wrap = true, Bounds = new Rectangle(24, y + 4, 432, 40) }; Body.Controls.Add(n); y += 52; }
        if (check != null) { Check = new CheckRow(check) { Bounds = new Rectangle(24, y + 4, 432, 44) }; Check.SetQuiet(checkOn); Body.Controls.Add(Check); }
        AddButtons(okText, kind);
    }

    public static bool Ask(IWin32Window owner, string heading, (string, string)[] rows, string okText, out bool ticked,
        string? note = null, ButtonKind kind = ButtonKind.Primary, string? check = null, bool checkOn = false)
    {
        using var d = new ConfirmDialog(heading, rows, note, okText, kind, check, checkOn);
        bool ok = d.ShowDialog(owner) == DialogResult.OK;
        ticked = d.Check?.Checked ?? false;
        return ok;
    }
}

/// Appearance and your Wallpaper Studio server.
public sealed class SettingsDialog : ThemedDialog
{
    public SettingsDialog(Settings s) : base("Settings", 520, 600)
    {
        int y = 22;
        Body.Controls.Add(new TextLabel("Settings", Theme.Display(14f)) { Bounds = new Rectangle(24, y, 460, 30) }); y += 46;

        Body.Controls.Add(new TextLabel("Appearance", Theme.Semi(9.5f), TextRole.Text2) { Bounds = new Rectangle(24, y, 460, 20) }); y += 26;
        var theme = new Segmented("Follow Windows", "Dark", "Light") { Bounds = new Rectangle(24, y, 472, 38) };
        theme.SetQuiet(s.Theme switch { "dark" => 1, "light" => 2, _ => 0 });
        theme.Changed += i => { s.Theme = i switch { 1 => "dark", 2 => "light", _ => "system" }; Theme.Apply(s.Theme); Theme.TitleBar(this); };
        Body.Controls.Add(theme); y += 58;

        Body.Controls.Add(new TextLabel("Wallpaper Studio server (optional)", Theme.Semi(9.5f), TextRole.Text2) { Bounds = new Rectangle(24, y, 460, 20) }); y += 24;
        var about = new TextLabel("Lets the app add wallpapers to your server when you save, and remove them when you delete. Uses the server's RCON (rcon.web on).", Theme.Body(9f), TextRole.Text2) { Wrap = true };
        Body.Controls.Add(about);
        about.Bounds = new Rectangle(24, y, 472, 20);   // Wrap grows the height to fit once the width is set
        y = about.Bottom + 14;

        Field Labeled(string label, string text, string ph, int x, int w, ref int yy, bool password = false)
        {
            Body.Controls.Add(new TextLabel(label, Theme.Medium(9f), TextRole.Text2) { Bounds = new Rectangle(x, yy, w, 18) });
            var f = new Field(ph) { Bounds = new Rectangle(x, yy + 20, w, 40), Text = text };
            f.Box.UseSystemPasswordChar = password;
            Body.Controls.Add(f);
            return f;
        }
        int y2 = y;
        var host = Labeled("Address", s.RconHost, "127.0.0.1", 24, 340, ref y);
        var port = Labeled("RCON port", s.RconPort.ToString(), "28016", 376, 120, ref y2);
        y += 72;
        var pw = Labeled("RCON password", s.RconPassword, "rcon.password", 24, 472, ref y, true); y += 72;
        var col = Labeled("Collection for new wallpapers", s.Collection, "Optional, e.g. Brick", 24, 472, ref y); y += 72;
        var add = new CheckRow("Add new wallpapers to the server when I save") { Bounds = new Rectangle(24, y, 472, 44) };
        add.SetQuiet(s.AddToServerAfterUpload);
        Body.Controls.Add(add);

        AddButtons("Save", ButtonKind.Primary, () =>
        {
            s.RconHost = host.Text.Trim();
            if (int.TryParse(port.Text.Trim(), out int p) && p > 0 && p < 65536) s.RconPort = p;
            s.RconPassword = pw.Text;
            s.Collection = col.Text.Trim();
            s.AddToServerAfterUpload = add.Checked;
            try { s.Save(); } catch { }
        });
    }
}
