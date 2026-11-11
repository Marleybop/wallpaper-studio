using System.Runtime.InteropServices;

namespace WallpaperStudioUploader;

static class Program
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

    [STAThread]
    static int Main(string[] args)
    {
        // Command-line mode, for scripts and testing (no Steam, no upload):
        //   WallpaperStudioUploader --build <image> <wall|floor|ceiling|spray> <out folder> [--size 1024] [--tile N] [--gloss 0.25] [--bump 1]
        if (args.Length > 0 && args[0] == "--build")
        {
            AttachConsole(-1);
            return BuildCli(args);
        }
        // Upload from the command line (Steam must be running):
        //   WallpaperStudioUploader --upload <image or link> <wall|floor|ceiling|spray> --title "Name" [--visibility hidden|unlisted|friends|public] [--id <existing id>] [--size 1024] [--tile N]
        if (args.Length > 0 && args[0] == "--upload")
        {
            AttachConsole(-1);
            return UploadCli(args);
        }

        ApplicationConfiguration.Initialize();
        // An image dropped onto the .exe (or "Open with"), or an image link, opens straight away.
        Application.Run(new MainForm(args.Length > 0 && (File.Exists(args[0]) || ImageFetcher.IsUrl(args[0], out _)) ? args[0] : null));
        return 0;
    }

    static int UploadCli(string[] a)
    {
        if (a.Length < 3 || !Kinds.TryParse(a[2], out var kind))
        {
            Console.Error.WriteLine("usage: --upload <image or link> <wall|floor|ceiling|spray> --title \"Name\" [--visibility hidden|unlisted|friends|public] [--id N] [--size 1024] [--tile N] [--description \"...\"]");
            return 2;
        }
        string title = "", desc = ""; ulong id = 0; var vis = Visibility.Hidden;
        var o = new BuildOptions { Kind = kind };
        for (int i = 3; i + 1 < a.Length; i += 2)
        {
            var v = a[i + 1];
            switch (a[i])
            {
                case "--title": title = v; break;
                case "--description": desc = v; break;
                case "--id": id = ulong.Parse(v); break;
                case "--size": o.Size = int.Parse(v) == 1024 ? 1024 : 2048; break;
                case "--tile": o.Tile = Math.Clamp(int.Parse(v), 1, 8); break;
                case "--visibility":
                    vis = v.ToLowerInvariant() switch { "public" => Visibility.Public, "unlisted" => Visibility.Unlisted, "friends" => Visibility.FriendsOnly, _ => Visibility.Hidden };
                    break;
            }
        }
        if (string.IsNullOrWhiteSpace(title)) { Console.Error.WriteLine("error: --title is required"); return 2; }
        try
        {
            string image = a[1];
            if (ImageFetcher.IsUrl(image, out var uri)) { image = ImageFetcher.Download(uri).GetAwaiter().GetResult().path; Console.WriteLine("downloaded " + image); }
            var err = SteamWorkshop.Connect();
            if (err != null) { Console.Error.WriteLine("error: " + err); return 1; }
            o.AuthorId = SteamWorkshop.SteamId;
            string folder = Path.Combine(Settings.ItemsDir, "_cli", "content");
            ItemBuilder.Build(image, folder, o, m => Console.WriteLine(m));
            Console.WriteLine($"uploading as {SteamWorkshop.Name}…");
            var res = SteamWorkshop.Upload(new UploadRequest
            {
                ExistingId = id, Title = title, Description = desc, Kind = kind, Visibility = vis, ContentFolder = folder,
            }, new Progress<float>(p => { })).GetAwaiter().GetResult();
            if (res.NeedsAgreement) Console.WriteLine("note: accept the Steam Workshop legal agreement on the item page");
            if (!res.Ok) { Console.Error.WriteLine("error: " + res.Message + (res.Id != 0 ? $" (item {res.Id})" : "")); return 1; }
            string keep = Path.Combine(Settings.ItemsDir, res.Id.ToString(), "content");
            Directory.CreateDirectory(keep);
            foreach (var f in Directory.GetFiles(folder)) File.Copy(f, Path.Combine(keep, Path.GetFileName(f)), true);
            Console.WriteLine($"uploaded {res.Id} {res.Url}");
            var pk = Kinds.PluginKind(kind);
            if (pk != null) Console.WriteLine($"wpstudio.add {res.Id} {pk}");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("error: " + e.Message); return 1; }
        finally { SteamWorkshop.Disconnect(); }
    }

    static int BuildCli(string[] a)
    {
        if (a.Length < 4 || !Kinds.TryParse(a[2], out var kind))
        {
            Console.Error.WriteLine("usage: --build <image> <wall|floor|ceiling|spray> <out folder> [--size 1024] [--tile N] [--gloss 0.25] [--bump 1]");
            return 2;
        }
        var o = new BuildOptions { Kind = kind };
        for (int i = 4; i + 1 < a.Length; i += 2)
        {
            var v = a[i + 1];
            switch (a[i])
            {
                case "--size": o.Size = int.Parse(v) == 1024 ? 1024 : 2048; break;
                case "--tile": o.Tile = Math.Clamp(int.Parse(v), 1, 8); break;
                case "--gloss": o.Gloss = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--bump": o.Bump = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--author": o.AuthorId = ulong.Parse(v); break;
            }
        }
        try
        {
            ItemBuilder.Build(a[1], a[3], o, m => Console.WriteLine(m));
            Console.WriteLine($"wrote {a[3]} as '{Kinds.ItemType(kind)}'");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("error: " + e.Message); return 1; }
    }
}
