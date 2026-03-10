using System.Security.Cryptography;
using System.Text.Json;

namespace invoice_v1.src.Application.Security
{
    public record VirusTotalResult(
        bool IsClean,
        bool IsUnknown,
        string Hash,
        int MaliciousEngines,
        int SuspiciousEngines,
        int TotalEngines,
        string Message);

    public class VirusTotalScanner
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly ILogger<VirusTotalScanner> _logger;
        private readonly string _baseUrl; // absolute base URL we will use

        public VirusTotalScanner(HttpClient httpClient, IConfiguration configuration, ILogger<VirusTotalScanner> logger)
        {
            _httpClient = httpClient;
            _apiKey = configuration["VirusTotal:ApiKey"] ?? string.Empty;
            _logger = logger;
            // prefer explicit config; fallback to virustotal host
            _baseUrl = (configuration["VirusTotal:BaseUrl"] ?? "https://www.virustotal.com").TrimEnd('/');
        }

        public async Task<VirusTotalResult> ScanAsync(Stream fileStream, string fileName)
        {
            // Compute SHA256 hash safely (handles non-seekable streams)
            var hash = ComputeSha256Hash(fileStream);
            try { fileStream.Position = 0; } catch { /* ignore if non-seekable */ }

            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("VirusTotal API key not configured. Skipping scan for {FileName}", fileName);
                return new VirusTotalResult(true, true, hash, 0, 0, 0, "API key not configured — skipped");
            }

            try
            {
                // Build an absolute request URI to avoid InvalidOperationException
                var requestUri = $"{_baseUrl}/api/v3/files/{hash}";

                // Debug log to confirm what's being used
                _logger.LogDebug("VirusTotal scan: HttpClient.BaseAddress={BaseAddress}; RequestUri={RequestUri}",
                    _httpClient.BaseAddress?.ToString() ?? "<null>", requestUri);

                var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Add("x-apikey", _apiKey);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation(
                        "VirusTotal: Hash {Hash} for {FileName} not found in database — treating as clean",
                        hash, fileName);
                    return new VirusTotalResult(true, true, hash, 0, 0, 0, "Hash not found in VirusTotal DB");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await SafeReadResponseBody(response);
                    _logger.LogWarning(
                        "VirusTotal API returned {StatusCode} for hash {Hash}. Failing open. Body: {Body}",
                        response.StatusCode, hash, body);
                    return new VirusTotalResult(true, false, hash, 0, 0, 0,
                        $"API returned {response.StatusCode} — failing open");
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var stats = root
                    .GetProperty("data")
                    .GetProperty("attributes")
                    .GetProperty("last_analysis_stats");

                int malicious = stats.GetProperty("malicious").GetInt32();
                int suspicious = stats.GetProperty("suspicious").GetInt32();
                int total = 0;
                foreach (var prop in stats.EnumerateObject())
                {
                    total += prop.Value.GetInt32();
                }

                bool isClean = malicious == 0 && suspicious <= 2;

                if (!isClean)
                {
                    _logger.LogWarning(
                        "VirusTotal: File {FileName} flagged — malicious: {Malicious}, suspicious: {Suspicious}, total engines: {Total}",
                        fileName, malicious, suspicious, total);
                }
                else
                {
                    _logger.LogInformation(
                        "VirusTotal: File {FileName} is clean — {Total} engines scanned",
                        fileName, total);
                }

                var message = isClean
                    ? $"Clean — {total} engines scanned"
                    : $"Flagged — malicious: {malicious}, suspicious: {suspicious}";

                return new VirusTotalResult(isClean, false, hash, malicious, suspicious, total, message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "VirusTotal API error for file {FileName}. Failing open", fileName);
                return new VirusTotalResult(true, false, hash, 0, 0, 0,
                    $"API error — failing open: {ex.Message}");
            }
        }

        private static string ComputeSha256Hash(Stream stream)
        {
            // handle non-seekable streams by copying to a MemoryStream
            if (!stream.CanSeek)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                using var sha = SHA256.Create();
                var hashBytes = sha.ComputeHash(ms);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
            else
            {
                var orig = stream.Position;
                stream.Position = 0;
                using var sha = SHA256.Create();
                var hashBytes = sha.ComputeHash(stream);
                stream.Position = orig;
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        private static async Task<string> SafeReadResponseBody(HttpResponseMessage resp)
        {
            try
            {
                return await resp.Content.ReadAsStringAsync();
            }
            catch
            {
                return "<unreadable body>";
            }
        }
    }
}