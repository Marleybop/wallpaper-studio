using System.Security.Cryptography;
using System.Text;
using Steamworks;
using Steamworks.Data;
using Steamworks.Ugc;

namespace WallpaperStudioUploader;

/// One of your Workshop items, as listed in the sidebar.
public sealed class LibItem
{
    public ulong Id;
    public string Title = "";
    public string Description = "";
    public ItemKind Kind;
    public Visibility Visibility;
    public DateTime Updated;
    public string PreviewUrl = "";
    public System.Drawing.Image? Thumb;
    public string Folder => Library.Folder(Id);
}

/// Your wallpapers and sprays on the Rust Workshop, plus each item's local folder:
/// Documents\Wallpaper Studio Uploader\<id>\  source.*, source-upscaled.png, edit.json, content\
public static class Library
{
    public static string Folder(ulong id) => Path.Combine(Settings.ItemsDir, id == 0 ? "_new" : id.ToString());
    static string ThumbDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperStudioUploader", "thumbs");
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// Every wallpaper/spray this Steam account has published for Rust, newest first.
    public static async Task<List<LibItem>> LoadAsync()
    {
        var list = new List<LibItem>();
        var seen = new HashSet<ulong>();
        for (int page = 1; page <= 40; page++)
        {
            // All of the user's Rust items; wallpapers and sprays are picked out by tag below
            // (Steam's tag filter combined with a user list returned nothing).
            var q = Query.All.WhereUserPublished(SteamClient.SteamId).SortByCreationDate();
            using var res = await q.GetPageAsync(page);
            if (res == null) break;
            int n = 0;
            foreach (var it in res.Value.Entries)
            {
                n++;
                if (!seen.Add(it.Id.Value)) continue;
                if (!Enum.GetValues<ItemKind>().Any(k => it.HasTag(Kinds.ItemType(k)))) continue;
                var kind = Enum.GetValues<ItemKind>().First(k => it.HasTag(Kinds.ItemType(k)));
                list.Add(new LibItem
                {
                    Id = it.Id.Value,
                    Title = it.Title ?? it.Id.Value.ToString(),
                    Description = it.Description ?? "",
                    Kind = kind,
                    Visibility = it.IsPublic ? Visibility.Public : it.IsFriendsOnly ? Visibility.FriendsOnly : it.IsPrivate ? Visibility.Hidden : Visibility.Unlisted,
                    Updated = it.Updated,
                    PreviewUrl = it.PreviewImageUrl ?? "",
                });
            }
            if (n < 50) break;   // Steam pages hold 50
        }
        return list.OrderByDescending(i => i.Updated).ToList();
    }

    /// Fills in thumbnails (cached on disk), calling back as each arrives.
    public static async Task LoadThumbsAsync(IEnumerable<LibItem> items, Action<LibItem> ready)
    {
        Directory.CreateDirectory(ThumbDir);
        foreach (var it in items.ToList())
        {
            if (it.Thumb != null || string.IsNullOrEmpty(it.PreviewUrl)) continue;
            try
            {
                string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(it.PreviewUrl)))[..12];
                string file = Path.Combine(ThumbDir, $"{it.Id}-{hash}.img");
                if (!File.Exists(file)) await File.WriteAllBytesAsync(file, await http.GetByteArrayAsync(it.PreviewUrl));
                using var full = ImageOps.Load(file);
                it.Thumb = ImageOps.Shrink(full, 96);
                ready(it);
            }
            catch { /* no thumbnail; the list shows a placeholder */ }
        }
    }

    /// Makes sure the item has a source image in its folder. Items made by this app already do;
    /// others are downloaded from Steam and their colour texture is used.
    public static async Task<string> EnsureSourceAsync(LibItem item, IProgress<string> status)
    {
        string dir = item.Folder;
        Directory.CreateDirectory(dir);
        var existing = Directory.GetFiles(dir, "source.*").FirstOrDefault();
        if (existing != null) return existing;

        // v1 uploads kept the built textures in content\
        string built = Path.Combine(dir, "content", "_MainTex0.png");
        if (!File.Exists(built))
        {
            status.Report("Downloading the item from Steam…");
            var ok = await SteamUGC.DownloadAsync(new PublishedFileId { Value = item.Id }, null, 100);
            var it = await Steamworks.Ugc.Item.GetAsync(new PublishedFileId { Value = item.Id });
            string? folder = it?.Directory;
            built = folder != null ? Path.Combine(folder, "_MainTex0.png") : "";
            if (!ok || !File.Exists(built)) throw new InvalidOperationException("Couldn't download this item from Steam. Try again in a minute.");
        }
        string dest = Path.Combine(dir, "source.png");
        File.Copy(built, dest, true);
        return dest;
    }

    /// Copies a new image into the item's folder as its source (replacing the old one).
    public static string AdoptSource(ulong id, string imagePath)
    {
        string dir = Folder(id);
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "source*.*")) File.Delete(old);
        string ext = Path.GetExtension(imagePath).ToLowerInvariant();
        if (ext is "" or ".img" or ".webp" or ".avif") ext = ".png";
        string dest = Path.Combine(dir, "source" + ext);
        File.Copy(imagePath, dest, true);
        return dest;
    }

    /// After the first upload, the "_new" folder becomes the item's folder.
    public static void Promote(ulong newId)
    {
        string from = Folder(0), to = Folder(newId);
        if (!Directory.Exists(from)) return;
        if (Directory.Exists(to)) Directory.Delete(to, true);
        Directory.Move(from, to);
    }

    public static EditState? LoadEdit(ulong id) => EditState.Load(Path.Combine(Folder(id), "edit.json"));
    public static void SaveEdit(EditState e)
    {
        Directory.CreateDirectory(Folder(e.Id));
        File.WriteAllText(Path.Combine(Folder(e.Id), "edit.json"), e.Json());
    }

    public static async Task<bool> DeleteAsync(ulong id) => await SteamUGC.DeleteFileAsync(new PublishedFileId { Value = id });
}
