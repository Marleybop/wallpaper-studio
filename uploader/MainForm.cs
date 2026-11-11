using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using static WallpaperStudioUploader.ItemBuilder;
// (Preview, Build and LoadImage come from ItemBuilder)

namespace WallpaperStudioUploader;

public sealed class MainForm : Form
{
    readonly Settings settings = Settings.Load();

    // header
    readonly Panel header = new HeaderBar();
    readonly StatusPill steamPill = new();

    // left column
    readonly Panel leftScroll = new() { AutoScroll = true, BackColor = Theme.Bg };
    readonly Stack left = new() { Gap = 12, BackColor = Theme.Bg, Padding = new Padding(0, 0, 10, 16) };
    readonly Label imageName = new() { AutoSize = false, Font = Theme.Semi, ForeColor = Theme.Text, BackColor = Theme.Panel, AutoEllipsis = true, Text = "No image yet" };
    readonly Label imageInfo = new() { AutoSize = false, Font = Theme.Small, ForeColor = Theme.Faint, BackColor = Theme.Panel, Text = "Drop one on the preview, or browse" };
    readonly FlatButton browse = new("Browse…");
    readonly TypePicker typePicker = new();
    readonly Hint typeHint = new();
    readonly Field title = new("Shown on the Workshop page");
    readonly Field description = new("Optional", multiline: true, height: 70);
    readonly Segmented visibility = new("Public", "Unlisted", "Friends", "Hidden");
    readonly Hint visibilityHint = new();
    readonly Card lookCard = new("Look", 4);
    readonly Segmented size = new("2048 px  ·  best", "1024 px  ·  smaller");
    readonly Stepper tile = new("Repeat", 1, 8, 1) { Format = v => v == 1 ? "Off" : $"{v}×{v}" };
    readonly Slider gloss = new("Shine", 0, 100, 25) { Format = v => v < 15 ? "Matte" : v > 70 ? "Glossy" : $"{v}%" };
    readonly Slider bump = new("Surface depth", 0, 200, 100) { Format = v => v == 0 ? "Flat" : $"{v}%" };
    readonly Hint lookHint = new();
    readonly Expander updateExp = new("Update an item you already uploaded", "Change its picture, text or visibility");
    readonly Card updateCard = new("Update item");
    readonly Field existingId = new("Workshop ID or link");
    readonly FlatButton loadExisting = new("Load");
    readonly FlatButton clearExisting = new("New item instead", ButtonKind.Ghost);
    readonly Expander serverExp = new("Send to a Wallpaper Studio server", "Optional: add new wallpapers over RCON");
    readonly Card serverCard = new("Server (RCON)");
    readonly Field rconHost = new("127.0.0.1");
    readonly Field rconPort = new("28016");
    readonly Field rconPassword = new("rcon.password");
    readonly Field collection = new("Optional, e.g. Brick");
    readonly Toggle addAfterUpload = new("Add wallpapers to the server after upload");
    readonly FlatButton addNow = new("Add the current ID now");

    readonly Field imageUrl = new("…or paste an image link");
    readonly FlatButton fetchUrl = new("Get");

    // right column
    readonly PreviewBox preview = new();
    readonly ResultCard result = new();
    readonly Panel logPanel = new LogFrame();
    readonly RichTextBox log = new() { BorderStyle = BorderStyle.None, ReadOnly = true, BackColor = Theme.Input, ForeColor = Theme.Muted, Font = Theme.Mono, DetectUrls = false };
    readonly ProgressLine progress = new();
    readonly Label status = new() { AutoSize = false, AutoEllipsis = true, Font = Theme.Base, ForeColor = Theme.Muted, BackColor = Theme.Bg, TextAlign = ContentAlignment.MiddleLeft };
    readonly FlatButton logButton = new("Show log", ButtonKind.Ghost);
    readonly FlatButton uploadButton = new("Upload to Workshop", ButtonKind.Primary) { Height = 40 };

