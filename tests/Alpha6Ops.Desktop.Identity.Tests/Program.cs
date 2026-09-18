using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alpha6Ops.Desktop;
using Alpha6Ops.Identity;
using Duende.IdentityModel.OidcClient;

var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name); count++;
}
var now = DateTimeOffset.UtcNow;
var airline = new AirlineWorkspace(Guid.NewGuid(), "test-air", "Test Airline", "TEST", AirlinePlan.Community,
    "active", MembershipStatus.Active, [AirlineRole.Pilot], false, [Capabilities.AirlineRead]);
var bootstrap = new BootstrapResponse(new(Guid.NewGuid(), "Test Pilot", "pilot@example.invalid", true, AccountStatus.Active),
    new(PersonalPlan.Free, "active", null), Capabilities.Personal, [airline], new(airline.Id), now, now.AddDays(30));
Check(OfflineSessionPolicy.MayOpen(bootstrap, now, now, now.AddDays(29)), "trusted offline window allows local recording before expiry");
Check(!OfflineSessionPolicy.MayOpen(bootstrap, now, now, now.AddDays(30)), "offline lease expires at thirty days exactly");
Check(!OfflineSessionPolicy.MayOpen(bootstrap, now, now.AddDays(2), now.AddDays(1)), "clock rollback cannot extend offline access");
Check(!OfflineSessionPolicy.MayOpen(bootstrap with { OfflineExpiresAt = now.AddDays(1) }, now, now, now.AddDays(2)), "server expiry is honored when shorter than local maximum");
Check(!OfflineSessionPolicy.MayOpen(bootstrap with { Account = bootstrap.Account with { Status = AccountStatus.Suspended } }, now, now, now), "suspended account cannot open cached data");
Check(OfflineSessionPolicy.ValidateWorkspace(bootstrap, new(Guid.NewGuid())) == WorkspaceSelection.Personal, "unknown airline falls back to personal");
Check(OfflineSessionPolicy.ValidateWorkspace(bootstrap with { Airlines = [airline with { MembershipStatus = MembershipStatus.Suspended }] }, new(airline.Id)) == WorkspaceSelection.Personal, "suspended airline cannot remain selected");

