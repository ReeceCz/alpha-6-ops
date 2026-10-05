using System.Net;
using System.Net.Http.Json;
using Alpha6Ops.Accounts;

namespace Alpha6Ops.Server;

/// <summary>
/// Deletes sign-in identities from Auth0 through the Management API. Off until Auth0:ManagementClientId and
/// Auth0:ManagementClientSecret are set for a machine-to-machine application granted only delete:users.
/// </summary>
public sealed class Auth0ManagementClient(HttpClient http, ServerSettings settings, IConfiguration config)
{
    private readonly string clientId = config["Auth0:ManagementClientId"] ?? "";
    private readonly string clientSecret = config["Auth0:ManagementClientSecret"] ?? "";
    public bool IsConfigured => settings.IsConfigured && clientId.Length > 0 && clientSecret.Length > 0;

    /// <summary>True when Auth0 no longer has the user, including when it was already gone.</summary>
    public async Task<bool> DeleteUserAsync(string subject, CancellationToken ct)
    {
        var token = await http.PostAsJsonAsync(new Uri(settings.Authority + "oauth/token"), new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["client_secret"] = clientSecret,
            ["audience"] = settings.Authority + "api/v2/"
        }, ct);
        token.EnsureSuccessStatusCode();
        var accessToken = (await token.Content.ReadFromJsonAsync<TokenResponse>(ct))?.access_token
            ?? throw new InvalidOperationException("Auth0 returned no management token.");
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(settings.Authority + "api/v2/users/" + Uri.EscapeDataString(subject)));
        request.Headers.Authorization = new("Bearer", accessToken);
        using var response = await http.SendAsync(request, ct);
        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound;
    }

    private sealed record TokenResponse(string access_token);
}

/// <summary>
/// Hourly: erases accounts whose deletion grace period has ended, then removes their Auth0 sign-in when the
/// Management API is configured. Otherwise the identities wait in the queue for manual removal (docs/hosting.md).
/// </summary>
public sealed class AccountDeletionJob(IServiceScopeFactory scopes, Auth0ManagementClient auth0, ServerSettings settings, ILogger<AccountDeletionJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let start-up, migrations and health checks settle before the first pass.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Account deletion pass failed; it will retry next hour."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsService>();
        var erased = await accounts.PurgeDueDeletionsAsync(ct);
        if (erased > 0) log.LogInformation("Erased {Count} account(s) after their deletion grace period.", erased);
        var staleRequests = await accounts.PurgeStaleInviteRequestsAsync(ct);
        if (staleRequests > 0) log.LogInformation("Deleted {Count} unanswered invitation request(s) older than {Days} days.", staleRequests, InviteRequestRules.RetentionDays);
        var pending = await accounts.PendingIdentityRemovalsAsync(ct);
        if (pending.Length == 0) return;
        if (!auth0.IsConfigured)
        {
            log.LogWarning("{Count} deleted account(s) still have an Auth0 sign-in. Remove them by hand or configure the Management API (docs/hosting.md).", pending.Length);
            return;
        }
        foreach (var identity in pending.Where(x => x.Issuer == settings.Authority))
        {
            if (await auth0.DeleteUserAsync(identity.Subject, ct)) await accounts.MarkIdentityRemovedAsync(identity.UserId, ct);
            else log.LogWarning("Auth0 refused to delete the sign-in for account {UserId}; retrying next hour.", identity.UserId);
        }
    }
}
