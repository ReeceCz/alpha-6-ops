using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alpha6Ops.Identity;
using Duende.IdentityModel.Client;
using Duende.IdentityModel.OidcClient;

namespace Alpha6Ops.Desktop;

internal sealed record SavedAccountSession(string ConfigurationKey, string? RefreshToken, BootstrapResponse Bootstrap,
    DateTimeOffset ReceivedAt, DateTimeOffset LastSeenAt, WorkspaceSelection Workspace);

// Only these application-authored messages are safe to show in the account window. Code carries the
// server's stable problem code (or a local one such as "offline") so callers can branch without parsing text.
internal sealed class AccountSessionException(string message, string code = "") : InvalidOperationException(message)
{
    internal string Code { get; } = code;
}
internal sealed record ProblemBody(string? Title, string? Code);
internal sealed record BrowserLoginResult(string AccessToken, string? RefreshToken, bool IsError = false, string? Error = null);
// Success means the session is established. Otherwise MfaToken carries the provider's challenge token; when
// NeedsEnrollment is set the pilot has no authenticator yet and must add one before the code is accepted.
internal sealed record PasswordLoginOutcome(bool Success, string? MfaToken, bool NeedsEnrollment);
internal sealed record OtpEnrollment(string Secret, string BarcodeUri, string[] RecoveryCodes);
internal enum StepUpChoice { Cancelled, Browser, Completed }
internal sealed record RememberedLogin(string Email, string? Password)
{
    internal static RememberedLogin None { get; } = new("", null);
}