var directory = Path.GetFullPath(Path.Combine("work", "identity-client-tests", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(directory);
var config = new IdentityConfiguration("https://identity.example.invalid", "test-native", "https://api.example.invalid", "https://api.example.invalid/", "https://portal.example.invalid/");
var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config))));
var sessionPath = Path.Combine(directory, "Identity", "session.bin");
SavedAccountSession Seed(string refresh = "old-refresh")
{
    var saved = new SavedAccountSession(key, refresh, bootstrap, now, now, new(airline.Id));
    Directory.CreateDirectory(Path.GetDirectoryName(sessionPath)!);
    File.WriteAllBytes(sessionPath, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(saved), null, DataProtectionScope.CurrentUser));
    return saved;
}
SavedAccountSession Read() => JsonSerializer.Deserialize<SavedAccountSession>(ProtectedData.Unprotect(File.ReadAllBytes(sessionPath), null, DataProtectionScope.CurrentUser))!;
HttpResponseMessage Json(object value, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = JsonContent.Create(value) };
var mode = "offline";
var tokensSeen = new List<string>();
var workspaceWrites = 0;
var airlineCreates = 0;
var unauthorizedServed = false;
var lastUserAgent = "";
var passwordLogins = 0; var associations = 0; var resets = 0; var otpEnrolled = false; var signups = new List<string>();
HttpResponseMessage Problem(HttpStatusCode code, string problemCode, string title) => Json(new { type = "about:blank", title, status = (int)code, code = problemCode }, code);
async Task<HttpResponseMessage> Handle(HttpRequestMessage request)
{
    if (mode == "offline") throw new HttpRequestException("Simulated network outage");
    lastUserAgent = request.Headers.UserAgent.ToString();
    if (request.RequestUri!.AbsolutePath.StartsWith("/api/") && mode == "unauthorized-once" && !unauthorizedServed)
    { unauthorizedServed = true; return new(HttpStatusCode.Unauthorized); }
    if (request.RequestUri.AbsolutePath == "/api/v1/me/profile" && request.Method == HttpMethod.Put)
    {
        var body = (await request.Content!.ReadFromJsonAsync<UpdateProfileRequest>())!;
        if (body.SimBriefUsername.Length == 1) return Problem(HttpStatusCode.BadRequest, "invalid_profile", "SimBrief username must be 2 to 80 characters without spaces.");
        return Json(new UserProfile(body.SimBriefUsername, body.Callsign.ToUpperInvariant(), body.HomeBaseIcao.ToUpperInvariant(), body.WeightUnit, body.AltitudeUnit,
            body.LandingDistanceUnit, body.PreferredWorkspace, body.TimeZone, body.AvatarInitials.ToUpperInvariant(), now, "0.16.0", now));
    }
    if (request.RequestUri.AbsolutePath == "/api/v1/virtual-airlines" && request.Method == HttpMethod.Post)
    {
        airlineCreates++;
        if (mode == "mfa-required") return Problem(HttpStatusCode.Forbidden, "mfa_required", "Confirm your identity with a passkey or MFA to continue.");
        return Json(airline with { Id = Guid.NewGuid(), Name = "Created Airline", Roles = [AirlineRole.Pilot, AirlineRole.Owner], IsFounder = true });
    }
    if (request.RequestUri.AbsolutePath == "/api/v1/invitations/accept") return Json(airline);
    if (request.RequestUri.AbsolutePath.EndsWith("/invitations") && request.Method == HttpMethod.Post)
        return Json(new IssuedInvitation(new InvitationResponse(Guid.NewGuid(), "NEW@EXAMPLE.INVALID", [AirlineRole.Pilot], now.AddDays(7), "pending"), "raw-invite-code"));
    if (request.RequestUri.AbsolutePath.EndsWith("/members")) return Json(new[] { new MemberResponse(Guid.NewGuid(), bootstrap.Account.Id, "Test Pilot", "pilot@example.invalid", MembershipStatus.Active, [AirlineRole.Pilot, AirlineRole.Owner]) });
    if (request.RequestUri!.AbsolutePath == "/oauth/token")
    {
        var form = await request.Content!.ReadAsStringAsync();
        tokensSeen.Add(form);
        if (form.Contains("grant-type%2Fpassword-realm") || form.Contains("grant-type/password-realm"))
        {
            // Fake provider: one known account; MFA is demanded when the pilot has an authenticator or asks for step-up.
            passwordLogins++;
            if (!form.Contains("password=correct-horse")) return Json(new { error = "invalid_grant", error_description = "Wrong email or password." }, HttpStatusCode.Forbidden);
            if (mode == "password-grant-disabled") return Json(new { error = "unauthorized_client", error_description = "Grant type 'http://auth0.com/oauth/grant-type/password-realm' not allowed for the client." }, HttpStatusCode.Forbidden);
            if (otpEnrolled || form.Contains("acr_values=")) return Json(new { error = "mfa_required", error_description = "Multifactor authentication required", mfa_token = "mfa-token-1" }, HttpStatusCode.Forbidden);
            return Json(new { access_token = "test-access", refresh_token = "new-refresh", token_type = "Bearer", expires_in = 300 });
        }
        if (form.Contains("mfa-otp"))
        {
            if (!form.Contains("mfa_token=mfa-token-1")) return Json(new { error = "expired_token", error_description = "mfa_token is expired" }, HttpStatusCode.Unauthorized);
            if (!form.Contains("otp=123456")) return Json(new { error = "invalid_grant", error_description = "Invalid otp_code." }, HttpStatusCode.Forbidden);
            otpEnrolled = true;
            return Json(new { access_token = "test-access", refresh_token = "new-refresh", token_type = "Bearer", expires_in = 300 });
        }
        if (form.Contains("mfa-recovery-code"))
        {
            if (!form.Contains("recovery_code=RECOVER1234")) return Json(new { error = "invalid_grant", error_description = "Invalid recovery code" }, HttpStatusCode.Forbidden);
            return Json(new { access_token = "test-access", refresh_token = "new-refresh", token_type = "Bearer", expires_in = 300, recovery_code = "REPLACEMENT99" });
        }
        if (mode == "revoked") return Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
        return Json(new { access_token = "test-access", refresh_token = "new-refresh", token_type = "Bearer", expires_in = 300 });
    }
    if (request.RequestUri.AbsolutePath == "/mfa/authenticators")
    {
        if (request.Headers.Authorization?.Parameter != "mfa-token-1") return new(HttpStatusCode.Unauthorized);
        return Json(otpEnrolled ? new object[] { new { id = "totp|dev_1", authenticator_type = "otp", active = true } } : Array.Empty<object>());
    }
    if (request.RequestUri.AbsolutePath == "/mfa/associate")
    {
        if (request.Headers.Authorization?.Parameter != "mfa-token-1") return new(HttpStatusCode.Unauthorized);
        associations++;
        return Json(new { authenticator_type = "otp", secret = "JBSWY3DPEHPK3PXP", barcode_uri = "otpauth://totp/Alpha6:pilot?secret=JBSWY3DPEHPK3PXP", recovery_codes = new[] { "RECOVER1234" } });
    }
    if (request.RequestUri.AbsolutePath == "/dbconnections/signup")
    {
        var body = await request.Content!.ReadAsStringAsync();
        signups.Add(body);
        if (body.Contains("taken@example.invalid")) return Json(new { code = "invalid_signup", description = "Invalid sign up" }, HttpStatusCode.BadRequest);
        if (body.Contains("\"password\":\"weak\"")) return Json(new { name = "PasswordStrengthError", message = "Password is too weak" }, HttpStatusCode.BadRequest);
        return Json(new { _id = "new", email = "new@example.invalid", email_verified = false });
    }
    if (request.RequestUri.AbsolutePath == "/dbconnections/change_password") { resets++; return new(HttpStatusCode.OK) { Content = new StringContent("We've just sent you an email to reset your password.") }; }
    if (request.RequestUri.AbsolutePath == "/api/v1/me/bootstrap")
    {
        Check(request.Headers.Authorization?.Parameter == "test-access", "bootstrap sends bearer credential only to configured API");
        if (mode == "bootstrap-down") return Json(new { error = "temporarily_unavailable" }, HttpStatusCode.ServiceUnavailable);
        if (mode == "forbidden") return Json(new { code = "account_suspended" }, HttpStatusCode.Forbidden);
        if (mode == "other-account") return Json(bootstrap with { Account = bootstrap.Account with { Id = Guid.NewGuid() } });
        if (mode == "membership-removed") return Json(bootstrap with { Airlines = [] });
        return Json(bootstrap);
    }
    if (request.RequestUri.AbsolutePath == "/api/v1/me/workspace")
    {
        workspaceWrites++;
        if (mode == "workspace-denied") return Json(new { code = "forbidden" }, HttpStatusCode.Forbidden);
    }
    return new(HttpStatusCode.NoContent);
}
DesktopAccountSession Session() => new(config, directory, () => new FakeHandler(Handle));
Seed();
var session = Session();
Check(await session.RestoreAsync(default) && session.IsOffline, "network failure restores trusted local mode");
Check(session.MayStartNewFlight(now.AddDays(29)) && !session.MayStartNewFlight(now.AddDays(30)), "open app cannot start a new flight after offline authorization expires");
Check(Read().RefreshToken == "old-refresh", "network failure preserves refresh credential for reconnection");
Check(session.Workspace.AirlineId == airline.Id, "offline restore retains the original flight workspace");
Check(session.DataDirectory.Contains(bootstrap.Account.Id.ToString("N")) && session.DataDirectory.EndsWith(airline.Id.ToString("N")), "local data is scoped to immutable account and airline IDs");
mode = "bootstrap-down";
session = Session();
Check(await session.RestoreAsync(default) && session.IsOffline, "API outage after token refresh preserves trusted local mode");
Check(Read().RefreshToken == "new-refresh", "new rotating credential is durable before API bootstrap");
mode = "online";
session = Session();
Check(await session.RestoreAsync(default) && !session.IsOffline, "reconnection revalidates account and memberships");
Check(tokensSeen.Last().Contains("refresh_token=new-refresh"), "reconnection uses the most recent rotating token");
var protectedBytes = File.ReadAllBytes(sessionPath);
Check(!Encoding.UTF8.GetString(protectedBytes).Contains("new-refresh"), "refresh credential is protected on disk");
mode = "revoked";
Check(!await session.RestoreAsync(default) && session.Bootstrap is null && !File.Exists(sessionPath), "known token revocation clears cache instead of offline fallback");
foreach (var failure in new[] { "forbidden", "other-account" })
{
    Seed(); mode = failure; session = Session();
    try { await session.RestoreAsync(default); throw new Exception("Expected authorization failure"); }
    catch (InvalidOperationException) { Check(session.Bootstrap is null && !File.Exists(sessionPath), failure + " cannot retain cached access"); }
}
Seed(); mode = "online"; session = Session(); await session.RestoreAsync(default);
await session.SignOutAsync();
Check(session.Bootstrap is null && !File.Exists(sessionPath), "logout removes protected credentials");
Directory.CreateDirectory(Path.GetDirectoryName(sessionPath)!);
File.WriteAllBytes(sessionPath, [1, 2, 3]);
Check(!await Session().RestoreAsync(default), "corrupt credential cache fails closed");

