using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace WallpaperStudioUploader;

/// Gets an image from a web link (or the clipboard) into a local file the builder can read.
public static class ImageFetcher
{
    const long MaxBytes = 60L * 1024 * 1024;   // 60 MB is plenty for any texture source

    static readonly HttpClient http = CreateClient();

    static HttpClient CreateClient()
    {
        var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("WallpaperStudioUploader/1.0");
        h.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        h.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html", 0.8));
        h.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.5));
        return h;
    }

    static string Dir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperStudioUploader", "downloads");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// True if the text looks like a web link.
    public static bool IsUrl(string? s, out Uri uri)
    {
        uri = null!;
        s = s?.Trim().Trim('"', '\'', '<', '>');
        return !string.IsNullOrEmpty(s) && Uri.TryCreate(s, UriKind.Absolute, out uri!) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    /// Downloads the image at the link. If the link is a web page, follows the page's main image
    /// (its og:image / twitter:image tag), which covers Imgur, ArtStation, Reddit, DeviantArt and similar.
    /// Returns the local file and a friendly name for the title.
    public static async Task<(string path, string name)> Download(Uri uri, CancellationToken ct = default)
    {
        for (int hop = 0; hop < 2; hop++)
        {
            using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"The site answered {(int)resp.StatusCode} {resp.ReasonPhrase}. Check the link works in your browser.");
            if (resp.Content.Headers.ContentLength > MaxBytes)
                throw new InvalidOperationException("That file is over 60 MB, which is far bigger than a wallpaper needs.");

            var type = resp.Content.Headers.ContentType?.MediaType ?? "";
            var final = resp.RequestMessage?.RequestUri ?? uri;

            if (type.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                if (hop > 0) break;
                var html = await ReadLimited(resp, 2 * 1024 * 1024, ct);
                var img = FindPageImage(System.Text.Encoding.UTF8.GetString(html), final);
                if (img == null) throw new InvalidOperationException("That link is a web page, and no image was found on it. Right-click the image → \"Copy image address\" and paste that instead.");
                uri = img;
                continue;
            }

            var bytes = await ReadLimited(resp, MaxBytes, ct);
            string ext = ExtensionFor(type, final);
            string name = NameFrom(final);
            string path = Path.Combine(Dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Safe(name)}{ext}");
            await File.WriteAllBytesAsync(path, bytes, ct);

            // Make sure it really is an image we can read.
            try { using var check = ItemBuilder.LoadImage(path); }
            catch
            {
                File.Delete(path);
                throw new InvalidOperationException(type.Contains("webp") || ext == ".webp" || ext == ".avif"
                    ? "That image is WebP/AVIF, which Windows can't read here. Open it in your browser, save it as PNG or JPG, then drop the file in."
                    : $"That link didn't give a readable image (it sent \"{(type == "" ? "unknown" : type)}\").");
            }
            return (path, name);
        }
        throw new InvalidOperationException("Couldn't find an image at that link.");
    }

    /// Saves an image from the clipboard (e.g. a browser's "Copy image") to a file.
    public static string SaveClipboardImage(Image img)
    {
        string path = Path.Combine(Dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-pasted.png");
        img.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    static async Task<byte[]> ReadLimited(HttpResponseMessage resp, long max, CancellationToken ct)
    {
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > max) throw new InvalidOperationException("That download is too big for a wallpaper (over 60 MB).");
        }
        return ms.ToArray();
    }

    static Uri? FindPageImage(string html, Uri page)
    {
        foreach (var prop in new[] { "og:image:secure_url", "og:image:url", "og:image", "twitter:image", "twitter:image:src" })
        {
            // <meta property="og:image" content="..."> in either attribute order
            var m = Regex.Match(html, $@"<meta[^>]+(?:property|name)\s*=\s*[""']{Regex.Escape(prop)}[""'][^>]*content\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!m.Success) m = Regex.Match(html, $@"<meta[^>]+content\s*=\s*[""']([^""']+)[""'][^>]*(?:property|name)\s*=\s*[""']{Regex.Escape(prop)}[""']", RegexOptions.IgnoreCase);
            if (m.Success && Uri.TryCreate(page, WebUtility.HtmlDecode(m.Groups[1].Value), out var u) && (u.Scheme == "https" || u.Scheme == "http"))
                return u;
        }
        return null;
    }

    static string ExtensionFor(string type, Uri u) => type.ToLowerInvariant() switch
    {
        "image/png" => ".png", "image/jpeg" or "image/jpg" => ".jpg", "image/gif" => ".gif", "image/bmp" => ".bmp",
        "image/tiff" => ".tif", "image/webp" => ".webp", "image/avif" => ".avif",
        _ => Path.GetExtension(u.AbsolutePath) is { Length: > 1 and <= 5 } e ? e.ToLowerInvariant() : ".img",
    };

    static string NameFrom(Uri u)
    {
        // Use the file name unless it's missing or looks like a random ID (e.g. Imgur's "aB3xY9k").
        var last = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(u.AbsolutePath)).Trim();
        bool randomId = Regex.IsMatch(last, @"^[A-Za-z0-9]+$") && last.Any(char.IsDigit) && last.Length <= 16;
        return last.Length < 3 || randomId ? u.Host.Replace("www.", "").Replace("i.", "").Split('.')[0] + " image" : last;
    }

    static string Safe(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Length > 60 ? s[..60] : s;
    }
}