internal sealed class DesktopAccountSession
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string sessionPath;
    private readonly string configurationKey;
    private readonly string rootDirectory;
    private readonly Func<HttpMessageHandler>? handlerFactory;
    private readonly Func<LoginRequest, CancellationToken, Task<BrowserLoginResult>> login;
    private string? accessToken;
    private SavedAccountSession? saved;
    internal IdentityConfiguration Configuration { get; }
    internal BootstrapResponse? Bootstrap => saved?.Bootstrap;
    internal WorkspaceSelection Workspace => saved?.Workspace ?? WorkspaceSelection.Personal;
    internal bool IsOffline { get; private set; }
    internal bool CanRefresh => !string.IsNullOrEmpty(saved?.RefreshToken) || !string.IsNullOrEmpty(accessToken);
    internal bool MayStartNewFlight(DateTimeOffset now) => saved is not null
        && OfflineSessionPolicy.MayOpen(saved.Bootstrap, saved.ReceivedAt, saved.LastSeenAt, now);
    internal string WorkspaceName => Workspace.AirlineId is { } id
        ? Bootstrap?.Airlines.FirstOrDefault(a => a.Id == id)?.Name ?? "Flying as a Pilot" : "Flying as a Pilot";
    internal string DataDirectory => Path.Combine(rootDirectory, "accounts", Bootstrap!.Account.Id.ToString("N"),
        Workspace.AirlineId is { } id ? Path.Combine("airlines", id.ToString("N")) : "personal");
    internal UserProfile Profile => Bootstrap?.Profile ?? UserProfile.Default;
    internal static string ClientVersion { get; } = typeof(DesktopAccountSession).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    internal const string MultiFactorPolicy = "http://schemas.openid.net/pape/policies/2007/06/multi-factor";

    internal DesktopAccountSession(IdentityConfiguration configuration, string? directory = null, Func<HttpMessageHandler>? httpHandlerFactory = null,
        Func<LoginRequest, CancellationToken, Task<BrowserLoginResult>>? browserLogin = null)
    {
        Configuration = configuration;
        rootDirectory = directory ?? CrashReporter.RootDirectory;
        handlerFactory = httpHandlerFactory;
        login = browserLogin ?? LoginInBrowserAsync;
        configurationKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(configuration))));
        sessionPath = Path.Combine(rootDirectory, "Identity", "session.bin");
    }

    private OidcClient Client(SystemLoginBrowser? browser = null) => new(new OidcClientOptions
    {
        Authority = Configuration.Authority, ClientId = Configuration.ClientId,
        Scope = "openid profile email offline_access", RedirectUri = browser?.RedirectUri ?? "http://127.0.0.1/callback/",
        Browser = browser, LoadProfile = false
    });

    private async Task<BrowserLoginResult> LoginInBrowserAsync(LoginRequest request, CancellationToken token)
    {
        using var browser = new SystemLoginBrowser(Configuration.CallbackPort, portalUrl: Configuration.PortalUrl);
        var result = await Client(browser).LoginAsync(request, token);
        token.ThrowIfCancellationRequested();
        if (browser.LastResultType == Duende.IdentityModel.OidcClient.Browser.BrowserResultType.Timeout)
            throw new AccountSessionException("Sign-in timed out. Try again and complete sign-in in your browser within three minutes.");
        // The provider's own error text is the only way to diagnose a failed code exchange; it is shown, never logged.
        var detail = result.IsError ? string.Join(": ", new[] { result.Error, result.ErrorDescription }.Where(x => !string.IsNullOrWhiteSpace(x))) : null;
        return new(result.AccessToken, result.RefreshToken, result.IsError, detail);
    }

    internal async Task LoginAsync(CancellationToken token, string? loginHint = null, bool createAccount = false)
    {
        await gate.WaitAsync(token);
        try
        {
            Clear(); // Account changes always require a fresh verified network response.
            var parameters = new System.Collections.Generic.Dictionary<string, string> { ["audience"] = Configuration.Audience, ["prompt"] = "login" };
            if (!string.IsNullOrWhiteSpace(loginHint)) parameters["login_hint"] = loginHint;
            if (createAccount) parameters["screen_hint"] = "signup";
            var result = await login(new LoginRequest
            { FrontChannelExtraParameters = new Parameters(parameters) }, token);
            token.ThrowIfCancellationRequested();
            if (result.IsError || string.IsNullOrWhiteSpace(result.AccessToken))
                throw new AccountSessionException(BrowserFailure(result), "browser_login_failed");
            await EstablishAsync(result.AccessToken, result.RefreshToken, token, preserveWorkspace: false);
        }
        finally { gate.Release(); }
    }

    private static string BrowserFailure(BrowserLoginResult result)
    {
        if (result.Error is not { Length: > 0 } error) return "Sign-in was canceled or could not be verified. Try again in your browser.";
        if (error.Contains("access_denied", StringComparison.OrdinalIgnoreCase) && error.Contains("consent", StringComparison.OrdinalIgnoreCase)) return "Sign-in was declined at the consent screen. Try again and choose Accept.";
        if (error.Contains("unauthorized_client", StringComparison.OrdinalIgnoreCase)) return "This installation's sign-in client is not allowed to use browser sign-in. The provider said: " + Trim(error);
        if (error.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)) return "The browser sign-in could not be completed by the app (the sign-in code was rejected). Try again; if it repeats, the provider said: " + Trim(error);
        return "The browser sign-in finished but the app could not complete it. The provider said: " + Trim(error);
    }
    private static string Trim(string text) => text.Length > 220 ? text[..220] + "…" : text;

    internal async Task<bool> RestoreAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { return await RestoreCoreAsync(token); }
        finally { gate.Release(); }
    }

    // Re-authenticates the same account with a fresh multi-factor proof. The current session and
    // workspace survive a cancelled or failed attempt; only a verified same-account result replaces them.
    internal async Task StepUpAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (saved is null) throw new AccountSessionException("Sign in to continue.", "signed_out");
            var parameters = new System.Collections.Generic.Dictionary<string, string>
            {
                ["audience"] = Configuration.Audience, ["prompt"] = "login", ["max_age"] = "0",
                ["acr_values"] = MultiFactorPolicy, ["login_hint"] = saved.Bootstrap.Account.Email
            };
            var result = await login(new LoginRequest { FrontChannelExtraParameters = new Parameters(parameters) }, token);
            token.ThrowIfCancellationRequested();
            if (result.IsError || string.IsNullOrWhiteSpace(result.AccessToken))
                throw new AccountSessionException("Identity confirmation was canceled or could not be verified. " + (result.Error is { Length: > 0 } e ? $"The provider said: {Trim(e)}" : "Try again."), "step_up_failed");
            await EstablishAsync(result.AccessToken, result.RefreshToken, token);
        }
        finally { gate.Release(); }
    }

    // In-app sign-in: Auth0's password-realm grant plus the MFA API, so email/password pilots never leave the
    // window. Passkeys and Microsoft/Google still go through the browser. Passwords are used for the one
    // request and never stored, logged or kept in the session file.
    internal async Task<PasswordLoginOutcome> PasswordLoginAsync(string email, string password, bool stepUp, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (stepUp && saved is null) throw new AccountSessionException("Sign in to continue.", "signed_out");
            if (!stepUp) Clear();
            var form = new System.Collections.Generic.Dictionary<string, string>
            {
                ["grant_type"] = "http://auth0.com/oauth/grant-type/password-realm", ["client_id"] = Configuration.ClientId,
                ["username"] = email.Trim(), ["password"] = password, ["realm"] = Configuration.DatabaseConnection,
                ["audience"] = Configuration.Audience, ["scope"] = "openid profile email offline_access"
            };
            if (stepUp) form["acr_values"] = MultiFactorPolicy;
            var (status, json) = await PostFormAsync("/oauth/token", form, token);
            if (status == HttpStatusCode.OK) { await EstablishTokensAsync(json, stepUp, token); return new(true, null, false); }
            var error = json.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
            if (error == "mfa_required" && json.TryGetProperty("mfa_token", out var mfaToken) && mfaToken.GetString() is { Length: > 0 } mfa)
                return new(false, mfa, !await HasOtpAuthenticatorAsync(mfa, token));
            throw ProviderError(status, error, json);
        }
        finally { gate.Release(); }
    }

    internal async Task<OtpEnrollment> BeginOtpEnrollmentAsync(string mfaToken, CancellationToken token)
    {
        using var client = Http();
        using var request = new HttpRequestMessage(HttpMethod.Post, Configuration.Authority.TrimEnd('/') + "/mfa/associate")
        { Content = JsonContent.Create(new { authenticator_types = new[] { "otp" } }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mfaToken);
        using var response = await client.SendAsync(request, token);
        var json = await ReadJsonAsync(response, token);
        if (!response.IsSuccessStatusCode) throw ProviderError(response.StatusCode, json.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "", json);
        var codes = json.TryGetProperty("recovery_codes", out var rc) && rc.ValueKind == JsonValueKind.Array ? rc.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray() : [];
        return new(json.GetProperty("secret").GetString() ?? "", json.TryGetProperty("barcode_uri", out var uri) ? uri.GetString() ?? "" : "", codes);
    }

    // Completes an in-app MFA challenge or enrolment with an authenticator code, or a recovery code. When a
    // recovery code is used, Auth0 issues a replacement that is returned once and must be shown to the pilot.
    internal async Task<string?> CompleteMfaAsync(string mfaToken, string code, bool recoveryCode, bool stepUp, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var form = new System.Collections.Generic.Dictionary<string, string> { ["client_id"] = Configuration.ClientId, ["mfa_token"] = mfaToken };
            var clean = new string(code.Where(char.IsLetterOrDigit).ToArray());
            if (recoveryCode) { form["grant_type"] = "http://auth0.com/oauth/grant-type/mfa-recovery-code"; form["recovery_code"] = clean; }
            else { form["grant_type"] = "http://auth0.com/oauth/grant-type/mfa-otp"; form["otp"] = clean; }
            var (status, json) = await PostFormAsync("/oauth/token", form, token);
            if (status != HttpStatusCode.OK) throw ProviderError(status, json.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "", json);
            await EstablishTokensAsync(json, stepUp, token);
            return json.TryGetProperty("recovery_code", out var replacement) ? replacement.GetString() : null;
        }
        finally { gate.Release(); }
    }

    internal async Task SignUpAsync(string email, string password, CancellationToken token)
    {
        using var client = Http();
        using var response = await client.PostAsJsonAsync(Configuration.Authority.TrimEnd('/') + "/dbconnections/signup",
            new { client_id = Configuration.ClientId, email = email.Trim(), password, connection = Configuration.DatabaseConnection }, token);
        if (response.IsSuccessStatusCode) return;
        var json = await ReadJsonAsync(response, token);
        var code = json.TryGetProperty("code", out var c) ? c.GetString() ?? "" : json.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var description = json.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : json.TryGetProperty("message", out var m) ? m.GetString() : null;
        throw code switch
        {
            "invalid_signup" or "user_exists" => new AccountSessionException("An account with this email already exists. Sign in instead, or reset your password.", "user_exists"),
            "invalid_password" or "PasswordStrengthError" => new AccountSessionException("Choose a stronger password: at least 8 characters with letters, numbers and symbols.", "invalid_password"),
            "password_dictionary_error" or "PasswordDictionaryError" => new AccountSessionException("That password is too common. Choose another.", "invalid_password"),
            _ => new AccountSessionException(description is { Length: > 0 } && description.Length < 160 ? description : "The account could not be created. Try again or use your browser.", code)
        };
    }

    internal async Task RequestPasswordResetAsync(string email, CancellationToken token)
    {
        using var client = Http();
        using var response = await client.PostAsJsonAsync(Configuration.Authority.TrimEnd('/') + "/dbconnections/change_password",
            new { client_id = Configuration.ClientId, email = email.Trim(), connection = Configuration.DatabaseConnection }, token);
        if (!response.IsSuccessStatusCode) throw new AccountSessionException("The reset email could not be sent. Check the address and try again.", "reset_failed");
    }

    private async Task<bool> HasOtpAuthenticatorAsync(string mfaToken, CancellationToken token)
    {
        using var client = Http();
        using var request = new HttpRequestMessage(HttpMethod.Get, Configuration.Authority.TrimEnd('/') + "/mfa/authenticators");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mfaToken);
        using var response = await client.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) return false;
        var json = await ReadJsonAsync(response, token);
        return json.ValueKind == JsonValueKind.Array && json.EnumerateArray().Any(a =>
            a.TryGetProperty("authenticator_type", out var type) && type.GetString() == "otp" && (!a.TryGetProperty("active", out var active) || active.GetBoolean()));
    }

    private async Task EstablishTokensAsync(JsonElement json, bool stepUp, CancellationToken token)
    {
        var access = json.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        var refresh = json.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        if (string.IsNullOrWhiteSpace(access)) throw new AccountSessionException("Sign-in could not be verified. Try again.", "token_missing");
        await EstablishAsync(access, refresh, token, preserveWorkspace: stepUp);
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostFormAsync(string path, System.Collections.Generic.Dictionary<string, string> form, CancellationToken token)
    {
        using var client = Http();
        using var response = await client.PostAsync(Configuration.Authority.TrimEnd('/') + path, new FormUrlEncodedContent(form), token);
        return (response.StatusCode, await ReadJsonAsync(response, token));
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return document.RootElement.Clone();
        }
        catch (JsonException) { return default; }
    }

    private static AccountSessionException ProviderError(HttpStatusCode status, string error, JsonElement json)
    {
        var description = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error_description", out var d) ? d.GetString() ?? "" : "";
        return error switch
        {
            "invalid_grant" when description.Contains("otp", StringComparison.OrdinalIgnoreCase) || description.Contains("code", StringComparison.OrdinalIgnoreCase)
                => new("That code was not accepted. Check the time on your device and try the newest code.", "invalid_code"),
            "invalid_grant" => new("Wrong email or password.", "invalid_credentials"),
            "too_many_attempts" or "too_many_requests" => new("Too many attempts. Wait a few minutes, or reset your password.", "too_many_attempts"),
            "unauthorized_client" or "unsupported_grant_type" or "access_denied" when description.Contains("Grant type", StringComparison.OrdinalIgnoreCase) || description.Contains("not allowed", StringComparison.OrdinalIgnoreCase)
                => new("In-app sign-in is not enabled for this installation yet. Use browser sign-in.", "password_grant_disabled"),
            "expired_token" or "invalid_token" => new("The sign-in attempt expired. Start again.", "mfa_expired"),
            "unauthorized" when description.Contains("verify", StringComparison.OrdinalIgnoreCase)
                => new("Verify your email from the message Alpha 6 sent you, then sign in again.", "email_unverified"),
            _ when (int)status >= 500 || status == 0 => new("The sign-in service is unavailable. Try again shortly.", "provider_unavailable"),
            _ => new(description is { Length: > 0 and < 160 } ? description : "Sign-in was declined. Try again or use your browser.", error)
        };
    }

    // Runs an account operation; when the service asks for a fresh multi-factor proof, confirms with the
    // caller, performs the step-up, and retries exactly once.
    internal Task<T> WithStepUpAsync<T>(Func<Task<T>> action, Func<Task<bool>> confirm, CancellationToken token) =>
        WithStepUpAsync(action, async () => await confirm() ? StepUpChoice.Browser : StepUpChoice.Cancelled, token);

    // The chooser may complete the step-up itself (in-app password + code), hand it to the browser, or cancel.
    internal async Task<T> WithStepUpAsync<T>(Func<Task<T>> action, Func<Task<StepUpChoice>> choose, CancellationToken token)
    {
        try { return await action(); }
        catch (AccountSessionException error) when (error.Code == "mfa_required")
        {
            switch (await choose())
            {
                case StepUpChoice.Cancelled: throw new OperationCanceledException();
                case StepUpChoice.Browser: await StepUpAsync(token); break;
            }
            return await action();
        }
    }

    internal Task<UserProfile> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken token) => Under(async () =>
    {
        var profile = await SendAsync<UserProfile>(HttpMethod.Put, "api/v1/me/profile", request, token)
            ?? throw new InvalidDataException("The account service returned an empty profile.");
        if (saved is not null) Save(saved with { Bootstrap = saved.Bootstrap with { Profile = profile } });
        return profile;
    }, token);
    internal Task<PersonalEntitlement> SetPersonalPlanAsync(PersonalPlan plan, CancellationToken token) => Under(async () =>
    {
        var entitlement = await SendAsync<PersonalEntitlement>(HttpMethod.Put, "api/v1/me/plan", new SetPersonalPlanRequest(plan), token)
            ?? throw new InvalidDataException("The account service returned an empty plan.");
        await EstablishAsync(accessToken!, null, token);
        return entitlement;
    }, token);
    internal Task<AirlineWorkspace> CreateAirlineAsync(CreateAirlineRequest request, CancellationToken token) => Under(async () =>
    {
        var airline = await SendAsync<AirlineWorkspace>(HttpMethod.Post, "api/v1/virtual-airlines", request, token)
            ?? throw new InvalidDataException("The account service returned an empty airline.");
        await EstablishAsync(accessToken!, null, token);
        return airline;
    }, token);
    internal Task<AirlineWorkspace> AcceptInvitationAsync(string code, CancellationToken token) => Under(async () =>
    {
        var airline = await SendAsync<AirlineWorkspace>(HttpMethod.Post, "api/v1/invitations/accept", new { token = code.Trim() }, token)
            ?? throw new InvalidDataException("The account service returned an empty airline.");
        await EstablishAsync(accessToken!, null, token);
        return airline;
    }, token);
    internal Task<AirlineWorkspace> SetAirlinePlanAsync(Guid airlineId, AirlinePlan plan, CancellationToken token) => Under(async () =>
    {
        var airline = await SendAsync<AirlineWorkspace>(HttpMethod.Put, $"api/v1/virtual-airlines/{airlineId}/plan", new SetAirlinePlanRequest(plan), token)
            ?? throw new InvalidDataException("The account service returned an empty airline.");
        await EstablishAsync(accessToken!, null, token);
        return airline;
    }, token);
    internal Task<IssuedInvitation> InviteAsync(Guid airlineId, InviteMemberRequest request, CancellationToken token) => Under(async () =>
        await SendAsync<IssuedInvitation>(HttpMethod.Post, $"api/v1/virtual-airlines/{airlineId}/invitations", request, token)
            ?? throw new InvalidDataException("The account service returned an empty invitation."), token);
    internal Task<InvitationResponse[]> ListInvitationsAsync(Guid airlineId, CancellationToken token) => Under(async () =>
        await SendAsync<InvitationResponse[]>(HttpMethod.Get, $"api/v1/virtual-airlines/{airlineId}/invitations", null, token) ?? [], token);
    internal Task RevokeInvitationAsync(Guid airlineId, Guid invitationId, CancellationToken token) => Under(async () =>
        await SendAsync<object>(HttpMethod.Delete, $"api/v1/virtual-airlines/{airlineId}/invitations/{invitationId}", null, token), token);
    internal Task<MemberResponse[]> ListMembersAsync(Guid airlineId, CancellationToken token) => Under(async () =>
        await SendAsync<MemberResponse[]>(HttpMethod.Get, $"api/v1/virtual-airlines/{airlineId}/members", null, token) ?? [], token);
    internal Task ChangeRolesAsync(Guid airlineId, Guid membershipId, AirlineRole[] roles, CancellationToken token) => Under(async () =>
        await SendAsync<object>(HttpMethod.Put, $"api/v1/virtual-airlines/{airlineId}/members/{membershipId}/roles", new ChangeRolesRequest(roles), token), token);
    internal Task TransferOwnershipAsync(Guid airlineId, Guid newOwnerUserId, CancellationToken token) => Under(async () =>
    {
        await SendAsync<object>(HttpMethod.Post, $"api/v1/virtual-airlines/{airlineId}/ownership-transfer", new TransferOwnershipRequest(newOwnerUserId), token);
        await EstablishAsync(accessToken!, null, token);
        return true;
    }, token);

    internal Task<UserProfile> SetAvatarAsync(byte[] image, string contentType, CancellationToken token) => Under(async () =>
    {
        var profile = await SendBytesAsync<UserProfile>(HttpMethod.Put, "api/v1/me/avatar", image, contentType, token)
            ?? throw new InvalidDataException("The account service returned an empty profile.");
        if (saved is not null) Save(saved with { Bootstrap = saved.Bootstrap with { Profile = profile } });
        return profile;
    }, token);
    internal Task RemoveAvatarAsync(CancellationToken token) => Under(async () =>
    {
        await SendAsync<object>(HttpMethod.Delete, "api/v1/me/avatar", null, token);
        await EstablishAsync(accessToken!, null, token);
        return true;
    }, token);
    internal Task<AirlineWorkspace> SetAirlineLogoAsync(Guid airlineId, byte[] image, string contentType, CancellationToken token) => Under(async () =>
    {
        var airline = await SendBytesAsync<AirlineWorkspace>(HttpMethod.Put, $"api/v1/virtual-airlines/{airlineId}/logo", image, contentType, token)
            ?? throw new InvalidDataException("The account service returned an empty airline.");
        await EstablishAsync(accessToken!, null, token);
        return airline;
    }, token);
    internal Task<LogbookSummary> LogbookSummaryAsync(CancellationToken token) => Under(async () =>
        await SendAsync<LogbookSummary>(HttpMethod.Get, "api/v1/me/flights/summary", null, token) ?? new LogbookSummary(0, 0, 0, [], null, null), token);
    internal Task<LogbookPage> ListFlightsAsync(int page, int pageSize, CancellationToken token) => Under(async () =>
        await SendAsync<LogbookPage>(HttpMethod.Get, $"api/v1/me/flights?page={page}&pageSize={pageSize}", null, token) ?? new LogbookPage([], 0, page, pageSize), token);
    internal Task<LogbookImportResult> ImportLogbookAsync(string csv, string? fileName, CancellationToken token) => Under(async () =>
        await SendAsync<LogbookImportResult>(HttpMethod.Post, "api/v1/me/flights/import", new LogbookImportRequest(csv, fileName), token)
            ?? throw new InvalidDataException("The account service returned an empty import result."), token);
    internal Task UndoLogbookImportAsync(Guid batchId, CancellationToken token) => Under(async () =>
        await SendAsync<object>(HttpMethod.Delete, $"api/v1/me/flights/imports/{batchId}", null, token), token);
    // Media served by the website (avatars, logos) is public by id; fetch without credentials, null when absent.
    internal async Task<byte[]?> FetchMediaAsync(string relativeUrl, CancellationToken token)
    {
        if (string.IsNullOrEmpty(relativeUrl)) return null;
        try
        {
            using var client = Http();
            using var response = await client.GetAsync(new Uri(new Uri(Configuration.ApiBaseUrl.TrimEnd('/') + "/"), relativeUrl.TrimStart('/')), token);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(token) : null;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { return null; }
    }

    private async Task<T?> SendBytesAsync<T>(HttpMethod method, string path, byte[] bytes, string contentType, CancellationToken token)
    {
        if (IsOffline || string.IsNullOrEmpty(accessToken)) throw new AccountSessionException("Connect to the internet to use account services.", "offline");
        using var client = Api(accessToken);
        using var request = new HttpRequestMessage(method, path) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var response = await client.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.Unauthorized) { Clear(); throw new AccountSessionException("Your session has expired. Sign in again.", "session_expired"); }
        if ((int)response.StatusCode >= 500) throw new HttpRequestException("The account service is unavailable.", null, response.StatusCode);
        if (!response.IsSuccessStatusCode)
        {
            ProblemBody? problem = null;
            try { problem = await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken: token); } catch (Exception error) when (error is JsonException or IOException) { }
            throw new AccountSessionException(problem?.Title is { Length: > 0 } title ? title : "The account service declined that image.", problem?.Code ?? "");
        }
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: token);
    }

    private async Task<T> Under<T>(Func<Task<T>> action, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { return await action(); }
        finally { gate.Release(); }
    }

    // Authenticated call while holding gate. One silent credential refresh on 401; explicit provider or
    // service rejections become AccountSessionException with the server's stable code and never clear the
    // session, so a multi-factor prompt can follow without signing the pilot out.
    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken token, bool retried = false)
    {
        if (IsOffline || string.IsNullOrEmpty(accessToken))
            throw new AccountSessionException("Connect to the internet to use account services.", "offline");
        using var client = Api(accessToken);
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (!retried && !string.IsNullOrEmpty(saved?.RefreshToken) && await RestoreCoreAsync(token) && !IsOffline)
                return await SendAsync<T>(method, path, body, token, retried: true);
            Clear();
            throw new AccountSessionException("Your session has expired. Sign in again.", "session_expired");
        }
        if ((int)response.StatusCode >= 500) throw new HttpRequestException("The account service is unavailable.", null, response.StatusCode);
        if (!response.IsSuccessStatusCode)
        {
            ProblemBody? problem = null;
            try { problem = await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken: token); } catch (Exception error) when (error is JsonException or IOException) { }
            throw new AccountSessionException(problem?.Title is { Length: > 0 } title ? title : "The account service declined that request.", problem?.Code ?? "");
        }
        if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object)) return default;
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: token);
    }

    // Called only while holding gate, including before committing a workspace change.
    private async Task<bool> RestoreCoreAsync(CancellationToken token)
    {
        saved ??= Read();
        if (saved is null) return false;
        try
        {
            if (string.IsNullOrEmpty(saved.RefreshToken))
            {
                if (string.IsNullOrEmpty(accessToken)) return TryOffline();
                await EstablishAsync(accessToken, null, token);
                return true;
            }
            using var http = Http();
            // Use the maintained protocol client. All refresh exchanges are serialized by gate.
            var result = await http.RequestRefreshTokenAsync(new RefreshTokenRequest
            {
                Address = Configuration.Authority.TrimEnd('/') + "/oauth/token", ClientId = Configuration.ClientId,
                ClientCredentialStyle = ClientCredentialStyle.PostBody, RefreshToken = saved.RefreshToken
            }, token);
            if (result.IsError)
            {
                if (result.HttpStatusCode == 0 || (int)result.HttpStatusCode >= 500) return TryOffline();
                Clear(); return false; // invalid_grant and every explicit provider rejection fail closed.
            }
            if (string.IsNullOrWhiteSpace(result.AccessToken)) { Clear(); return false; }
            // Persist the new refresh token before bootstrap; an API outage must not lose rotation.
            Save(saved with { RefreshToken = result.RefreshToken ?? saved.RefreshToken });
            await EstablishAsync(result.AccessToken, result.RefreshToken, token);
            return true;
        }
        catch (HttpRequestException error) when (error.StatusCode is null || (int)error.StatusCode >= 500)
        { return TryOffline(); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return TryOffline(); }
    }

    private async Task EstablishAsync(string access, string? refresh, CancellationToken token, bool preserveWorkspace = true)
    {
        using var client = Api(access);
        using var response = await client.GetAsync("api/v1/me/bootstrap", token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { Clear(); throw new AccountSessionException("This account is not authorized. Sign in again or contact support."); }
        response.EnsureSuccessStatusCode();
        var bootstrap = await response.Content.ReadFromJsonAsync<BootstrapResponse>(cancellationToken: token)
            ?? throw new InvalidDataException("The account service returned an empty response.");
        if (bootstrap.Account.Status != AccountStatus.Active || bootstrap.Account.Id == Guid.Empty) { Clear(); throw new AccountSessionException("This account is not active. Contact support for help."); }
        if (preserveWorkspace && saved is not null && saved.Bootstrap.Account.Id != bootstrap.Account.Id)
        { Clear(); throw new AccountSessionException("The account identity changed. Sign in again."); }
        token.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        accessToken = access; IsOffline = false;
        var selected = preserveWorkspace && saved is not null ? saved.Workspace : bootstrap.LastWorkspace;
        Save(new(configurationKey, refresh ?? saved?.RefreshToken, bootstrap, now, now, OfflineSessionPolicy.ValidateWorkspace(bootstrap, selected)));
    }

    private bool TryOffline()
    {
        var now = DateTimeOffset.UtcNow;
        if (saved is null || !OfflineSessionPolicy.MayOpen(saved.Bootstrap, saved.ReceivedAt, saved.LastSeenAt, now)) { Clear(); return false; }
        accessToken = null; IsOffline = true;
        // Retain the flight's workspace for local capture. Cloud operations still require fresh authorization.
        Save(saved with { LastSeenAt = now });
        return true;
    }

    internal async Task SelectWorkspaceAsync(WorkspaceSelection selection, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (saved is null) throw new AccountSessionException("Sign in to choose a workspace.");
            if (!IsOffline && !string.IsNullOrEmpty(saved.RefreshToken))
            {
                if (!await RestoreCoreAsync(token)) throw new AccountSessionException("Your session has expired. Sign in again.");
                if (IsOffline) throw new AccountSessionException("The account service is unavailable. You can now open your current workspace offline or try reconnecting.");
            }
            if (!MayStartNewFlight(DateTimeOffset.UtcNow))
            { Clear(); throw new AccountSessionException("Your saved session has expired. Sign in again to open a workspace."); }
            if (IsOffline && selection.AirlineId is not null && selection != saved.Workspace)
                throw new AccountSessionException("Connect to the internet to choose a different virtual airline.");
            if (OfflineSessionPolicy.ValidateWorkspace(saved.Bootstrap, selection) != selection) throw new AccountSessionException("This airline membership is unavailable. Choose another workspace.");
            if (!IsOffline)
            {
                using var client = Api(accessToken!);
                using var response = await client.PutAsJsonAsync("api/v1/me/workspace", selection, token);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { Clear(); throw new AccountSessionException("Your access changed. Sign in again."); }
                response.EnsureSuccessStatusCode();
            }
            Save(saved with { Workspace = selection });
        }
        finally { gate.Release(); }
    }

    internal async Task SignOutAsync()
    {
        await gate.WaitAsync();
        try
        {
            var refresh = saved?.RefreshToken;
            Clear();
            if (refresh is null) return;
            // Auth0's public-client revocation endpoint; always clear locally even if offline.
            using var client = Http();
            try
            {
                using var response = await client.PostAsJsonAsync(Configuration.Authority.TrimEnd('/') + "/oauth/revoke",
                    new { client_id = Configuration.ClientId, token = refresh, token_type_hint = "refresh_token" });
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { }
        }
        finally { gate.Release(); }
    }

    private HttpClient Api(string access)
    {
        var client = Http();
        client.BaseAddress = new Uri(Configuration.ApiBaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Alpha6OPS/" + ClientVersion);
        return client;
    }
    private HttpClient Http() => new(handlerFactory?.Invoke() ?? new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(20) };
    // "Remember me" for the in-app form. Protected with Windows DPAPI for the current Windows user, like the
    // session file: convenient on a personal PC, not a defence against software already running as that user.
    internal RememberedLogin RememberedLogin
    {
        get
        {
            try
            {
                var path = Path.Combine(rootDirectory, "Identity", "remembered.bin");
                if (!File.Exists(path)) return RememberedLogin.None;
                var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                try { return JsonSerializer.Deserialize<RememberedLogin>(bytes) ?? RememberedLogin.None; }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            catch (Exception error) when (error is CryptographicException or IOException or JsonException or UnauthorizedAccessException) { return RememberedLogin.None; }
        }
    }
    internal void Remember(string email, string? password)
    {
        var path = Path.Combine(rootDirectory, "Identity", "remembered.bin");
        if (email.Length == 0 && password is null) { try { File.Delete(path); } catch (IOException) { } return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new RememberedLogin(email, password));
        try { File.WriteAllBytes(path, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private SavedAccountSession? Read()
    {
        try
        {
            if (!File.Exists(sessionPath)) return null;
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(sessionPath), null, DataProtectionScope.CurrentUser);
            try
            {
                var session = JsonSerializer.Deserialize<SavedAccountSession>(bytes);
                return session?.ConfigurationKey == configurationKey ? session : null;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception error) when (error is CryptographicException or IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    private void Save(SavedAccountSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(sessionPath)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session);
        try
        {
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            var temporary = sessionPath + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(protectedBytes); stream.Flush(true); }
            File.Move(temporary, sessionPath, true);
            saved = session;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private void Clear()
    {
        saved = null; accessToken = null; IsOffline = false;
        try { File.Delete(sessionPath); File.Delete(sessionPath + ".tmp"); }
        catch (DirectoryNotFoundException) { } // First sign-in has no Identity directory yet.
    }
}