mode = "online";
var firstInstall = new DesktopAccountSession(config, Path.Combine(directory, "first-install"), () => new FakeHandler(Handle),
    (_, _) => Task.FromResult(new BrowserLoginResult("test-access", "first-refresh")));
await firstInstall.LoginAsync(default);
Check(firstInstall.Bootstrap is not null, "first sign-in succeeds before any identity cache directory exists");
var accessOnly = new DesktopAccountSession(config, Path.Combine(directory, "access-only"), () => new FakeHandler(Handle),
    (_, _) => Task.FromResult(new BrowserLoginResult("test-access", null)));
await accessOnly.LoginAsync(default);
var refreshesBeforeAccessOnly = tokensSeen.Count;
Check(await accessOnly.RestoreAsync(default) && !accessOnly.IsOffline && tokensSeen.Count == refreshesBeforeAccessOnly,
    "in-memory access token can refresh memberships when provider did not issue a refresh token");
LoginRequest? loginRequest = null;
var loginMode = "success";
using var cancelLogin = new CancellationTokenSource();
session = new(config, directory, () => new FakeHandler(Handle), (request, token) =>
{
    loginRequest = request;
    if (loginMode == "cancel") cancelLogin.Cancel();
    return Task.FromResult(loginMode == "error"
        ? new BrowserLoginResult("", null, true)
        : new BrowserLoginResult("test-access", "signup-refresh"));
});
await session.LoginAsync(default, "new-pilot@example.invalid", createAccount: true);
Check(loginRequest!.FrontChannelExtraParameters.Any(p => p.Key == "screen_hint" && p.Value == "signup")
    && loginRequest.FrontChannelExtraParameters.Any(p => p.Key == "login_hint" && p.Value == "new-pilot@example.invalid")
    && loginRequest.FrontChannelExtraParameters.Any(p => p.Key == "audience" && p.Value == config.Audience),
    "desktop registration requests hosted signup with email hint and API audience");
