#nullable disable
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Snapline.Updates;

class UpdateInfo
{
    public Version Version;
    public string Url, Sha256, Signature, Published;
    public List<string> Notes = new();
}

// Over-the-air updates. The feed (update.json) says which version is newest and where its installer is.
// An update is only run if its SHA-256 matches the feed AND it carries a valid signature from the
// Snapline update key (see tools\new-update-key.ps1). Nothing installs without the user's OK.
static class Updater
{
    /// Public feed. Every release overwrites update.json, so this URL always points at the newest one.
    public const string DefaultFeed = "https://github.com/Coursion-Studio/Snapline/releases/latest/download/update.json";

    public static string FeedUrl => string.IsNullOrEmpty(UpdateSettings.UpdateFeed) ? DefaultFeed : UpdateSettings.UpdateFeed;

    public static Version Current => Norm(typeof(Updater).Assembly.GetName().Version);

    public static string CurrentText { get { var v = Current; return v.Major + "." + v.Minor + "." + v.Build; } }

    internal static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    static HttpClient Client()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Snapline-Windows/" + CurrentText);
        return c;
    }

    /// Reads bytes from an http(s) or file:/// URL (file feeds are for local testing).
    static byte[] Fetch(string url, Action<int> progress = null)
    {
        var uri = new Uri(url);
        if (uri.IsFile) { var b = File.ReadAllBytes(uri.LocalPath); progress?.Invoke(100); return b; }
        using var c = Client();
        using var resp = c.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;
        using var s = resp.Content.ReadAsStream();
        var ms = new MemoryStream();
        var buf = new byte[81920];
        int n, last = -1;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            ms.Write(buf, 0, n);
            if (total > 0 && progress != null)
            {
                int pct = (int)(ms.Length * 100 / total);
                if (pct != last) { last = pct; progress(pct); }
            }
        }
        return ms.ToArray();
    }

    /// Reads the feed. Returns the update if it's newer than this copy, otherwise null. Throws on network/feed errors.
    public static UpdateInfo Check()
    {
        var info = ParseFeed(Fetch(FeedUrl));
        UpdateSettings.LastUpdateCheck = DateTime.UtcNow.Ticks;
        UpdateSettings.Save();
        return info.Version > Current ? info : null;
    }

    /// Parses update.json. Throws InvalidDataException if it's incomplete.
    internal static UpdateInfo ParseFeed(byte[] raw)
    {
        int skip = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF ? 3 : 0; // Windows PowerShell writes a BOM
        using var doc = JsonDocument.Parse(raw.AsMemory(skip));
        var r = doc.RootElement;
        string S(string k) => r.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        var info = new UpdateInfo
        {
            Version = Norm(new Version(S("version") ?? throw new InvalidDataException("The update feed has no version."))),
            Url = S("url"), Sha256 = S("sha256"), Signature = S("signature"), Published = S("published"),
        };
        if (r.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Array)
            foreach (var n in notes.EnumerateArray()) info.Notes.Add(n.ToString());
        if (info.Url == null || info.Sha256 == null || info.Signature == null) throw new InvalidDataException("The update feed is incomplete.");
        return info;
    }
    /// Downloads the installer to %TEMP% and verifies it. Returns its path, or throws if anything doesn't match.
    public static string Download(UpdateInfo u, Action<int> progress)
    {
        string dir = Path.Combine(Path.GetTempPath(), "Snapline-update");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "SnaplineSetup-" + u.Version + ".exe");
        if (File.Exists(path)) File.Delete(path);
        var data = Fetch(u.Url, progress);
        Verify(data, u);
        File.WriteAllBytes(path, data);
        return path;
    }

    public static void Verify(byte[] data, UpdateInfo u) => Verify(data, u, UpdateKey.PublicXml);

    /// Checks the SHA-256 and the RSA signature against the given public key.
    internal static void Verify(byte[] data, UpdateInfo u, string publicKeyXml)
    {
        string hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        if (!string.Equals(hash, u.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("The download is damaged (checksum mismatch). Nothing was installed.");
        using var rsa = RSA.Create();
        rsa.FromXmlString(publicKeyXml);
        byte[] sig;
        try { sig = Convert.FromBase64String(u.Signature); }
        catch (FormatException) { throw new CryptographicException("The update isn't signed correctly. Nothing was installed."); }
        if (!rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("The update isn't signed by Coursion Studio. Nothing was installed.");
    }

    /// Runs the verified installer silently; it closes this copy, updates, and reopens the app.
    public static void Install(string setupPath)
    {
        bool startup = false;
        try { startup = UpdateHost.StartupEnabled(); } catch { }
        Process.Start(new ProcessStartInfo(setupPath, "/S /launch" + (startup ? " /startup" : "")) { UseShellExecute = true });
        UpdateHost.Quit();
    }
}