    // state
    string? sourcePath;
    string? sourceUrl;                // set when the image came from a link
    Bitmap? previewSource;            // the chosen image, scaled down for fast previews
    int previewGen;
    CancellationTokenSource? fetchCts;
    readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 160 };
    ulong lastId;
    bool busy;

    public MainForm(string? initialImage = null)
    {
        if (initialImage != null)
            Shown += async (_, _) => { if (ImageFetcher.IsUrl(initialImage, out var u)) await FetchUrl(u); else SetImage(initialImage); };
        Text = "Wallpaper Studio Uploader";
        Icon = Theme.MakeIcon();
        BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Base;
        ClientSize = new Size(1240, 820); MinimumSize = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        BuildLeft();
        BuildRight();
        header.Controls.Add(steamPill);
        Controls.AddRange(new Control[] { header, leftScroll, preview, result, logPanel, progress, status, logButton, uploadButton });
        KeyPreview = true;
        leftScroll.Controls.Add(left);
        Theme.DarkScrollbars(leftScroll);
        Theme.DarkScrollbars(log);

        // restore settings
        visibility.SelectedIndex = (int)settings.Visibility;
        size.SelectedIndex = settings.Size == 1024 ? 1 : 0;
        rconHost.Text = settings.RconHost; rconPort.Text = settings.RconPort.ToString(); rconPassword.Text = settings.RconPassword;
        collection.Text = settings.Collection; addAfterUpload.Checked = settings.AddToServerAfterUpload;
        OnKindChanged(); OnVisibilityChanged();
        steamPill.Set("Connecting to Steam…", false);
        SetStatus("Pick an image to begin.");

        Wire();
        Shown += (_, _) => { Theme.DarkTitleBar(this); ConnectSteam(); };
        HandleCreated += (_, _) => Theme.DarkTitleBar(this);
        FormClosing += (_, _) => { SaveSettings(); SteamWorkshop.Disconnect(); };
    }

    ItemKind SelectedKind => (ItemKind)typePicker.SelectedIndex;

    // ================= layout =================

    void BuildLeft()
    {
        // 1 Image
        var c1 = new Card("Image", 1);
        var row = new InlineRow(browse) { Height = 40 };
        var names = new Panel { BackColor = Theme.Panel };
        imageName.SetBounds(0, 0, 10, 20); imageInfo.SetBounds(0, 21, 10, 18);
        imageName.Anchor = imageInfo.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        names.Controls.Add(imageName); names.Controls.Add(imageInfo);
        names.Resize += (_, _) => { imageName.Width = imageInfo.Width = names.Width; };
        row.Fill = names;
        c1.Controls.Add(row);
        c1.Controls.Add(new InlineRow(fetchUrl) { Fill = imageUrl, Height = 36 });
        c1.Controls.Add(new Hint("A direct image link or a page with one on it (Imgur, ArtStation, Reddit…). Ctrl+V anywhere also pastes a link or a copied image."));

        // 2 Type
        var c2 = new Card("Type", 2);
        c2.Controls.Add(typePicker); c2.Controls.Add(typeHint);

        // 3 Details
        var c3 = new Card("Details", 3);
        c3.Controls.AddRange(new Control[]
        {
            new Caption("Title"), title, new Caption("Description"), description,
            new Caption("Visibility on Steam"), visibility, visibilityHint,
        });

        // 4 Look
        lookCard.Controls.AddRange(new Control[] { new Caption("Texture size"), size, tile, gloss, bump, lookHint });

        // update existing
        updateCard.Controls.Add(new InlineRow(loadExisting) { Fill = existingId, Height = 36 });
        updateCard.Controls.Add(new Hint("Load fills in the details. Choose a new image to replace the picture, or leave it to change only the text or visibility. You can only update items your Steam account owns."));
        updateCard.Controls.Add(clearExisting);
        updateExp.Target = updateCard;

        // server
        var hostRow = new InlineRow(rconPort) { Fill = rconHost, Height = 36, RightWidth = 96 };
        serverCard.Controls.AddRange(new Control[]
        {
            new Caption("Address  ·  RCON port"), hostRow, new Caption("RCON password"), rconPassword,
            new Caption("Collection"), collection, addAfterUpload, addNow,
            new Hint("Runs  wpstudio.add <id> <surface>  on the server. Needs rcon.web on (the default). The password is saved encrypted for your Windows account."),
        });
        rconPassword.Box.UseSystemPasswordChar = true;
        serverExp.Target = serverCard;

        left.Controls.AddRange(new Control[] { c1, c2, c3, lookCard, updateExp, updateCard, serverExp, serverCard });
        updateExp.Open = false; serverExp.Open = false;
    }

    void BuildRight()
    {
        logPanel.Controls.Add(log);
        logPanel.Visible = false;
        uploadButton.Icon = (g, r, c) => Glyphs.Upload(g, r, c, 1.8f);
        uploadButton.FitWidth();
        logButton.FitWidth();
        browse.FitWidth();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (header == null) return;
        int W = ClientSize.Width, H = ClientSize.Height, pad = 20, gap = 20;
        header.SetBounds(0, 0, W, 68);
        steamPill.Location = new Point(W - pad - steamPill.Width, (68 - steamPill.Height) / 2);

        int leftW = 440, top = 80, scrollW = SystemInformation.VerticalScrollBarWidth + 4;
        leftScroll.SetBounds(pad, top, leftW + scrollW, H - top - 4);
        left.Width = leftW;

        int rx = pad + leftW + scrollW + gap, rw = W - rx - pad;

        int bottom = H - 16;
        uploadButton.Location = new Point(rx + rw - uploadButton.Width, bottom - uploadButton.Height);
        logButton.Location = new Point(uploadButton.Left - 8 - logButton.Width, bottom - logButton.Height - 3);
        status.SetBounds(rx, bottom - 40, logButton.Left - rx - 12, 40);
        progress.SetBounds(rx, bottom - 52, rw, 4);
        int y = progress.Top - 12;
        if (logPanel.Visible) { logPanel.SetBounds(rx, y - 150, rw, 150); y = logPanel.Top - 12; }
        if (result.Visible) { result.SetBounds(rx, y - 64, rw, 64); y = result.Top - 12; }
        preview.SetBounds(rx, top, rw, y - top);
    }

    // ================= events =================

    void Wire()
    {
        browse.Click += (_, _) => ChooseImage();
        preview.Browse += () => { if (preview.Image == null) ChooseImage(); };
        preview.FileDropped += SetImage;
        preview.TextDropped += t => { if (ImageFetcher.IsUrl(t, out var u)) _ = FetchUrl(u); else SetStatus("That isn't an image or a link.", true); };
        preview.ImageDropped += img => SetImage(ImageFetcher.SaveClipboardImage(img));
        fetchUrl.Click += async (_, _) =>
        {
            if (ImageFetcher.IsUrl(imageUrl.Text, out var u)) await FetchUrl(u);
            else SetStatus("Paste a link that starts with https://", true);
        };
        imageUrl.Box.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (ImageFetcher.IsUrl(imageUrl.Text, out var u)) await FetchUrl(u);
        };
        KeyDown += OnPasteKey;
        typePicker.Changed += _ => { OnKindChanged(); RequestPreview(); };
        visibility.Changed += _ => OnVisibilityChanged();
        tile.Changed += _ => RequestPreview();
        previewTimer.Tick += (_, _) => { previewTimer.Stop(); RenderPreview(); };
        uploadButton.Click += async (_, _) => await UploadClicked();
        loadExisting.Click += async (_, _) => await LoadExisting();
        clearExisting.Click += (_, _) => { existingId.Text = ""; SetStatus("Next upload creates a new item."); };
        logButton.Click += (_, _) => { logPanel.Visible = !logPanel.Visible; logButton.Text = logPanel.Visible ? "Hide log" : "Show log"; PerformLayout(); };
        addNow.Click += async (_, _) =>
        {
            ulong id = ParseId(existingId.Text);
            if (id == 0) id = lastId;
            if (id == 0) { SetStatus("Enter a Workshop ID under “Update an item”, or upload something first.", true); return; }
            await AddToServer(id);
        };
        result.CopyId.Click += (_, _) => Copy(lastId.ToString(), "ID copied.");
        result.CopyCommand.Click += (_, _) => Copy(AddCommand(lastId), "Command copied. Paste it into the server console.");
        result.OpenPage.Click += (_, _) => Process.Start(new ProcessStartInfo($"https://steamcommunity.com/sharedfiles/filedetails/?id={lastId}") { UseShellExecute = true });
        foreach (var exp in new[] { updateExp, serverExp }) exp.Click += (_, _) => left.PerformLayout();
    }

    void OnKindChanged()
    {
        bool spray = SelectedKind == ItemKind.Spray;
        typeHint.Text = SelectedKind switch
        {
            ItemKind.Wall => "Covers one wall panel. Uses the centre square of your image.",
            ItemKind.Floor => "Covers one floor tile. Seamless patterns look best, since tiles sit side by side.",
            ItemKind.Ceiling => "Covers one ceiling tile. Seamless patterns look best.",
            _ => "A decal for the Spray Can. Keeps the whole image and its transparency, so use a PNG with a clear background.",
        };
        tile.Visible = gloss.Visible = bump.Visible = !spray;
        lookHint.Text = spray ? "Sprays only use the colour of your image. Transparent areas stay clear."
                              : "Repeat tiles small patterns. Shine and surface depth are made from the image's brightness.";
        lookCard.Title = spray ? "Size" : "Look";
        addAfterUpload.Enabled = addNow.Enabled = !spray;
        lookCard.Invalidate(); left.PerformLayout(); PerformLayout();
    }

    void OnVisibilityChanged()
    {
        visibilityHint.Text = visibility.SelectedIndex switch
        {
            0 => "Anyone can find it on the Workshop.",
            1 => "Only people with the link can see it on Steam.",
            2 => "Only your Steam friends can see it on Steam.",
            _ => "Nobody can browse it, but it still works in game by its ID. Good for server-only wallpapers.",
        };
        left.PerformLayout();
    }

    // ================= Steam =================

    async void ConnectSteam()
    {
        uploadButton.Enabled = loadExisting.Enabled = false;
        steamPill.Set("Connecting to Steam…", false); PerformLayout();
        var err = await Task.Run(SteamWorkshop.Connect);
        if (err != null)
        {
            steamPill.Set("Steam not connected · click to retry", false);
            steamPill.Cursor = Cursors.Hand;
            steamPill.Click -= RetrySteam; steamPill.Click += RetrySteam;
            SetStatus(err.Split('\n')[0], true); Log(err, true);
        }
        else
        {
            steamPill.Set($"Steam · {SteamWorkshop.Name}", true);
            steamPill.Cursor = Cursors.Default;
            uploadButton.Enabled = loadExisting.Enabled = !busy;
            Log($"Connected to Steam as {SteamWorkshop.Name} ({SteamWorkshop.SteamId}).");
        }
        PerformLayout();
    }

    void RetrySteam(object? s, EventArgs e) { if (!SteamWorkshop.Ready) ConnectSteam(); }

    // ================= image + preview =================

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

    void SetImage(string path, string? fromUrl)
    {
        Bitmap full;
        try { full = LoadImage(path); }
        catch (Exception e) { SetStatus("Couldn't open that image: " + e.Message, true); return; }

        sourcePath = path;
        sourceUrl = fromUrl;
        if (fromUrl == null && !path.Contains("WallpaperStudioUploader")) settings.LastImageFolder = Path.GetDirectoryName(path) ?? "";
        imageName.Text = fromUrl != null ? new Uri(fromUrl).Host + "  ·  " + Path.GetFileName(new Uri(fromUrl).AbsolutePath) : Path.GetFileName(path);
        bool tiny = Math.Min(full.Width, full.Height) < 512;
        imageInfo.Text = $"{full.Width} × {full.Height} px" + (tiny ? "  ·  small, will look blurry" : full.Width != full.Height ? "  ·  not square" : "");
        imageInfo.ForeColor = tiny ? Theme.Warn : Theme.Faint;
        if (fromUrl == null) imageUrl.Text = "";
        if (string.IsNullOrWhiteSpace(title.Text) && fromUrl == null) title.Text = Tidy(Path.GetFileNameWithoutExtension(path));

        // keep a smaller copy for live previews
        float s = Math.Min(1f, 1024f / Math.Max(full.Width, full.Height));
        var small = s < 1f ? new Bitmap(full, Math.Max(1, (int)(full.Width * s)), Math.Max(1, (int)(full.Height * s))) : full;
        if (!ReferenceEquals(small, full)) full.Dispose();
        previewSource?.Dispose(); previewSource = small;
        result.Visible = false;
        SetStatus("Image loaded. Adjust the look, then upload.");
        Log($"Image: {path}");
        RenderPreview();
    }

    void RequestPreview() { if (previewSource != null) { previewTimer.Stop(); previewTimer.Start(); } }

    async void RenderPreview()
    {
        if (previewSource == null) return;
        int gen = ++previewGen;
        var o = Options();
        var src = new Bitmap(previewSource);   // the worker gets its own copy
        Bitmap img;
        try { img = await Task.Run(() => { using (src) return Preview(src, o); }); }
        catch (Exception e) { SetStatus("Preview failed: " + e.Message, true); return; }
        if (gen != previewGen) { img.Dispose(); return; }   // a newer one is on its way
        preview.Badge = o.Kind switch
        {
            ItemKind.Spray => "Spray decal",
            _ => Kinds.Label(o.Kind).Replace("Wallpaper: ", "") is var s ? char.ToUpper(s[0]) + s[1..] + (o.Tile > 1 ? $"  ·  repeated {o.Tile}×{o.Tile}" : "") : "",
        };
        preview.Image = img;
    }

    // ---- images from links and the clipboard ----

    async Task FetchUrl(Uri uri)
    {
        fetchCts?.Cancel();
        var cts = fetchCts = new CancellationTokenSource();
        imageUrl.Text = uri.ToString();
        fetchUrl.Enabled = false; preview.Busy = true; preview.Invalidate();
        SetStatus($"Downloading from {uri.Host}…");
        try
        {
            var (path, name) = await ImageFetcher.Download(uri, cts.Token);
            if (cts.IsCancellationRequested) return;
            Log($"Downloaded {uri} → {path}");
            bool hadTitle = !string.IsNullOrWhiteSpace(title.Text);
            SetImage(path, uri.ToString());
            imageName.Text = $"{uri.Host.Replace("www.", "")}  ·  {name}";
            if (!hadTitle) title.Text = Tidy(name);
        }
        catch (OperationCanceledException) when (!cts.IsCancellationRequested) { SetStatus("The download timed out. Try again, or save the image and drop the file in.", true); }
        catch (OperationCanceledException) { }
        catch (Exception e) { SetStatus(e.Message, true); Log($"Download failed: {uri}: {e.Message}", true); }
        finally { fetchUrl.Enabled = true; preview.Busy = false; preview.Invalidate(); }
    }

    // Ctrl+V anywhere outside a text box: paste a copied image, a link, or a copied file.
    void OnPasteKey(object? sender, KeyEventArgs e)
    {
        if (!(e.Control && e.KeyCode == Keys.V) || GetFocusedTextBox() != null) return;   // normal paste in text boxes
        try
        {
            if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList() is { Count: > 0 } files) { SetImage(files[0]!); e.Handled = true; }
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is Image img) { using (img) SetImage(ImageFetcher.SaveClipboardImage(img)); e.Handled = true; }
            else if (Clipboard.ContainsText() && ImageFetcher.IsUrl(Clipboard.GetText(), out var u)) { _ = FetchUrl(u); e.Handled = true; }
        }
        catch (Exception ex) { SetStatus("Couldn't paste that: " + ex.Message, true); }
    }

    TextBoxBase? GetFocusedTextBox()
    {
        Control? c = ActiveControl;
        while (c is ContainerControl cc && cc.ActiveControl != null) c = cc.ActiveControl;
        return c as TextBoxBase ?? (c is Field f && f.Box.Focused ? f.Box : null);
    }

    static string Tidy(string name) => Regex.Replace(name.Replace('_', ' ').Replace('-', ' '), @"\s+", " ").Trim();

    BuildOptions Options() => new()
    {
        Kind = SelectedKind,
        Size = size.SelectedIndex == 1 ? 1024 : 2048,
        Tile = tile.Value,
        Gloss = gloss.Value / 100f,
        Bump = bump.Value / 100f,
        AuthorId = SteamWorkshop.SteamId,
    };

    // ================= upload =================

    async Task UploadClicked()
    {
        if (busy) return;
        if (!SteamWorkshop.Ready) { SetStatus("Not connected to Steam. Start Steam, then click the Steam badge to retry.", true); return; }
        ulong existing = ParseId(existingId.Text);
        if (!string.IsNullOrWhiteSpace(existingId.Text) && existing == 0) { SetStatus("“Update item” isn't a Workshop ID or link.", true); return; }
        if (string.IsNullOrWhiteSpace(title.Text)) { SetStatus("Give it a title first.", true); title.Box.Focus(); return; }
        bool haveImage = sourcePath != null && File.Exists(sourcePath);
        if (!haveImage && existing == 0) { SetStatus("Choose an image first.", true); return; }

        string what = existing == 0 ? "Create a new Workshop item" : $"Update Workshop item {existing}" + (haveImage ? "" : " (details only, picture unchanged)");
        string[] visNames = { "Public", "Unlisted", "Friends only", "Hidden" };
        if (!ConfirmDialog.Ask(this, what, new[]
            {
                ("Title", title.Text.Trim()),
                ("Type", Kinds.ItemType(SelectedKind)),
                ("Visibility", visNames[visibility.SelectedIndex]),
                ("Steam account", SteamWorkshop.Name),
            }, existing == 0 ? "Upload" : "Update"))
            return;

        SetBusy(true);
        try
        {
            var o = Options();
            string? folder = null;
            if (haveImage)
            {
                SetStatus("Making textures…"); progress.Value = 0.03f;
                folder = Path.Combine(Settings.ItemsDir, "_draft", "content");
                var sw = Stopwatch.StartNew();
                string srcPath = sourcePath!;
                await Task.Run(() => Build(srcPath, folder, o, m => Log("  " + m)));
                Log($"Textures ready in {sw.Elapsed.TotalSeconds:0.0}s.");
            }
            progress.Value = 0.15f;
            SetStatus(existing == 0 ? "Creating the Workshop item and uploading…" : $"Uploading to {existing}…");
            var prog = new Progress<float>(p => progress.Value = 0.15f + 0.85f * p);
            var res = await SteamWorkshop.Upload(new UploadRequest
            {
                ExistingId = existing,
                Title = title.Text.Trim(),
                Description = description.Text.Trim(),
                Kind = SelectedKind,
                Visibility = (Visibility)visibility.SelectedIndex,
                ContentFolder = folder,
            }, prog);

            if (res.NeedsAgreement)
                Log("Steam needs you to accept the Workshop legal agreement once. Open the Workshop page and accept it; then the item can be used.", true);
            if (!res.Ok)
            {
                progress.Value = 0;
                SetStatus(res.Message, true); Log(res.Message, true);
                if (res.Id != 0) lastId = res.Id;
                return;
            }

            progress.Value = 1;
            lastId = res.Id;
            if (folder != null) KeepCopy(res.Id, folder);
            existingId.Text = res.Id.ToString();
            bool wallpaper = Kinds.PluginKind(SelectedKind) != null;
            try { Clipboard.SetText(wallpaper ? AddCommand(res.Id) : res.Id.ToString()); } catch { }
            result.Show(existing == 0 ? "Uploaded to the Workshop" : "Workshop item updated", res.Id, wallpaper);
            SetStatus(wallpaper ? "Done. The add command is on your clipboard. Steam may take a few minutes to serve it."
                                : "Done. The ID is on your clipboard. Steam may take a few minutes to serve it.", good: true);
            Log($"Uploaded {res.Id}.");
            if (res.NeedsAgreement) SetStatus("Uploaded, but accept the Workshop agreement on the item page before it works.", true);
            if (wallpaper && addAfterUpload.Checked) await AddToServer(res.Id);
        }
        catch (Exception e) { progress.Value = 0; SetStatus("Upload failed: " + e.Message, true); Log(e.ToString(), true); }
        finally { SetBusy(false); SaveSettings(); PerformLayout(); }
    }

    // Keep the uploaded files in Documents\Wallpaper Studio Uploader\<id>\ so the item can be re-uploaded later.
    void KeepCopy(ulong id, string folder)
    {
        try
        {
            string dest = Path.Combine(Settings.ItemsDir, id.ToString());
            string destContent = Path.Combine(dest, "content");
            Directory.CreateDirectory(destContent);
            foreach (var f in Directory.GetFiles(folder)) File.Copy(f, Path.Combine(destContent, Path.GetFileName(f)), true);
            File.WriteAllText(Path.Combine(dest, "item.json"), JsonSerializer.Serialize(new
            {
                Id = id, Title = title.Text, Description = description.Text, Type = Kinds.ItemType(SelectedKind),
                Source = sourceUrl ?? sourcePath, Uploaded = DateTime.Now,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Log("Saved a copy in " + dest);
        }
        catch (Exception e) { Log("Couldn't save a local copy: " + e.Message, true); }
    }

    async Task LoadExisting()
    {
        ulong id = ParseId(existingId.Text);
        if (id == 0) { SetStatus("Enter a Workshop ID or link to load.", true); return; }
        SetBusy(true);
        try
        {
            SetStatus($"Loading {id}…");
            var (item, err) = await SteamWorkshop.GetOwnItem(id);
            if (err != null) { SetStatus(err, true); return; }
            var it = item!.Value;
            title.Text = it.Title ?? ""; description.Text = it.Description ?? "";
            foreach (ItemKind k in Enum.GetValues<ItemKind>())
                if (it.HasTag(Kinds.ItemType(k))) typePicker.SelectedIndex = (int)k;
            visibility.SelectedIndex = it.IsPublic ? 0 : it.IsFriendsOnly ? 2 : it.IsPrivate ? 3 : 1;
            lastId = id;
            SetStatus($"Loaded “{it.Title}”. Choose a new image to replace the picture, or just change the details and upload.", good: true);
        }
        catch (Exception e) { SetStatus("Couldn't load it: " + e.Message, true); }
        finally { SetBusy(false); }
    }

    // ================= server =================

    string AddCommand(ulong id)
    {
        string? pk = Kinds.PluginKind(SelectedKind);
        return pk == null ? id.ToString() : $"wpstudio.add {id} {pk}" + (string.IsNullOrWhiteSpace(collection.Text) ? "" : " " + collection.Text.Trim());
    }

    async Task AddToServer(ulong id)
    {
        if (Kinds.PluginKind(SelectedKind) == null) { SetStatus("Sprays aren't managed by Wallpaper Studio, so there's nothing to add.", true); return; }
        if (string.IsNullOrEmpty(rconPassword.Text)) { SetStatus("Fill in the server's RCON password first.", true); serverExp.Open = true; left.PerformLayout(); return; }
        if (!int.TryParse(string.IsNullOrWhiteSpace(rconPort.Text) ? "28016" : rconPort.Text, out int port)) { SetStatus("The RCON port should be a number.", true); return; }
        string host = string.IsNullOrWhiteSpace(rconHost.Text) ? "127.0.0.1" : rconHost.Text.Trim();
        string cmd = AddCommand(id);
        try
        {
            SetStatus($"Sending to {host}:{port}…");
            Log("Server: " + cmd);
            var reply = await Rcon.Send(host, port, rconPassword.Text, cmd);
            reply = string.IsNullOrWhiteSpace(reply) ? "(no reply)" : reply.Trim();
            Log("Server replied: " + reply);
            SetStatus("Server: " + reply, good: reply.StartsWith("Added"), error: !reply.StartsWith("Added") && !reply.StartsWith("Already"));
            SaveSettings();
        }
        catch (Exception e) { SetStatus(e.Message, true); Log(e.Message, true); }
    }

    // ================= helpers =================

    static ulong ParseId(string s)
    {
        s = s.Trim();
        if (ulong.TryParse(s, out var id)) return id;
        var m = Regex.Match(s, @"[?&]id=(\d+)");
        return m.Success && ulong.TryParse(m.Groups[1].Value, out id) ? id : 0;
    }

    void Copy(string text, string msg) { try { Clipboard.SetText(text); SetStatus(msg, good: true); } catch { } }

    void SetBusy(bool b)
    {
        busy = b;
        UseWaitCursor = b;
        uploadButton.Enabled = !b && SteamWorkshop.Ready;
        loadExisting.Enabled = !b && SteamWorkshop.Ready;
        if (b) { progress.Value = 0; result.Visible = false; PerformLayout(); }
    }

    void SetStatus(string msg, bool error = false, bool good = false)
    {
        if (InvokeRequired) { BeginInvoke(() => SetStatus(msg, error, good)); return; }
        status.Text = msg;
        status.ForeColor = error ? Theme.Danger : good ? Theme.Accent : Theme.Muted;
    }

    void Log(string msg, bool error = false)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(msg, error)); return; }
        log.SelectionStart = log.TextLength;
        log.SelectionColor = Theme.Faint; log.AppendText($"{DateTime.Now:HH:mm:ss}  ");
        log.SelectionColor = error ? Theme.Danger : Theme.Muted; log.AppendText(msg + Environment.NewLine);
        log.ScrollToCaret();
    }

    void SaveSettings()
    {
        settings.RconHost = rconHost.Text.Trim();
        if (int.TryParse(rconPort.Text, out int p)) settings.RconPort = p;
        settings.RconPassword = rconPassword.Text;
        settings.Collection = collection.Text.Trim();
        settings.AddToServerAfterUpload = addAfterUpload.Checked;
        settings.Visibility = (Visibility)visibility.SelectedIndex;
        settings.Size = size.SelectedIndex == 1 ? 1024 : 2048;
        try { settings.Save(); } catch { }
    }
}