Check(session.Bootstrap?.Account.Id == bootstrap.Account.Id && Read().RefreshToken == "signup-refresh",
    "registration bootstraps and protects a usable desktop session");
await session.LoginAsync(default);
Check(!loginRequest!.FrontChannelExtraParameters.Any(p => p.Key is "screen_hint" or "login_hint"),
    "other sign-in options do not inherit a previous signup or email hint");
loginMode = "error";
try { await session.LoginAsync(default); throw new Exception("Expected failed sign-in"); }
catch (AccountSessionException error)
{
    Check(error.Message == "Sign-in was canceled or could not be verified. Try again in your browser." && session.Bootstrap is null && !File.Exists(sessionPath),
        "failed login clears old session and exposes only safe application text");
}
loginMode = "cancel";
try { await session.LoginAsync(cancelLogin.Token); throw new Exception("Expected canceled login"); }
catch (OperationCanceledException)
{
    Check(session.Bootstrap is null && !File.Exists(sessionPath), "cancellation cannot establish a session from a late login result");
}
loginMode = "success";
await session.LoginAsync(default);
Check(session.Bootstrap is not null, "sign-in can be retried after cancellation");
Check(lastUserAgent.StartsWith("Alpha6OPS/"), "account requests identify the desktop version");
Check(session.Profile == UserProfile.Default, "missing server profile falls back to defaults without failing");
var updatedProfile = await session.UpdateProfileAsync(new("reece74", "a6-001", "kjfk", "KG", "FT", "M", "personal", "UTC", "rc"), default);
Check(updatedProfile.Callsign == "A6-001" && session.Profile.SimBriefUsername == "reece74" && Read().Bootstrap.Profile?.HomeBaseIcao == "KJFK",
    "profile updates are cached in the protected session");
