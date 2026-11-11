using System.Diagnostics;

namespace WallpaperStudioUploader;

public sealed class MainForm : Form
{
    readonly Settings settings;

    // ---- layout ----
    readonly Surface header = new(Surface.Fill.Surface, AnchorStyles.Bottom);
    readonly Surface sidebar = new(Surface.Fill.Bg, AnchorStyles.Right);
    readonly Surface titleBar = new(Surface.Fill.Surface, AnchorStyles.Bottom);
    readonly Surface content = new(Surface.Fill.Bg);

    // header
    readonly FlatButton themeButton = new("", ButtonKind.Ghost, 36);
    readonly FlatButton settingsButton = new("Settings", ButtonKind.Ghost, 36);
    readonly TextLabel steamLabel = new("Connecting to Steam…", Theme.Body(9.25f), TextRole.Text2) { Align = ContentAlignment.MiddleRight };

    // sidebar
    readonly FlatButton newButton = new("+  New wallpaper or spray", ButtonKind.Primary, 44);
    readonly Field search = new("Search your uploads");
    readonly Chips chips = new();
    readonly Surface listHost = new(Surface.Fill.Bg) { AutoScroll = true };
    readonly TextLabel listNote = new("", Theme.Body(9f), TextRole.Text2) { Wrap = true };
    readonly FlatButton refreshButton = new("Refresh", ButtonKind.Ghost, 30);

    // title bar
    readonly TextLabel titleText = new("New item", Theme.Display(16f));
    readonly TextLabel idChip = new("", Theme.Mono(9f), TextRole.Text2);
    readonly FlatButton copyId = new("Copy ID");
    readonly FlatButton copyCmd = new("Copy add command");
    readonly FlatButton openPage = new("Open Workshop page");
    readonly FlatButton deleteButton = new("Delete…", ButtonKind.Danger);

    // editor (left)
    readonly ImageStage stage = new();
    readonly Segmented tabs = new("Crop", "Adjust", "Look", "Upscale", "Seamless");
    readonly Surface toolHost = new(Surface.Fill.Bg);
    readonly FlatButton browse = new("Choose image…");
    readonly Field linkField = new("…or paste an image link");
    readonly FlatButton getLink = new("Get");

    // tools
    readonly CheckRow fitWhole = new("Keep the whole image (no crop)");
    readonly Slider zoom = new("Zoom", 100, 800, 100) { Format = v => $"{v}%" };
    readonly FlatButton resetCrop = new("Reset crop", ButtonKind.Ghost, 32);
    readonly Slider bright = new("Brightness", -100, 100, 0), contrast = new("Contrast", -100, 100, 0),
        sat = new("Saturation", -100, 100, 0), warm = new("Warmth", -100, 100, 0), sharp = new("Sharpen", 0, 100, 0) { Format = v => v.ToString() };
    readonly FlatButton resetAdjust = new("Reset", ButtonKind.Ghost, 32);
    readonly Segmented size = new("2048 px · best", "1024 px · smaller");
    readonly Slider repeat = new("Repeat", 1, 8, 1) { Format = v => v == 1 ? "Off" : $"{v}×{v}" };
    readonly Slider gloss = new("Shine", 0, 100, 25) { Format = v => v < 15 ? "Matte" : v > 70 ? "Glossy" : $"{v}%" };
    readonly Slider bump = new("Surface depth", 0, 200, 100) { Format = v => v == 0 ? "Flat" : $"{v}%" };
    readonly TextLabel upscaleText = new("", Theme.Body(9.5f), TextRole.Text2) { Wrap = true };
    readonly FlatButton upscaleButton = new("Upscale 4× with AI", ButtonKind.Secondary, 40);
    readonly FlatButton undoUpscale = new("Use the original instead", ButtonKind.Ghost, 36);
    readonly Slider seam = new("Blend edges", 0, 100, 0) { Format = v => v == 0 ? "Off" : $"{v}%" };
    readonly TextLabel seamText = new("Makes a pattern tile without visible seams by blending its edges. Check the 3 × 3 view: edges should flow into each other.", Theme.Body(9.25f), TextRole.Text2) { Wrap = true };
    readonly TextLabel lookText = new("", Theme.Body(9.25f), TextRole.Text2) { Wrap = true };

    // details (right)
    readonly Segmented kind = new("Wall", "Floor", "Ceiling", "Spray");
    readonly Field title = new("Shown on the Workshop page");
    readonly Field description = new("Optional", multiline: true, height: 84);
    readonly Segmented visibility = new("Public", "Unlisted", "Friends", "Hidden");
    readonly TextLabel visHint = new("", Theme.Body(9f), TextRole.Text2) { Wrap = true };
    readonly CheckRow serverCheck = new("Add it to my server when I save");
    readonly TextLabel status = new("", Theme.Body(9.25f), TextRole.Text2) { Wrap = true };
    readonly ProgressLine progress = new();
    readonly FlatButton saveButton = new("Save to Workshop", ButtonKind.Primary, 48);

    // ---- state ----
    readonly List<LibItem> items = new();
    readonly List<ItemRow> rows = new();
    LibItem? current;                 // null = new item
    EditState state = new();
    string savedSnapshot = "";
    string? sourcePath;
    Bitmap? sourcePreview;
    int renderGen;
    bool busy, loading;
    readonly System.Windows.Forms.Timer renderTimer = new() { Interval = 90 };

