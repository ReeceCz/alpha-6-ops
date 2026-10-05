using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Alpha6Ops.Server;

/// <summary>Cloudflare Turnstile for the public invitation request form. Off until both keys are set.</summary>
public sealed class TurnstileSettings
{
    public string SiteKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public bool Enabled => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(SecretKey);
    public const string Origin = "https://challenges.cloudflare.com";
}

public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct);
}

/// <summary>Server-side check of a Turnstile token (https://developers.cloudflare.com/turnstile/get-started/server-side-validation/).</summary>
public sealed class CloudflareTurnstileVerifier(HttpClient http, IOptions<TurnstileSettings> settings, ILogger<CloudflareTurnstileVerifier> log) : ITurnstileVerifier
{
    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return false;
        var form = new Dictionary<string, string> { ["secret"] = settings.Value.SecretKey, ["response"] = token };
        if (!string.IsNullOrEmpty(remoteIp)) form["remoteip"] = remoteIp;
        try
        {
            using var response = await http.PostAsync(TurnstileSettings.Origin + "/turnstile/v0/siteverify", new FormUrlEncodedContent(form), ct);
            var result = await response.Content.ReadFromJsonAsync<SiteverifyResult>(ct);
            return response.IsSuccessStatusCode && result?.Success == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Fail closed: without a verdict the request isn't accepted, and the visitor can try again.
            log.LogWarning(ex, "Turnstile verification failed.");
            return false;
        }
    }

    private sealed record SiteverifyResult(bool Success);
}

/// <summary>Three invitation requests per visitor address per hour, on top of the general request limits.</summary>
public sealed class InviteRequestLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = 3, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));

    public bool TryAcquire(string? address)
    {
        using var lease = limiter.AttemptAcquire(address ?? "unknown");
        return lease.IsAcquired;
    }

    public void Dispose() => limiter.Dispose();
}
