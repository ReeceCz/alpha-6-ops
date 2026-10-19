using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace Alpha6Ops.Server;

/// <summary>
/// Private desktop builds in a Cloudflare R2 bucket, downloadable by staff only. Off until all four keys are set.
/// The bucket stays private: the portal lists it and hands out short-lived signed links.
/// </summary>
public sealed class PreviewBuildSettings
{
    public string AccountId { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
    public string Prefix { get; set; } = "preview/";
    public bool Enabled => !string.IsNullOrWhiteSpace(AccountId) && !string.IsNullOrWhiteSpace(Bucket)
        && !string.IsNullOrWhiteSpace(AccessKeyId) && !string.IsNullOrWhiteSpace(SecretAccessKey);
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);
}

public sealed record PreviewBuild(string Key, string Name, long Size, DateTimeOffset Uploaded);

public interface IPreviewBuildStore
{
    bool Enabled { get; }
    /// <summary>Builds under the prefix, newest first. Throws <see cref="PreviewBuildStoreException"/> when the store can't be read.</summary>
    Task<PreviewBuild[]> ListAsync(CancellationToken ct);
    Uri DownloadUrl(PreviewBuild build);
}

public sealed class PreviewBuildStoreException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class R2PreviewBuildStore(HttpClient http, IOptions<PreviewBuildSettings> options, TimeProvider time, ILogger<R2PreviewBuildStore> log) : IPreviewBuildStore
{
    private static readonly XNamespace S3 = "http://s3.amazonaws.com/doc/2006-03-01/";
    private PreviewBuildSettings Settings => options.Value;
    private string Host => $"{Settings.AccountId.Trim()}.r2.cloudflarestorage.com";
    public bool Enabled => Settings.Enabled;

    public async Task<PreviewBuild[]> ListAsync(CancellationToken ct)
    {
        var url = Sign("GET", "/" + Settings.Bucket.Trim(), new() { ["list-type"] = "2", ["prefix"] = Settings.Prefix });
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("Listing preview builds failed with {Status}.", (int)response.StatusCode);
                throw new PreviewBuildStoreException($"The build store answered {(int)response.StatusCode}. Check the PreviewBuilds settings.");
            }
            return Parse(await response.Content.ReadAsStringAsync(ct), Settings.Prefix);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            log.LogWarning(ex, "Listing preview builds failed.");
            throw new PreviewBuildStoreException("The build store didn't answer. Try again in a minute.", ex);
        }
    }

    public Uri DownloadUrl(PreviewBuild build) => Sign("GET", "/" + Settings.Bucket.Trim() + "/" + build.Key,
        new() { ["response-content-disposition"] = $"attachment; filename=\"{build.Name}\"" });

    private Uri Sign(string method, string path, Dictionary<string, string> query) =>
        SigV4.Presign(method, Host, path, query, "auto", "s3", Settings.AccessKeyId.Trim(), Settings.SecretAccessKey.Trim(), time.GetUtcNow(), PreviewBuildSettings.LinkLifetime);

    /// <summary>Reads a ListObjectsV2 answer. Folder placeholders (keys ending in '/') are skipped.</summary>
    public static PreviewBuild[] Parse(string xml, string prefix) =>
        XDocument.Parse(xml).Root!.Elements(S3 + "Contents")
            .Select(c => (Key: (string?)c.Element(S3 + "Key") ?? "", Size: (long?)c.Element(S3 + "Size") ?? 0, Modified: (string?)c.Element(S3 + "LastModified")))
            .Where(c => c.Key.StartsWith(prefix, StringComparison.Ordinal) && !c.Key.EndsWith('/'))
            .Select(c => new PreviewBuild(c.Key, c.Key[(c.Key.LastIndexOf('/') + 1)..], c.Size,
                DateTimeOffset.TryParse(c.Modified, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : DateTimeOffset.MinValue))
            .OrderByDescending(b => b.Uploaded)
            .ToArray();
}

/// <summary>AWS Signature Version 4 query-string signing (presigned URLs), as R2's S3 API accepts it.</summary>
public static class SigV4
{
    public static Uri Presign(string method, string host, string path, IReadOnlyDictionary<string, string> query, string region, string service,
        string accessKeyId, string secretAccessKey, DateTimeOffset now, TimeSpan expires)
    {
        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var day = amzDate[..8];
        var scope = $"{day}/{region}/{service}/aws4_request";
        var parameters = new SortedDictionary<string, string>(query.ToDictionary(), StringComparer.Ordinal)
        {
            ["X-Amz-Algorithm"] = "AWS4-HMAC-SHA256",
            ["X-Amz-Credential"] = $"{accessKeyId}/{scope}",
            ["X-Amz-Date"] = amzDate,
            ["X-Amz-Expires"] = ((int)expires.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            ["X-Amz-SignedHeaders"] = "host",
        };
        var canonicalPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        var canonicalQuery = string.Join('&', parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        var canonicalRequest = $"{method}\n{canonicalPath}\n{canonicalQuery}\nhost:{host}\n\nhost\nUNSIGNED-PAYLOAD";
        var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)))}";
        var key = Encoding.UTF8.GetBytes("AWS4" + secretAccessKey);
        foreach (var part in new[] { day, region, service, "aws4_request" }) key = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(part));
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(stringToSign)));
        return new Uri($"https://{host}{canonicalPath}?{canonicalQuery}&X-Amz-Signature={signature}");
    }
}