/// App name and tagline across the top.
sealed class HeaderBar : Panel
{
    public HeaderBar() { DoubleBuffered = true; BackColor = Theme.Bg; SetStyle(ControlStyles.ResizeRedraw, true); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; Theme.Hq(g);
        var tileR = new RectangleF(20, 15, 38, 38);
        Theme.FillRound(g, Theme.Accent, tileR, 9);
        Glyphs.Bricks(g, new RectangleF(29, 25, 20, 18), Theme.AccentText, 1.8f);
        TextRenderer.DrawText(g, "Wallpaper Studio Uploader", Theme.Header, new Point(70, 9), Theme.Text);
        TextRenderer.DrawText(g, "Turn any image into a Rust wallpaper or spray, ready on the Steam Workshop", Theme.Small, new Point(72, 43), Theme.Muted);
        using var pen = new Pen(Theme.Border);
        g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

/// Rounded frame for the log box.
sealed class LogFrame : Panel
{
    public LogFrame() { DoubleBuffered = true; BackColor = Theme.Bg; Padding = new Padding(12, 10, 6, 10); }
    protected override void OnLayout(LayoutEventArgs e) { if (Controls.Count > 0) Controls[0].SetBounds(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical); base.OnLayout(e); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Theme.Bg); Theme.Hq(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRound(g, Theme.Input, r, 10); Theme.StrokeRound(g, Theme.Border, r, 10);
    }
}

/// A control that fills the row, with a fixed control on the right.
sealed class InlineRow : Panel
{
    readonly Control right;
    Control? fill;
    public int RightWidth { get; set; }

    public InlineRow(Control right) { this.right = right; BackColor = Theme.Panel; Controls.Add(right); }

    public Control? Fill
    {
        get => fill;
        set { if (fill != null) Controls.Remove(fill); fill = value; if (value != null) Controls.Add(value); PerformLayout(); }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        int rw = RightWidth > 0 ? RightWidth : right.Width;
        right.SetBounds(Width - rw, (Height - right.Height) / 2, rw, right.Height);
        if (right is Field f) right.Height = Height;
        fill?.SetBounds(0, 0, Width - rw - 8, Height);
        base.OnLayout(e);
    }
}

/// A dark confirmation dialog listing what's about to happen.
sealed class ConfirmDialog : Form
{
    ConfirmDialog(string heading, (string k, string v)[] rows, string okText)
    {
        Text = "Confirm"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent; BackColor = Theme.Panel; ForeColor = Theme.Text; Font = Theme.Base;
        ClientSize = new Size(460, 130 + rows.Length * 28);
        var h = new Label { Text = heading, Font = Theme.CardTitle, ForeColor = Theme.Text, AutoSize = false, Bounds = new Rectangle(24, 20, 412, 26) };
        Controls.Add(h);
        int y = 58;
        foreach (var (k, v) in rows)
        {
            Controls.Add(new Label { Text = k, ForeColor = Theme.Muted, AutoSize = false, Bounds = new Rectangle(24, y, 120, 24) });
            Controls.Add(new Label { Text = v, ForeColor = Theme.Text, Font = Theme.Semi, AutoSize = false, AutoEllipsis = true, Bounds = new Rectangle(150, y, 286, 24) });
            y += 28;
        }
        var ok = new FlatButton(okText, ButtonKind.Primary) { Height = 36 }; ok.FitWidth();
        var cancel = new FlatButton("Cancel") { Height = 36 }; cancel.FitWidth();
        ok.Location = new Point(ClientSize.Width - 24 - ok.Width, ClientSize.Height - 20 - ok.Height);
        cancel.Location = new Point(ok.Left - 8 - cancel.Width, ok.Top);
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(ok); Controls.Add(cancel);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } else if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); } };
        HandleCreated += (_, _) => Theme.DarkTitleBar(this);
    }

    public static bool Ask(IWin32Window owner, string heading, (string, string)[] rows, string okText)
    {
        using var d = new ConfirmDialog(heading, rows, okText);
        return d.ShowDialog(owner) == DialogResult.OK;
    }
}