    public MainForm(Settings settings, string? initialImage = null)
    {
        this.settings = settings;
        Text = "Wallpaper Studio Uploader";
        Icon = Theme.AppIcon();
        ClientSize = new Size(1320, 860); MinimumSize = new Size(1120, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.Body(); KeyPreview = true; DoubleBuffered = true;
        BackColor = Theme.P.Bg;

        Build();
        Wire();
        Theme.Changed += OnThemeChanged;
        OnThemeChanged();
        NewItem(force: true);
        Shown += async (_, _) =>
        {
            Theme.TitleBar(this);
            await ConnectSteam();
            if (initialImage != null) { if (ImageFetcher.IsUrl(initialImage, out var u)) await FetchUrl(u); else SetImage(initialImage); }
        };
        FormClosing += (sender, e) =>
        {
            if (Dirty && !ConfirmDialog.Ask(this, "Close without saving?", Array.Empty<(string, string)>(), "Close", out _,
                "Your changes to this item haven't been uploaded yet.", ButtonKind.Danger)) { e.Cancel = true; return; }
            try { settings.Save(); } catch { }
            SteamWorkshop.Disconnect();
        };
    }

    bool Dirty => state.Json() != savedSnapshot && (sourcePath != null || current != null);
    bool IsSpray => state.Kind == ItemKind.Spray;

    // =============== building the window ===============

    void Build()
    {
        Controls.AddRange(new Control[] { header, sidebar, titleBar, content });
        header.Paint += PaintHeader;
        header.Controls.AddRange(new Control[] { steamLabel, themeButton, settingsButton });
        themeButton.Width = 40;
        themeButton.Icon = (g, r, c) => DrawThemeIcon(g, r, c);

        sidebar.Controls.AddRange(new Control[] { newButton, search, chips, listHost, listNote, refreshButton });
        Theme.Scrollbars(listHost);

        titleBar.Controls.AddRange(new Control[] { titleText, idChip, copyId, copyCmd, openPage, deleteButton });
        idChip.Paint += (_, e) => { };

        content.Controls.AddRange(new Control[] { stage, tabs, toolHost, browse, linkField, getLink,
            kind, title, description, visibility, visHint, serverCheck, status, progress, saveButton });
        foreach (var (text, y) in new[] { ("Type", 0), ("Title", 1), ("Description", 2), ("Visibility on Steam", 3) })
            content.Controls.Add(new TextLabel(text, Theme.Semi(9.25f), TextRole.Text2) { Name = "cap" + y });

        toolHost.Controls.AddRange(new Control[] { fitWhole, zoom, resetCrop, bright, contrast, sat, warm, sharp, resetAdjust,
            size, repeat, gloss, bump, lookText, upscaleText, upscaleButton, undoUpscale, seam, seamText });
        toolHost.Controls.Add(new TextLabel("Texture size", Theme.Semi(9.25f), TextRole.Text2) { Name = "capSize" });
        toolHost.Controls.Add(new TextLabel("Drag the square on the image to choose which part is used, and scroll to zoom.", Theme.Body(9.25f), TextRole.Text2) { Wrap = true, Name = "cropText" });
        built = true;
    }

    bool built;

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (!built) return;   // setting the window size lays out before the controls exist
        int W = ClientSize.Width, H = ClientSize.Height;
        const int headH = 60, sideW = 330, titleH = 72;
        header.SetBounds(0, 0, W, headH);
        settingsButton.Location = new Point(W - 16 - settingsButton.Width, (headH - 36) / 2);
        themeButton.Location = new Point(settingsButton.Left - 6 - themeButton.Width, settingsButton.Top);
        int slw = TextRenderer.MeasureText(steamLabel.Text, steamLabel.Font, Size.Empty, TextFormatFlags.NoPadding).Width + 6;
        steamLabel.SetBounds(themeButton.Left - 18 - slw, 12, slw, 36);

        sidebar.SetBounds(0, headH, sideW, H - headH);
        int sw = sideW - 32;
        newButton.SetBounds(16, 16, sw, 44);
        search.SetBounds(16, 70, sw, 40);
        chips.SetBounds(16, 120, sw, chips.Height);
        int ly = chips.Bottom + 10;
        refreshButton.Location = new Point(16, sidebar.Height - 12 - refreshButton.Height);
        listNote.SetBounds(16, ly, sw, listNote.Height);
        int listTop = listNote.Visible && listNote.Text.Length > 0 ? listNote.Bottom + 8 : ly;
        listHost.SetBounds(8, listTop, sideW - 12, refreshButton.Top - 8 - listTop);
        LayoutRows();

        int mx = sideW, mw = W - sideW;
        titleBar.SetBounds(mx, headH, mw, titleH);
        int bx = mw - 24;
        foreach (var b in new[] { deleteButton, openPage, copyCmd, copyId })
        {
            if (!b.Visible) continue;
            bx -= b.Width; b.Location = new Point(bx, (titleH - b.Height) / 2); bx -= 8;
        }
        int room = Math.Max(80, bx - 24 - 12 - (idChip.Text.Length > 0 ? 130 : 0));
        int tw = Math.Min(room, TextRenderer.MeasureText(titleText.Text, titleText.Font, Size.Empty, TextFormatFlags.NoPadding).Width + 6);
        titleText.SetBounds(24, 1, tw, titleH - 3);
        idChip.SetBounds(titleText.Right + 12, (titleH - 26) / 2 + 2, 130, 26);

        content.SetBounds(mx, headH + titleH, mw, H - headH - titleH);
        LayoutContent();
    }

