using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace WallpaperStudioUploader;

/// AI upscaling with Real-ESRGAN (ncnn/Vulkan build, BSD-3-Clause, by Xintao Wang).
/// Not bundled: downloaded on first use from the project's official GitHub release and checked against a pinned SHA-256.
public static class Upscaler
{
    const string Url = "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-windows.zip";
    const string Sha256 = "ABC02804E17982A3BE33675E4D471E91EA374E65B70167ABC09E31ACB412802D";
    const long ZipBytes = 45474481;
    const string Model = "realesrgan-x4plus";

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WallpaperStudioUploader", "realesrgan");
    static string Exe => Path.Combine(Dir, "realesrgan-ncnn-vulkan.exe");
    public static bool Installed => File.Exists(Exe) && File.Exists(Path.Combine(Dir, "models", Model + ".bin"));
    public static string DownloadSize => $"{ZipBytes / 1048576} MB";

    public static async Task InstallAsync(IProgress<float> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Dir);
        string zip = Path.Combine(Dir, "download.zip");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WallpaperStudioUploader/2.0");
            using var resp = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zip);
            var buf = new byte[1 << 16];
            long got = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                got += n;
                progress.Report(Math.Min(1f, (float)got / ZipBytes));
            }
        }
        string hash;
        await using (var fs = File.OpenRead(zip)) hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
        if (!hash.Equals(Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(zip);
            throw new InvalidOperationException("The upscaler download didn't match the expected file, so it wasn't used. Try again later.");
        }
        // keep only what's needed: the program, its runtime DLL and the one model
        using (var z = ZipFile.OpenRead(zip))
        {
            foreach (var e in z.Entries)
            {
                string name = e.FullName.Replace('\\', '/');
                bool want = name is "realesrgan-ncnn-vulkan.exe" or "vcomp140.dll" || name.StartsWith($"models/{Model}.");
                if (!want) continue;
                string outPath = Path.GetFullPath(Path.Combine(Dir, name));
                if (!outPath.StartsWith(Path.GetFullPath(Dir), StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                e.ExtractToFile(outPath, true);
            }
        }
        File.Delete(zip);
    }

    /// 4× upscale of input into output (PNG). Needs a graphics card with Vulkan (almost all from the last ~8 years).
    public static async Task RunAsync(string input, string output, IProgress<float> progress, CancellationToken ct)
    {
        if (!Installed) throw new InvalidOperationException("The upscaler isn't installed yet.");
        var psi = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            WorkingDirectory = Dir,
        };
        foreach (var a in new[] { "-i", input, "-o", output, "-n", Model, "-s", "4", "-m", Path.Combine(Dir, "models"), "-f", "png" })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        string lastErr = "";
        var pct = new Regex(@"(\d+(\.\d+)?)%");
        var errTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await p.StandardError.ReadLineAsync()) != null)
            {
                var m = pct.Match(line);
                if (m.Success && float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    progress.Report(v / 100f);
                else if (!string.IsNullOrWhiteSpace(line)) lastErr = line.Trim();
            }
        }, ct);
        using (ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } }))
        {
            await p.WaitForExitAsync(ct);
            await errTask;
        }
        if (p.ExitCode != 0 || !File.Exists(output))
            throw new InvalidOperationException("Upscaling failed" + (lastErr.Length > 0 ? $": {lastErr}" : ".") +
                " Your graphics driver may need updating (it needs Vulkan).");
        progress.Report(1f);
    }
}
