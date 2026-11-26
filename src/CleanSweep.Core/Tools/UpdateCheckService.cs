using System.Net.Http.Headers;
using System.Text.Json;

namespace CleanSweep.Core.Tools;

public sealed class UpdateCheckResult
{
    public bool Success { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string ReleaseUrl { get; init; } = "https://github.com/edizdar/CleanSweep/releases";
    public string Message { get; init; } = string.Empty;
}

public static class UpdateCheckService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/edizdar/CleanSweep/releases/latest";

    public static bool IsNewerVersion(string currentVersion, string latestVersion)
    {
        if (!TryParseNormalizedVersion(currentVersion, out var cur) ||
            !TryParseNormalizedVersion(latestVersion, out var lat))
        {
            return false;
        }

        return lat > cur;
    }

    public static async Task<UpdateCheckResult> CheckLatestReleaseAsync(
        string currentVersion,
        CancellationToken ct = default)
    {
        string normalizedCurrent = NormalizeVersionText(currentVersion);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CleanSweep", normalizedCurrent.TrimStart('v')));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var resp = await client.GetAsync(LatestReleaseApiUrl, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    CurrentVersion = normalizedCurrent,
                    Message = $"Güncelleme sunucusuna ulaşılamadı (HTTP {(int)resp.StatusCode})."
                };
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            string tag = doc.RootElement.TryGetProperty("tag_name", out var tagEl)
                ? tagEl.GetString() ?? normalizedCurrent
                : normalizedCurrent;
            string htmlUrl = doc.RootElement.TryGetProperty("html_url", out var urlEl)
                ? urlEl.GetString() ?? "https://github.com/edizdar/CleanSweep/releases"
                : "https://github.com/edizdar/CleanSweep/releases";

            string normalizedLatest = NormalizeVersionText(tag);
            bool newer = IsNewerVersion(normalizedCurrent, normalizedLatest);

            return new UpdateCheckResult
            {
                Success = true,
                IsUpdateAvailable = newer,
                CurrentVersion = normalizedCurrent,
                LatestVersion = normalizedLatest,
                ReleaseUrl = htmlUrl,
                Message = newer
                    ? $"Yeni sürüm mevcut: {normalizedLatest} (mevcut: {normalizedCurrent})."
                    : $"CleanSweep güncel ({normalizedCurrent})."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = normalizedCurrent,
                Message = $"Güncelleme denetlenemedi (internet bağlantısı yok veya sınır aşıldı): {ex.Message}"
            };
        }
    }

    private static string NormalizeVersionText(string? raw)
    {
        string t = (raw ?? "0.0.0").Trim();
        if (!t.StartsWith('v') && !t.StartsWith('V'))
            t = "v" + t;
        return t;
    }

    private static bool TryParseNormalizedVersion(string? raw, out Version parsed)
    {
        parsed = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string cleaned = raw.Trim().TrimStart('v', 'V');
        int dashIdx = cleaned.IndexOf('-');
        if (dashIdx > 0) cleaned = cleaned[..dashIdx];
        string[] parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2) cleaned += ".0";
        return Version.TryParse(cleaned, out parsed!);
    }
}