    void LayoutContent()
    {
        int W = content.Width, H = content.Height, pad = 24;
        int rightW = Math.Clamp((W - pad * 3) * 38 / 100, 300, 400);
        int leftW = W - pad * 3 - rightW;
        int toolH = 196;
        int stageH = H - pad * 2 - 44 - 12 - toolH - 12 - 48;
        stage.SetBounds(pad, pad, leftW, Math.Max(200, stageH));
        int y = stage.Bottom + 12;
        browse.Location = new Point(pad, y + 2);
        getLink.Location = new Point(pad + leftW - getLink.Width, y + 2);
        linkField.SetBounds(browse.Right + 10, y, getLink.Left - 10 - browse.Right - 10, 40);
        y += 52;
        tabs.SetBounds(pad, y, leftW, 40);
        toolHost.SetBounds(pad, tabs.Bottom + 14, leftW, H - pad - tabs.Bottom - 14);
        LayoutTools();

        int rx = pad * 2 + leftW;
        int ry = pad;
        Control Cap(int i) => content.Controls["cap" + i]!;
        Cap(0).SetBounds(rx, ry, rightW, 20); ry += 24;
        kind.SetBounds(rx, ry, rightW, 40); ry += 54;
        Cap(1).SetBounds(rx, ry, rightW, 20); ry += 24;
        title.SetBounds(rx, ry, rightW, 40); ry += 54;
        Cap(2).SetBounds(rx, ry, rightW, 20); ry += 24;
        description.SetBounds(rx, ry, rightW, 84); ry += 98;
        Cap(3).SetBounds(rx, ry, rightW, 20); ry += 24;
        visibility.SetBounds(rx, ry, rightW, 40); ry += 46;
        visHint.SetBounds(rx, ry, rightW, visHint.Height); ry = visHint.Bottom + 12;
        serverCheck.SetBounds(rx, ry, rightW, 44);
        saveButton.SetBounds(rx, H - pad - 48, rightW, 48);
        progress.SetBounds(rx, saveButton.Top - 10, rightW, 3);
        status.SetBounds(rx, progress.Top - 8 - status.Height, rightW, status.Height);
    }

    void LayoutTools()
    {
        int w = toolHost.Width, half = (w - 24) / 2;
        foreach (Control c in toolHost.Controls) c.Visible = false;
        void Show(Control c, int x, int y, int cw = -1, int h = -1) { c.Visible = true; c.SetBounds(x, y, cw < 0 ? c.Width : cw, h < 0 ? c.Height : h); }
        switch (tabs.SelectedIndex)
        {
            case 0:
                if (IsSpray) { Show(fitWhole, 0, 0, w); }
                if (!(IsSpray && state.FitWhole))
                {
                    int y0 = IsSpray ? 56 : 0;
                    Show(toolHost.Controls["cropText"]!, 0, y0, w);
                    Show(zoom, 0, y0 + 40, half);
                    Show(resetCrop, w - resetCrop.Width, y0 + 48);
                }
                break;
            case 1:
                Show(bright, 0, 0, half); Show(contrast, half + 24, 0, half);
                Show(sat, 0, 56, half); Show(warm, half + 24, 56, half);
                Show(sharp, 0, 112, half); Show(resetAdjust, w - resetAdjust.Width, 122);
                break;
            case 2:
                Show(toolHost.Controls["capSize"]!, 0, 0, w, 20); Show(size, 0, 24, w, 38);
                if (IsSpray) { lookText.Text = "Sprays only use the picture's colours. Transparent areas stay clear."; Show(lookText, 0, 76, w); }
                else { Show(repeat, 0, 76, half); Show(gloss, half + 24, 76, half); Show(bump, 0, 132, half); }
                break;
            case 3:
                Show(upscaleText, 0, 0, w);
                Show(upscaleButton, 0, upscaleText.Bottom + 12);
                if (state.Upscaled) Show(undoUpscale, upscaleButton.Right + 10, upscaleText.Bottom + 14);
                break;
            case 4:
                if (IsSpray) { seamText.Text = "Sprays don't tile, so there's nothing to blend."; Show(seamText, 0, 0, w); }
                else { seamText.Text = "Makes a pattern tile without visible seams by blending its edges. Check the 3 × 3 view: edges should flow into each other."; Show(seam, 0, 0, half); Show(seamText, 0, 60, w); }
                break;
        }
    }

    // =============== painting bits ===============

    void PaintHeader(object? s, PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        Theme.UploaderIcon(g, new RectangleF(18, 15, 30, 30), Theme.P.Dark);
        TextRenderer.DrawText(g, "Wallpaper Studio Uploader", Theme.Display(13.5f), new Point(58, 17), Theme.P.Text, TextFormatFlags.NoPadding);
        var w = TextRenderer.MeasureText("Wallpaper Studio Uploader", Theme.Display(13.5f), Size.Empty, TextFormatFlags.NoPadding).Width;
        TextRenderer.DrawText(g, "by Marleybop", Theme.Body(9.25f), new Point(58 + w + 10, 22), Theme.P.Text2, TextFormatFlags.NoPadding);
        // Steam dot
        var dot = new RectangleF(steamLabel.Left - 14, 26, 8, 8);
        using var b = new SolidBrush(SteamWorkshop.Ready ? Theme.P.Ok : Theme.P.Muted);
        g.FillEllipse(b, dot);
    }