try { await session.UpdateProfileAsync(new("x", "", "", "LBS", "FT", "FT", "last_used", "", ""), default); throw new Exception("Expected invalid profile"); }
catch (AccountSessionException error) { Check(error.Code == "invalid_profile" && error.Message.Contains("SimBrief") && session.Bootstrap is not null, "service rejections surface the stable code and keep the session"); }
var workspaceBefore = session.Workspace;
mode = "mfa-required"; loginRequest = null;
var confirmations = 0;
var created = await session.WithStepUpAsync(() => session.CreateAirlineAsync(new("Created Airline", "created-airline", "CRT"), default),
    () => { confirmations++; mode = "online"; return Task.FromResult(true); }, default);
Check(created.Name == "Created Airline" && airlineCreates == 2 && confirmations == 1, "mfa_required prompts once, steps up, and retries exactly once");
Check(loginRequest!.FrontChannelExtraParameters.Any(p => p.Key == "acr_values" && p.Value == DesktopAccountSession.MultiFactorPolicy)
    && loginRequest.FrontChannelExtraParameters.Any(p => p.Key == "max_age" && p.Value == "0")
    && loginRequest.FrontChannelExtraParameters.Any(p => p.Key == "prompt" && p.Value == "login")
    && loginRequest.FrontChannelExtraParameters.Any(p => p.Key == "login_hint" && p.Value == bootstrap.Account.Email),
    "step-up requests a fresh multi-factor proof for the signed-in account");
Check(session.Workspace == workspaceBefore && session.Bootstrap is not null, "step-up preserves the workspace and session");
mode = "mfa-required"; airlineCreates = 0;
try { await session.WithStepUpAsync(() => session.CreateAirlineAsync(new("Blocked", "blocked", "BLK"), default), () => Task.FromResult(false), default); throw new Exception("Expected cancellation"); }
catch (OperationCanceledException) { Check(airlineCreates == 1 && session.Bootstrap is not null, "declining the identity check cancels without a retry or sign-out"); }
loginMode = "error";
try { await session.StepUpAsync(default); throw new Exception("Expected failed step-up"); }
catch (AccountSessionException error) { Check(error.Code == "step_up_failed" && session.Bootstrap is not null && File.Exists(sessionPath), "failed step-up keeps the existing session"); }
loginMode = "success"; mode = "unauthorized-once"; unauthorizedServed = false;
var refreshesBefore401 = tokensSeen.Count;
var members = await session.ListMembersAsync(airline.Id, default);
Check(members.Length == 1 && tokensSeen.Count == refreshesBefore401 + 1, "a rejected access token is refreshed once and the call retried");
mode = "online";
var refreshes = tokensSeen.Count;
await session.SelectWorkspaceAsync(WorkspaceSelection.Personal, default);
Check(tokensSeen.Count == refreshes + 1 && workspaceWrites == 1 && Read().Workspace == WorkspaceSelection.Personal,
    "opening a workspace refreshes credentials and durably saves the chosen workspace");
