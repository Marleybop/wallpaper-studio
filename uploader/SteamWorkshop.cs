using System.Reflection;
using Steamworks;
using Steamworks.Data;
using Steamworks.Ugc;

namespace WallpaperStudioUploader;

public enum Visibility { Public, Unlisted, FriendsOnly, Hidden }

public sealed class UploadRequest
{
    public ulong ExistingId;            // 0 = create a new Workshop item
    public string Title = "";
    public string Description = "";
    public ItemKind Kind;
    public Visibility Visibility = Visibility.Hidden;
    public string? ContentFolder;       // null = only update the title/description/visibility
    public string ChangeNote = "";
}

public sealed class UploadResult
{
    public bool Ok;
    public ulong Id;
    public string Message = "";
    public bool NeedsAgreement;
    public string Url => $"https://steamcommunity.com/sharedfiles/filedetails/?id={Id}";
}

/// Uploads to the Rust Workshop as the Steam user who is logged in. Steam must be running.
public static class SteamWorkshop
{
    public const uint RustAppId = 252490;

    public static bool Ready => SteamClient.IsValid;
    public static ulong SteamId => Ready ? SteamClient.SteamId.Value : 0;
    public static string Name => Ready ? SteamClient.Name : "";

    public static string? Connect()
    {
        if (SteamClient.IsValid) return null;
        try
        {
            SteamClient.Init(RustAppId, asyncCallbacks: true);
            return SteamClient.IsLoggedOn ? null : "Steam is running but not logged on.";
        }
        catch (Exception e)
        {
            return "Couldn't connect to Steam. Start Steam, log in with the account that owns Rust, then try again.\n(" + e.Message + ")";
        }
    }

    public static void Disconnect() { if (SteamClient.IsValid) SteamClient.Shutdown(); }

    /// Returns the item's title/description/kind if it exists and belongs to you.
    public static async Task<(Item? item, string? error)> GetOwnItem(ulong id)
    {
        var item = await Item.GetAsync(new PublishedFileId { Value = id });
        if (item == null || item.Value.Result != Result.OK) return (null, $"Workshop item {id} wasn't found.");
        var it = item.Value;
        if (it.ConsumerApp != RustAppId) return (null, $"Item {id} isn't a Rust Workshop item.");
        if (it.Owner.Id.Value != SteamId) return (null, $"Item {id} belongs to another Steam account, so you can't edit it.");
        return (it, null);
    }

    public static async Task<UploadResult> Upload(UploadRequest r, IProgress<float>? progress = null)
    {
        var res = new UploadResult();
        if (!Ready) { res.Message = "Not connected to Steam."; return res; }

        Editor ed = r.ExistingId == 0 ? Editor.NewCommunityFile : new Editor(new PublishedFileId { Value = r.ExistingId });
        ed = ed.ForAppId(RustAppId)
               .WithTitle(r.Title)
               .WithDescription(r.Description)
               .WithTag(Kinds.ItemType(r.Kind));
        ed = SetVisibility(ed, r.Visibility);
        if (r.ContentFolder != null)
        {
            ed = ed.WithContent(new DirectoryInfo(r.ContentFolder))
                   .WithPreviewFile(Path.Combine(r.ContentFolder, "icon_background.png"));
        }
        if (!string.IsNullOrWhiteSpace(r.ChangeNote)) ed = ed.WithChangeLog(r.ChangeNote);

        var pr = await ed.SubmitAsync(progress);
        res.Id = pr.FileId.Value;
        res.NeedsAgreement = pr.NeedsWorkshopAgreement;
        res.Ok = pr.Success;
        res.Message = pr.Success ? "Uploaded." : Explain(pr.Result);
        return res;
    }

    static Editor SetVisibility(Editor ed, Visibility v)
    {
        switch (v)
        {
            case Visibility.Public: return ed.WithPublicVisibility();
            case Visibility.FriendsOnly: return ed.WithFriendsOnlyVisibility();
            case Visibility.Hidden: return ed.WithPrivateVisibility();
        }
        // Facepunch.Steamworks 2.3.3 has no WithUnlistedVisibility and its enum is internal,
        // so set the Editor's (Nullable<enum>) field to "Unlisted" through reflection.
        object boxed = ed;
        var field = typeof(Editor).GetField("Visibility", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        var enumType = Nullable.GetUnderlyingType(field.FieldType)!;
        field.SetValue(boxed, Enum.Parse(enumType, "Unlisted"));
        return (Editor)boxed;
    }

    static string Explain(Result r) => r switch
    {
        Result.AccessDenied => "Steam refused the upload (AccessDenied). Make sure this account owns Rust and isn't limited, and that you own the item you're editing.",
        Result.FileNotFound => "Steam couldn't find that Workshop item, or the content folder is missing.",
        Result.LimitExceeded => "Steam says your Workshop storage is full.",
        Result.Timeout or Result.ServiceUnavailable or Result.NoConnection => $"Steam didn't answer ({r}). Try again in a minute.",
        Result.InvalidParam => "Steam rejected a field (InvalidParam). Check the title and that the image isn't huge.",
        _ => $"Upload failed: {r}.",
    };
}