    static void DrawThemeIcon(Graphics g, RectangleF r, Color c)
    {
        using var pen = new Pen(c, 1.8f);
        if (Theme.P.Dark)   // dark mode on: a crescent moon
        {
            using var moon = new Region(new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2).ToPath());
            moon.Exclude(new RectangleF(r.X + 6, r.Y - 2, r.Width - 3, r.Height - 3).ToPath());
            using var b = new SolidBrush(c);
            g.FillRegion(b, moon);
        }
        else
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                g.DrawLine(pen, cx + (float)Math.Cos(a) * 6.5f, cy + (float)Math.Sin(a) * 6.5f, cx + (float)Math.Cos(a) * 8.5f, cy + (float)Math.Sin(a) * 8.5f);
            }
        }
    }

    readonly ToolTip tips = new();

    void OnThemeChanged()
    {
        tips.SetToolTip(themeButton, Theme.P.Dark ? "Dark mode (click for light)" : "Light mode (click for dark)");
        BackColor = Theme.P.Bg;
        Theme.TitleBar(this);
        Theme.Scrollbars(listHost);
        Invalidate(true);
    }

    // =============== events ===============
    // (ToPath helper for the moon icon is below, in RectExt)

    void Wire()
    {
        themeButton.Click += (_, _) =>
        {
            settings.Theme = Theme.P.Dark ? "light" : "dark";
            Theme.Apply(settings.Theme);
            try { settings.Save(); } catch { }
        };
        settingsButton.Click += (_, _) => { using var d = new SettingsDialog(settings); d.ShowDialog(this); UpdateServerCheck(); };
        newButton.Click += (_, _) => NewItem();
        refreshButton.Click += async (_, _) => await LoadLibrary();
        search.Box.TextChanged += (_, _) => RebuildList();
        chips.Changed += _ => RebuildList();

        copyId.Click += (_, _) => Copy(state.Id.ToString(), "ID copied.");
        copyCmd.Click += (_, _) => Copy(AddCommand(), "Add command copied. Paste it into the server console, or the Admin tab in game.");
        openPage.Click += (_, _) => Process.Start(new ProcessStartInfo($"https://steamcommunity.com/sharedfiles/filedetails/?id={state.Id}") { UseShellExecute = true });
        deleteButton.Click += async (_, _) => await DeleteCurrent();

        stage.FileDropped += SetImage;
        stage.TextDropped += t => { if (ImageFetcher.IsUrl(t, out var u)) _ = FetchUrl(u); };
        stage.ImageDropped += img => SetImage(ImageFetcher.SaveClipboardImage(img));
        stage.Browse += ChooseImage;
        stage.CropChanged += () => { zoom.SetQuiet((int)(state.Zoom * 100)); RequestRender(); UpdateDirty(); };
        browse.Click += (_, _) => ChooseImage();
        getLink.Click += async (_, _) => { if (ImageFetcher.IsUrl(linkField.Text, out var u)) await FetchUrl(u); else SetStatus("Paste a link that starts with https://", TextRole.Error); };
        linkField.Box.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (ImageFetcher.IsUrl(linkField.Text, out var u)) await FetchUrl(u); } };

        tabs.Changed += _ => { UpdateStageMode(); LayoutTools(); };
        fitWhole.Changed += v => { state.FitWhole = v; UpdateStageMode(); LayoutTools(); RequestRender(); UpdateDirty(); };
        zoom.Changed += v => { state.Zoom = v / 100f; stage.Invalidate(); RequestRender(); UpdateDirty(); };
        resetCrop.Click += (_, _) => { state.CropX = state.CropY = 0.5f; state.Zoom = 1; zoom.SetQuiet(100); stage.Invalidate(); RequestRender(); UpdateDirty(); };
        void Adj(Slider s, Action<int> set) => s.Changed += v => { set(v); RequestRender(); UpdateDirty(); };
        Adj(bright, v => state.Brightness = v); Adj(contrast, v => state.Contrast = v); Adj(sat, v => state.Saturation = v);
        Adj(warm, v => state.Warmth = v); Adj(sharp, v => state.Sharpen = v); Adj(seam, v => state.Seamless = v);
        resetAdjust.Click += (_, _) => { foreach (var s in new[] { bright, contrast, sat, warm, sharp }) s.Value = s.Default; };
        size.Changed += i => { state.Size = i == 1 ? 1024 : 2048; UpdateDirty(); };
        repeat.Changed += v => { state.Tile = v; stage.Invalidate(); UpdateDirty(); };
        gloss.Changed += v => { state.Gloss = v; UpdateDirty(); };
        bump.Changed += v => { state.Bump = v; UpdateDirty(); };
        upscaleButton.Click += async (_, _) => await Upscale();
        undoUpscale.Click += (_, _) => { state.Upscaled = false; ReloadSource(); LayoutTools(); UpdateUpscaleText(); UpdateDirty(); };

        kind.Changed += i => { state.Kind = (ItemKind)i; UpdateKind(); RequestRender(); UpdateDirty(); };
        title.Box.TextChanged += (_, _) => { state.Title = title.Text; UpdateDirty(); };
        description.Box.TextChanged += (_, _) => { state.Description = description.Text; UpdateDirty(); };
        visibility.Changed += i => { state.Visibility = (Visibility)i; UpdateVisHint(); UpdateDirty(); };
        saveButton.Click += async (_, _) => await Save();
        renderTimer.Tick += (_, _) => { renderTimer.Stop(); Render(); };

        KeyDown += OnKey;
    }

    void OnKey(object? s, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S) { e.Handled = e.SuppressKeyPress = true; _ = Save(); return; }
        if (e.Control && e.KeyCode == Keys.N) { e.Handled = e.SuppressKeyPress = true; NewItem(); return; }
        if (!(e.Control && e.KeyCode == Keys.V)) return;
        Control? c = ActiveControl;
        while (c is ContainerControl cc && cc.ActiveControl != null) c = cc.ActiveControl;
        if (c is TextBoxBase) return;
        try
        {
            if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList() is { Count: > 0 } f) { SetImage(f[0]!); e.Handled = true; }
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is Image img) { using (img) SetImage(ImageFetcher.SaveClipboardImage(img)); e.Handled = true; }
            else if (Clipboard.ContainsText() && ImageFetcher.IsUrl(Clipboard.GetText(), out var u)) { _ = FetchUrl(u); e.Handled = true; }
        }
        catch (Exception ex) { SetStatus("Couldn't paste that: " + ex.Message, TextRole.Error); }
    }

    // =============== Steam + library ===============

    async Task ConnectSteam()
    {
        steamLabel.Text = "Connecting to Steam…"; header.Invalidate();
        var err = await Task.Run(SteamWorkshop.Connect);
        if (err != null)
        {
            steamLabel.Text = "Steam not connected";
            listNote.Text = "Start Steam and log in with the account that owns Rust, then press Refresh.";
            SetStatus(err.Split('\n')[0], TextRole.Error);
        }
        else
        {
            steamLabel.Text = "Steam · " + SteamWorkshop.Name;
            await LoadLibrary();
        }
        header.Invalidate(); UpdateButtons(); PerformLayout();
    }

    async Task LoadLibrary()
    {
        if (!SteamWorkshop.Ready) { await ConnectSteam(); return; }
        listNote.Text = "Loading your Workshop items…"; PerformLayout();
        try
        {
            var list = await Library.LoadAsync();
            items.Clear(); items.AddRange(list);
            listNote.Text = items.Count == 0 ? "Nothing here yet. Press New to make your first wallpaper or spray." : "";
            RebuildList();
            _ = Library.LoadThumbsAsync(items, it => BeginInvoke(() => rows.FirstOrDefault(r => r.Item == it)?.Invalidate()));
        }
        catch (Exception e) { listNote.Text = "Couldn't load your items: " + e.Message; }
        PerformLayout();
    }

    void RebuildList()
    {
        string q = search.Text.Trim();
        var counts = new[]
        {
            ("All", items.Count), ("Walls", items.Count(i => i.Kind == ItemKind.Wall)), ("Floors", items.Count(i => i.Kind == ItemKind.Floor)),
            ("Ceilings", items.Count(i => i.Kind == ItemKind.Ceiling)), ("Sprays", items.Count(i => i.Kind == ItemKind.Spray)),
        };
        chips.SetItems(counts);
        var shown = items.Where(i => chips.SelectedIndex == 0 || (int)i.Kind == chips.SelectedIndex - 1)
                         .Where(i => q.Length == 0 || i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Id.ToString().Contains(q)).ToList();
        listHost.SuspendLayout();
        while (rows.Count > shown.Count) { var r = rows[^1]; rows.RemoveAt(rows.Count - 1); listHost.Controls.Remove(r); r.Dispose(); }
        while (rows.Count < shown.Count)
        {
            var r = new ItemRow();
            r.Click += async (_, _) => { if (r.Item != null) await OpenItem(r.Item); };
            rows.Add(r); listHost.Controls.Add(r);
        }
        for (int i = 0; i < shown.Count; i++) { rows[i].Bind(shown[i]); rows[i].Selected = current != null && shown[i].Id == current.Id; }
        listHost.ResumeLayout();
        LayoutRows();
    }

    void LayoutRows()
    {
        int w = listHost.ClientSize.Width - 8, y = 0;
        foreach (var r in rows) { r.SetBounds(8, y - listHost.VerticalScroll.Value, w, r.Height); y += r.Height; }
        listHost.AutoScrollMinSize = new Size(0, y);
    }

    void MarkSelected() { foreach (var r in rows) { r.Selected = current != null && r.Item?.Id == current.Id; r.Invalidate(); } }

    // =============== open / new ===============

    bool ConfirmDiscard() => !Dirty || ConfirmDialog.Ask(this, "Discard your changes?", Array.Empty<(string, string)>(), "Discard", out _,
        $"\"{(state.Title.Length > 0 ? state.Title : "This item")}\" has changes that haven't been uploaded.", ButtonKind.Danger);

    void NewItem(bool force = false)
    {
        if (!force && !ConfirmDiscard()) return;
        try { if (Directory.Exists(Library.Folder(0))) Directory.Delete(Library.Folder(0), true); } catch { }
        current = null;
        state = new EditState { Kind = chips.SelectedIndex is >= 1 and <= 4 ? (ItemKind)(chips.SelectedIndex - 1) : ItemKind.Wall, Visibility = settings.Visibility, Size = settings.Size };
        sourcePath = null;
        SetSourcePreview(null);
        LoadStateIntoControls();
        savedSnapshot = state.Json();
        tabs.SetQuiet(0);
        SetStatus("Choose an image to start: drop it on the left, browse, paste a link, or Ctrl+V.");
        MarkSelected(); UpdateButtons(); UpdateStageMode(); PerformLayout();
    }

    async Task OpenItem(LibItem item)
    {
        if (busy || (current?.Id == item.Id)) return;
        if (!ConfirmDiscard()) return;
        current = item;
        var saved = Library.LoadEdit(item.Id);
        state = saved ?? new EditState { Id = item.Id };
        state.Id = item.Id; state.Title = item.Title; state.Description = item.Description; state.Kind = item.Kind; state.Visibility = item.Visibility;
        LoadStateIntoControls();
        MarkSelected(); UpdateButtons();
        SetSourcePreview(null);
        sourcePath = null;
        SetBusy(true, "Loading…");
        try
        {
            var src = await Library.EnsureSourceAsync(item, new Progress<string>(m => SetStatus(m)));
            if (current != item) return;
            if (saved == null) state.SourceFile = Path.GetFileName(src);
            sourcePath = src;
            ReloadSource();
            SetStatus(saved == null ? "Loaded from Steam. Edit anything, then Save to Workshop." : "Loaded with your last edits.");
        }
        catch (Exception e) { SetStatus(e.Message, TextRole.Error); }
        finally { SetBusy(false); savedSnapshot = state.Json(); UpdateDirty(); PerformLayout(); }
    }

    void LoadStateIntoControls()
    {
        loading = true;
        kind.SetQuiet((int)state.Kind);
        title.Text = state.Title; description.Text = state.Description;
        visibility.SetQuiet((int)state.Visibility);
        fitWhole.SetQuiet(state.FitWhole); zoom.SetQuiet((int)(state.Zoom * 100));
        bright.SetQuiet(state.Brightness); contrast.SetQuiet(state.Contrast); sat.SetQuiet(state.Saturation);
        warm.SetQuiet(state.Warmth); sharp.SetQuiet(state.Sharpen); seam.SetQuiet(state.Seamless);
        size.SetQuiet(state.Size == 1024 ? 1 : 0); repeat.SetQuiet(state.Tile); gloss.SetQuiet(state.Gloss); bump.SetQuiet(state.Bump);
        loading = false;
        titleText.Text = current == null ? "New item" : (state.Title.Length > 0 ? state.Title : "Untitled");
        idChip.Text = current == null ? "" : state.Id.ToString();
        stage.State = state;
        UpdateKind(); UpdateVisHint(); UpdateServerCheck(); UpdateUpscaleText();
    }

    // =============== images ===============

    void ChooseImage()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose an image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
            InitialDirectory = Directory.Exists(settings.LastImageFolder) ? settings.LastImageFolder : "",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) SetImage(dlg.FileName);
    }

    void SetImage(string path) => SetImage(path, null);

    void SetImage(string path, string? url)
    {
        try { using var test = ImageOps.Load(path); }
        catch (Exception e) { SetStatus("Couldn't open that image: " + e.Message, TextRole.Error); return; }
        if (url == null && !path.Contains("WallpaperStudioUploader")) settings.LastImageFolder = Path.GetDirectoryName(path) ?? "";
        var dest = Library.AdoptSource(state.Id, path);
        state.SourceFile = Path.GetFileName(dest); state.SourceUrl = url;
        state.Upscaled = false; state.CropX = state.CropY = 0.5f; state.Zoom = 1;
        if (state.Title.Length == 0) title.Text = Tidy(Path.GetFileNameWithoutExtension(path));
        sourcePath = dest;
        if (url == null) linkField.Text = "";
        ReloadSource();
        UpdateUpscaleText(); LayoutTools(); UpdateDirty();
        SetStatus("Image loaded. Adjust it, then Save to Workshop.");
    }

    async Task FetchUrl(Uri uri)
    {
        SetBusy(true, "Downloading…");
        try
        {
            var (path, name) = await ImageFetcher.Download(uri);
            bool hadTitle = state.Title.Length > 0;
            linkField.Text = uri.ToString();
            SetImage(path, uri.ToString());
            if (!hadTitle) title.Text = Tidy(name);
        }
        catch (Exception e) { SetStatus(e.Message, TextRole.Error); }
        finally { SetBusy(false); }
    }

    static string Tidy(string s) => System.Text.RegularExpressions.Regex.Replace(s.Replace('_', ' ').Replace('-', ' '), @"\s+", " ").Trim();

    string? ActiveSourceFile()
    {
        if (sourcePath == null) return null;
        string up = Path.Combine(Path.GetDirectoryName(sourcePath)!, "source-upscaled.png");
        return state.Upscaled && File.Exists(up) ? up : sourcePath;
    }

    void ReloadSource()
    {
        var f = ActiveSourceFile();
        if (f == null) { SetSourcePreview(null); return; }
        try { using var full = ImageOps.Load(f); SetSourcePreview(ImageOps.Shrink(full, 1400)); }
        catch (Exception e) { SetStatus("Couldn't read the image: " + e.Message, TextRole.Error); }
    }

    void SetSourcePreview(Bitmap? b)
    {
        var old = sourcePreview; sourcePreview = b;
        stage.Source = b; stage.State = state;
        old?.Dispose();
        if (b == null) { var r = stage.Result; stage.Result = null; r?.Dispose(); stage.Invalidate(); }
        else Render();
        UpdateButtons();
    }

    void RequestRender() { if (!loading && sourcePreview != null) { renderTimer.Stop(); renderTimer.Start(); } }

    async void Render()
    {
        if (sourcePreview == null) return;
        int gen = ++renderGen;
        var src = new Bitmap(sourcePreview);
        var st = state.Clone();
        Bitmap img;
        try { img = await Task.Run(() => { using (src) return ImageOps.Render(src, st, 640); }); }
        catch (Exception e) { SetStatus("Preview failed: " + e.Message, TextRole.Error); return; }
        if (gen != renderGen) { img.Dispose(); return; }
        var old = stage.Result; stage.Result = img; old?.Dispose();
        stage.Invalidate();
    }

    async Task Upscale()
    {
        var src = sourcePath;
        if (src == null) { SetStatus("Choose an image first.", TextRole.Error); return; }
        using (var probe = Image.FromFile(src))
            if (Math.Max(probe.Width, probe.Height) > 3000)
            { SetStatus("This image is already big (over 3000 px), so upscaling won't add detail.", TextRole.Error); return; }
        SetBusy(true, "Upscaling…");
        try
        {
            if (!Upscaler.Installed)
            {
                if (!ConfirmDialog.Ask(this, "Download the AI upscaler?", new[] { ("Download", Upscaler.DownloadSize), ("From", "Real-ESRGAN on GitHub (free, open source)") },
                    "Download", out _, "It's downloaded once and reused. It needs a graphics card with Vulkan support, which nearly all recent ones have.")) return;
                SetStatus("Downloading the upscaler…");
                await Upscaler.InstallAsync(new Progress<float>(p => progress.Value = p), CancellationToken.None);
            }
            SetStatus("Upscaling 4× (this can take a minute for big images)…");
            string outFile = Path.Combine(Path.GetDirectoryName(src)!, "source-upscaled.png");
            await Upscaler.RunAsync(src, outFile, new Progress<float>(p => progress.Value = p), CancellationToken.None);
            state.Upscaled = true;
            ReloadSource();
            UpdateUpscaleText(); LayoutTools(); UpdateDirty();
            SetStatus("Upscaled. The sharper version is used from now on.", TextRole.Ok);
        }
        catch (Exception e) { SetStatus(e.Message, TextRole.Error); }
        finally { progress.Value = 0; SetBusy(false); }
    }

    // =============== save / delete ===============

    async Task Save()
    {
        if (busy) return;
        if (!SteamWorkshop.Ready) { SetStatus("Not connected to Steam. Start Steam, then press Refresh.", TextRole.Error); return; }
        if (sourcePath == null) { SetStatus("Choose an image first.", TextRole.Error); return; }
        if (string.IsNullOrWhiteSpace(state.Title)) { SetStatus("Give it a title first.", TextRole.Error); title.Box.Focus(); return; }

        bool isNew = current == null;
        var before = Library.LoadEdit(state.Id);
        bool imageChanged = isNew || before == null || before.ImageKey() != state.ImageKey() || before.Kind != state.Kind || before.Size != state.Size
                            || before.Tile != state.Tile || before.Gloss != state.Gloss || before.Bump != state.Bump;
        bool canAdd = !IsSpray && settings.HasServer;
        string[] visNames = { "Public", "Unlisted", "Friends only", "Hidden" };
        if (!ConfirmDialog.Ask(this, isNew ? "Upload a new Workshop item?" : "Save changes to the Workshop?",
            new[] { ("Title", state.Title), ("Type", Kinds.ItemType(state.Kind)), ("Visibility", visNames[(int)state.Visibility]),
                    ("Changes", isNew ? "New item" : imageChanged ? "Picture and details" : "Details only"), ("Steam account", SteamWorkshop.Name) },
            isNew ? "Upload" : "Save", out bool addToServer, check: canAdd ? "Add it to my server" : null, checkOn: canAdd && serverCheck.Checked))
            return;

        SetBusy(true, "Saving…");
        try
        {
            string folder = Library.Folder(state.Id), contentDir = Path.Combine(folder, "content");
            Bitmap? icon = null;
            if (imageChanged)
            {
                SetStatus("Making textures…"); progress.Value = 0.05f;
                var st = state.Clone(); var srcFile = ActiveSourceFile()!;
                var opts = new BuildOptions { Kind = st.Kind, Size = st.Size, Tile = st.Tile, Gloss = st.Gloss / 100f, Bump = st.Bump / 100f, AuthorId = SteamWorkshop.SteamId };
                icon = await Task.Run(() =>
                {
                    using var full = ImageOps.Load(srcFile);
                    using var final = ImageOps.Render(full, st, st.Size);
                    ItemBuilder.Build(final, contentDir, opts);
                    return ImageOps.Shrink(final, 96);
                });
            }
            progress.Value = 0.15f;
            SetStatus(isNew ? "Creating the Workshop item and uploading…" : "Uploading…");
            var res = await SteamWorkshop.Upload(new UploadRequest
            {
                ExistingId = state.Id, Title = state.Title.Trim(), Description = state.Description.Trim(), Kind = state.Kind,
                Visibility = state.Visibility, ContentFolder = imageChanged ? contentDir : null,
            }, new Progress<float>(p => progress.Value = 0.15f + 0.85f * p));
            if (!res.Ok) { SetStatus(res.Message, TextRole.Error); return; }

            if (isNew) { Library.Promote(res.Id); state.Id = res.Id; }
            Library.SaveEdit(state);
            savedSnapshot = state.Json();
            settings.Visibility = state.Visibility; settings.Size = state.Size;

            var item = items.FirstOrDefault(i => i.Id == state.Id);
            if (item == null) { item = new LibItem { Id = state.Id }; items.Insert(0, item); }
            item.Title = state.Title; item.Description = state.Description; item.Kind = state.Kind; item.Visibility = state.Visibility; item.Updated = DateTime.UtcNow;
            if (icon != null) { item.Thumb?.Dispose(); item.Thumb = icon; }
            current = item;
            RebuildList(); MarkSelected();
            titleText.Text = state.Title; idChip.Text = state.Id.ToString();
            UpdateButtons();

            string msg = isNew ? $"Uploaded as {state.Id}." : "Saved.";
            if (res.NeedsAgreement) msg += " Steam needs you to accept the Workshop agreement once: open the Workshop page.";
            try { Clipboard.SetText(IsSpray ? state.Id.ToString() : AddCommand()); msg += IsSpray ? " ID copied." : " Add command copied."; } catch { }
            SetStatus(msg + " Steam can take a few minutes to serve changes.", TextRole.Ok);
            if (addToServer) await ServerCommand(AddCommand(), "added to your server");
        }
        catch (Exception e) { SetStatus("Save failed: " + e.Message, TextRole.Error); }
        finally { progress.Value = 0; SetBusy(false); UpdateDirty(); PerformLayout(); }
    }

    async Task DeleteCurrent()
    {
        if (current == null || busy) return;
        bool canRemove = !IsSpray && settings.HasServer;
        if (!ConfirmDialog.Ask(this, "Delete this Workshop item?", new[] { ("Title", state.Title), ("ID", state.Id.ToString()) }, "Delete", out bool fromServer,
            "This permanently deletes it from the Steam Workshop. Anyone using it will see it go plain.", ButtonKind.Danger,
            canRemove ? "Also remove it from my server" : null, canRemove)) return;
        SetBusy(true, "Deleting…");
        try
        {
            ulong id = state.Id;
            if (!await Library.DeleteAsync(id)) { SetStatus("Steam didn't delete it. Try again in a minute.", TextRole.Error); return; }
            if (fromServer) await ServerCommand($"wpstudio.remove {id}", "removed from your server");
            items.RemoveAll(i => i.Id == id);
            savedSnapshot = state.Json();
            NewItem(force: true);
            RebuildList();
            SetStatus($"Deleted {id} from the Workshop." + (fromServer ? " Also removed from your server." : ""), TextRole.Ok);
        }
        catch (Exception e) { SetStatus("Delete failed: " + e.Message, TextRole.Error); }
        finally { SetBusy(false); }
    }

    string AddCommand()
    {
        string? pk = Kinds.PluginKind(state.Kind);
        return pk == null ? state.Id.ToString() : $"wpstudio.add {state.Id} {pk}" + (string.IsNullOrWhiteSpace(settings.Collection) ? "" : " " + settings.Collection.Trim());
    }

    async Task ServerCommand(string cmd, string what)
    {
        try
        {
            var reply = (await Rcon.Send(settings.RconHost, settings.RconPort, settings.RconPassword, cmd)).Trim();
            bool ok = reply.StartsWith("Added") || reply.StartsWith("Removed") || reply.StartsWith("Already");
            SetStatus(status.Text + (ok ? $" Also {what}." : $" Server said: {reply}"), ok ? TextRole.Ok : TextRole.Error);
        }
        catch (Exception e) { SetStatus(status.Text + " Server: " + e.Message, TextRole.Error); }
    }

    // =============== small updates ===============

    void UpdateKind()
    {
        bool spray = IsSpray;
        kind.SetQuiet((int)state.Kind);
        copyCmd.Text = spray ? "Copy add command" : "Copy add command";
        UpdateServerCheck(); UpdateStageMode(); LayoutTools(); UpdateButtons(); PerformLayout();
    }

    void UpdateVisHint()
    {
        visHint.Text = state.Visibility switch
        {
            Visibility.Public => "Anyone can find it on the Workshop.",
            Visibility.Unlisted => "Only people with the link can see it on Steam.",
            Visibility.FriendsOnly => "Only your Steam friends can see it on Steam.",
            _ => "Nobody can browse it, but it still works in game by its ID. Good for server-only wallpapers.",
        };
    }

    void UpdateServerCheck()
    {
        bool can = !IsSpray && settings.HasServer;
        serverCheck.Visible = can;
        serverCheck.SetQuiet(can && settings.AddToServerAfterUpload);
    }

    void UpdateUpscaleText()
    {
        string extra = Upscaler.Installed ? "" : $" The first time, it downloads the free Real-ESRGAN upscaler ({Upscaler.DownloadSize}).";
        upscaleText.Text = state.Upscaled
            ? "Using the AI-upscaled version of your image (4× the original size)."
            : "Small or blurry image? AI upscaling makes it 4× bigger and sharper before it's turned into a texture. Best for images under about 1000 px." + extra;
        upscaleButton.Text = state.Upscaled ? "Upscale again" : "Upscale 4× with AI";
        upscaleButton.FitWidth();
    }

    void UpdateStageMode()
    {
        stage.Mode = tabs.SelectedIndex switch { 0 => StageMode.Crop, 4 => StageMode.Tiled, _ => StageMode.Result };
        stage.ShowRepeat = tabs.SelectedIndex == 2;
        stage.Invalidate();
    }

    void UpdateButtons()
    {
        bool existing = current != null;
        copyId.Visible = openPage.Visible = deleteButton.Visible = existing;
        copyCmd.Visible = existing && !IsSpray;
        saveButton.Text = existing ? "Save to Workshop" : "Upload to Workshop";
        saveButton.Enabled = !busy && SteamWorkshop.Ready && sourcePath != null;
        newButton.Enabled = !busy;
        PerformLayout();
    }

    void UpdateDirty()
    {
        if (loading) return;
        titleText.Text = current == null ? "New item" : (state.Title.Length > 0 ? state.Title : "Untitled");
        if (Dirty && !busy && status.Role != TextRole.Error) SetStatus(current == null ? "Not uploaded yet." : "Unsaved changes.");
        PerformLayout();
    }

    void SetBusy(bool b, string what = "")
    {
        busy = b;
        stage.Busy = b ? what : "";
        stage.Invalidate();
        UseWaitCursor = b && what != "Upscaling…";
        UpdateButtons();
    }

    void SetStatus(string msg, TextRole role = TextRole.Text2)
    {
        if (InvokeRequired) { BeginInvoke(() => SetStatus(msg, role)); return; }
        status.Text = msg; status.Role = role; status.Invalidate();
        LayoutContent();
    }

    void Copy(string text, string msg) { try { Clipboard.SetText(text); SetStatus(msg, TextRole.Ok); } catch { } }
}

static class RectExt
{
    public static System.Drawing.Drawing2D.GraphicsPath ToPath(this RectangleF r)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddEllipse(r);
        return p;
    }
}