mode = "membership-removed";
try { await session.SelectWorkspaceAsync(new(airline.Id), default); throw new Exception("Expected removed membership"); }
catch (AccountSessionException)
{
    Check(workspaceWrites == 1 && session.Workspace == WorkspaceSelection.Personal && session.Bootstrap!.Airlines.Length == 0,
        "membership changes are rechecked before writing a workspace selection");
}
mode = "revoked";
try { await session.SelectWorkspaceAsync(WorkspaceSelection.Personal, default); throw new Exception("Expected revoked session"); }
catch (AccountSessionException)
{
    Check(session.Bootstrap is null && workspaceWrites == 1, "revoked session cannot open a workspace");
}
Seed(); mode = "online"; session = Session(); await session.RestoreAsync(default);
mode = "bootstrap-down";
try { await session.SelectWorkspaceAsync(WorkspaceSelection.Personal, default); throw new Exception("Expected offline transition"); }
catch (AccountSessionException)
{
    Check(session.IsOffline && session.Workspace.AirlineId == airline.Id && workspaceWrites == 1,
        "outage during selection preserves current workspace and offers explicit offline retry");
}
await session.SelectWorkspaceAsync(WorkspaceSelection.Personal, default);
Check(session.Workspace == WorkspaceSelection.Personal && workspaceWrites == 1, "explicit offline retry opens personal workspace without a cloud write");
Seed(); mode = "online"; session = Session(); await session.RestoreAsync(default);
mode = "workspace-denied";
try { await session.SelectWorkspaceAsync(WorkspaceSelection.Personal, default); throw new Exception("Expected access denial"); }
catch (AccountSessionException)
{
    Check(session.Bootstrap is null && !File.Exists(sessionPath), "workspace endpoint denial clears session even after successful refresh");
}
var expired = Seed() with { RefreshToken = null, ReceivedAt = now.AddDays(-31), LastSeenAt = now.AddDays(-31) };
File.WriteAllBytes(sessionPath, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(expired), null, DataProtectionScope.CurrentUser));
Check(!await Session().RestoreAsync(default), "expired offline session requires sign-in");
// In-app sign-in: password grant, MFA enrolment and challenge, recovery codes, step-up, sign-up and reset.
if (File.Exists(sessionPath)) File.Delete(sessionPath);
mode = "online"; session = Session();
try { await session.PasswordLoginAsync("pilot@example.invalid", "wrong", false, default); throw new Exception("Expected rejection"); }
catch (AccountSessionException error) { Check(error.Code == "invalid_credentials" && session.Bootstrap is null, "wrong password is reported without a session"); }
var plain = await session.PasswordLoginAsync("pilot@example.invalid", "correct-horse", false, default);
Check(plain.Success && session.Bootstrap is not null && !session.IsOffline && Read().RefreshToken == "new-refresh", "password sign-in establishes and protects the session");
Check(!tokensSeen.Last().Contains("acr_values") && tokensSeen.Last().Contains("realm=Username-Password-Authentication") && tokensSeen.Last().Contains("audience="), "password grant targets the database realm and the API audience without forcing MFA");
Check(!File.ReadAllText(sessionPath, Encoding.Latin1).Contains("correct-horse"), "the password is never written to disk");
var workspaceBeforeStepUp = session.Workspace;
var stepUp = await session.PasswordLoginAsync("pilot@example.invalid", "correct-horse", true, default);
Check(!stepUp.Success && stepUp.MfaToken == "mfa-token-1" && stepUp.NeedsEnrollment && session.Bootstrap is not null, "in-app step-up asks for a second factor and offers enrolment when none exists");
Check(tokensSeen.Last().Contains("acr_values="), "step-up sends the multi-factor policy in the token request body");
var enrolment = await session.BeginOtpEnrollmentAsync(stepUp.MfaToken!, default);
Check(enrolment.Secret == "JBSWY3DPEHPK3PXP" && enrolment.RecoveryCodes.SequenceEqual(new[] { "RECOVER1234" }) && associations == 1, "enrolment returns the secret and recovery codes once");
try { await session.CompleteMfaAsync(stepUp.MfaToken!, "000000", false, true, default); throw new Exception("Expected bad code"); }
catch (AccountSessionException error) { Check(error.Code == "invalid_code" && session.Bootstrap is not null, "a wrong code is reported and the session survives"); }
Check(await session.CompleteMfaAsync(stepUp.MfaToken!, "123 456", false, true, default) is null && session.Workspace == workspaceBeforeStepUp, "a good code completes step-up in the app and preserves the workspace");
var challenged = await session.PasswordLoginAsync("pilot@example.invalid", "correct-horse", false, default);
Check(!challenged.Success && !challenged.NeedsEnrollment, "an enrolled account is challenged, not re-enrolled, at the next sign-in");
Check(await session.CompleteMfaAsync(challenged.MfaToken!, "RECOVER-1234", true, false, default) == "REPLACEMENT99", "a recovery code signs in and yields its replacement");
try { await session.CompleteMfaAsync("stale", "123456", false, false, default); throw new Exception("Expected expiry"); }
catch (AccountSessionException error) { Check(error.Code == "mfa_expired", "an expired challenge asks the pilot to start again"); }
mode = "password-grant-disabled";
try { await session.PasswordLoginAsync("pilot@example.invalid", "correct-horse", false, default); throw new Exception("Expected disabled grant"); }
catch (AccountSessionException error) { Check(error.Code == "password_grant_disabled", "a tenant without the password grant points pilots to the browser"); }
mode = "online";
await session.SignUpAsync("new@example.invalid", "correct-horse", default);
Check(signups.Count == 1 && signups[0].Contains("Username-Password-Authentication") && !signups[0].Contains("access_token"), "sign-up posts to the database connection");
try { await session.SignUpAsync("taken@example.invalid", "correct-horse", default); throw new Exception("Expected duplicate"); }
catch (AccountSessionException error) { Check(error.Code == "user_exists", "an existing address is explained"); }
try { await session.SignUpAsync("new2@example.invalid", "weak", default); throw new Exception("Expected weak password"); }
catch (AccountSessionException error) { Check(error.Code == "invalid_password", "a weak password is explained"); }
await session.RequestPasswordResetAsync("pilot@example.invalid", default);
Check(resets == 1, "password reset request reaches the provider");
var again = await session.PasswordLoginAsync("pilot@example.invalid", "correct-horse", false, default);
await session.CompleteMfaAsync(again.MfaToken!, "123456", false, false, default);
Check(session.Bootstrap is not null && !session.IsOffline, "signing back in after a cleared attempt restores the session");
var choices = 0;
mode = "mfa-required"; airlineCreates = 0;
var viaApp = await session.WithStepUpAsync(() => session.CreateAirlineAsync(new("App Airline", "app-airline", "APP"), default),
    () => { choices++; mode = "online"; return Task.FromResult(StepUpChoice.Completed); }, default);
Check(viaApp.Name == "Created Airline" && airlineCreates == 2 && choices == 1, "an in-app security check retries without opening the browser");
Check(SystemLoginBrowser.CallbackPage(false).Contains("You’re signed in") && SystemLoginBrowser.CallbackPage(true).Contains("didn’t complete") && !SystemLoginBrowser.CallbackPage(false).Contains("<script"), "the browser callback page is branded and script-free");
await BrowserChecks.RunAsync(Check);
Console.WriteLine($"{count} desktop identity checks passed. Artifacts: {directory}");

sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
}
