using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OptiGames.Core.Services;

public sealed record SupportAttachment(string Id, string Name, long Bytes);

public sealed record SupportResult(bool Ok, string? ThreadId = null, string? Error = null);

/// <summary>
/// Files a bug report into the same support inbox the website uses, rather than opening a browser
/// and hoping the user finishes the job somewhere else.
///
/// This talks to the existing public endpoints — nothing here is a new backend. Images are staged
/// one at a time through /api/support/attachment, which returns an id, and those ids are then
/// attached to a thread created by /api/support. Staging first is what lets the server reject an
/// oversized or non-image file before any thread exists, so a rejected screenshot cannot leave a
/// half-written report in the inbox.
/// </summary>
public sealed class SupportService
{
    /// <summary>
    /// Where reports go. The support API lives on the beta host; when the site moves onto the
    /// main domain this is the only line that changes.
    /// </summary>
    public const string BaseUrl = "https://optigamesbeta.online";

    /// <summary>Matches ALLOWED in the site's lib/supportUploads.ts.</summary>
    public static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif" };

    /// <summary>Matches MAX_BYTES server-side. Checked here too so an oversized file fails
    /// instantly and locally instead of after an eight megabyte upload.</summary>
    public const long MaxAttachmentBytes = 8 * 1024 * 1024;

    // One client for the process. A new HttpClient per call exhausts sockets under retry.
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    private readonly ILogSink _log;
    public SupportService(ILogSink log) => _log = log;

    public static bool IsAllowedImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return AllowedExtensions.Contains(ext);
    }

    /// <summary>
    /// Stages one image and returns its id, or null with the reason logged. The server sniffs the
    /// actual bytes, so renaming something to .png will still be refused there.
    /// </summary>
    public async Task<SupportAttachment?> UploadAsync(string path, CancellationToken cancel = default)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                _log.Write($"  attachment missing: {path}");
                return null;
            }

            if (info.Length > MaxAttachmentBytes)
            {
                _log.Write($"  {info.Name} is over the 8MB limit and was skipped.");
                return null;
            }

            using var content = new ByteArrayContent(await File.ReadAllBytesAsync(path, cancel));
            content.Headers.ContentType = new MediaTypeHeaderValue(MimeFor(path));

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/support/attachment")
            {
                Content = content,
            };
            // The server reads the name from this header rather than a multipart envelope.
            req.Headers.TryAddWithoutValidation("x-file-name", Uri.EscapeDataString(info.Name));

            using var res = await Http.SendAsync(req, cancel);
            var body = await res.Content.ReadAsStringAsync(cancel);

            if (!res.IsSuccessStatusCode)
            {
                _log.Write($"  upload rejected ({(int)res.StatusCode}): {MessageFrom(body)}");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new SupportAttachment(
                root.GetProperty("id").GetString() ?? "",
                root.TryGetProperty("name", out var n) ? n.GetString() ?? info.Name : info.Name,
                root.TryGetProperty("bytes", out var b) ? b.GetInt64() : info.Length);
        }
        catch (Exception ex)
        {
            _log.Write($"  attachment upload failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Creates the thread. Category is fixed to the site's "technical" key.</summary>
    public async Task<SupportResult> SubmitAsync(
        string email,
        string message,
        IEnumerable<string> attachmentIds,
        CancellationToken cancel = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                category = "technical",
                email,
                message,
                attachmentIds = attachmentIds.ToArray(),
                // The honeypot field, deliberately empty. The server treats anything in it as a
                // bot and silently discards the report, so it must be sent blank rather than
                // omitted-and-guessed-at later.
                website = "",
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var res = await Http.PostAsync($"{BaseUrl}/api/support", content, cancel);
            var body = await res.Content.ReadAsStringAsync(cancel);

            if (!res.IsSuccessStatusCode)
            {
                var reason = MessageFrom(body);
                _log.Write($"Bug report rejected ({(int)res.StatusCode}): {reason}");
                return new SupportResult(false, Error: reason);
            }

            using var doc = JsonDocument.Parse(body);
            var id = doc.RootElement.TryGetProperty("threadId", out var t) ? t.GetString() : null;
            _log.Write("Bug report sent.");
            return new SupportResult(true, id);
        }
        catch (TaskCanceledException)
        {
            return new SupportResult(false, Error: "The request timed out. Check your connection and try again.");
        }
        catch (Exception ex)
        {
            _log.Write($"Bug report failed: {ex.Message}");
            return new SupportResult(false, Error: "Could not reach the support server.");
        }
    }

    /// <summary>The API answers errors as {"error":"...","message":"..."}; prefer the human one.</summary>
    private static string MessageFrom(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m)) return m.GetString() ?? "Unknown error.";
            if (doc.RootElement.TryGetProperty("error", out var e)) return e.GetString() ?? "Unknown error.";
        }
        catch { /* not JSON; fall through */ }
        return "Unknown error.";
    }

    private static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream",
    };
}
