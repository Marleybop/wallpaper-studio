using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallpaperStudioUploader;

/// Remembered between runs in %APPDATA%\WallpaperStudioUploader\settings.json.
/// The RCON password is encrypted for your Windows account (DPAPI), never stored as plain text.
public sealed class Settings
{
    public string RconHost { get; set; } = "127.0.0.1";
    public int RconPort { get; set; } = 28016;
    public string RconPasswordProtected { get; set; } = "";
    public string Collection { get; set; } = "";
    public bool AddToServerAfterUpload { get; set; }
    public Visibility Visibility { get; set; } = Visibility.Hidden;
    public int Size { get; set; } = 2048;
    public string LastImageFolder { get; set; } = "";

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WallpaperStudioUploader");
    static string FilePath => Path.Combine(Dir, "settings.json");

    /// Built items are kept here, one folder per Workshop ID, so they can be re-uploaded later.
    public static string ItemsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Wallpaper Studio Uploader");

    [JsonIgnore]   // only the encrypted copy above is ever written to disk
    public string RconPassword
    {
        get
        {
            if (string.IsNullOrEmpty(RconPasswordProtected)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(RconPasswordProtected), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
        set => RconPasswordProtected = string.IsNullOrEmpty(value) ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    public static Settings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
