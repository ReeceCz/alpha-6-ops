using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Alpha6Ops.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

var failures = 0;
void Check(bool condition, string scenario)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {scenario}");
    if (!condition) failures++;
}
using (var setup = new ServerFactory(null))
using (var client = setup.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false }))
{
    var landing = await client.GetAsync("/");
    Check(landing.StatusCode == HttpStatusCode.OK, "Development landing available without Auth0");
    Check((await landing.Content.ReadAsStringAsync()).Contains("not available on this installation"), "Unconfigured registration is explained");
    Check((await client.GetAsync("/api/v1/me/bootstrap")).StatusCode == HttpStatusCode.ServiceUnavailable, "Unconfigured API fails closed");
    Check((await client.GetAsync("/auth/login")).StatusCode == HttpStatusCode.ServiceUnavailable, "Unconfigured login fails closed");
    Check((await client.GetStringAsync("/Download")).Contains("not been published"), "Missing release cannot advertise a fake download");
    var release = await client.GetAsync("/api/v1/release");
    Check(release.StatusCode == HttpStatusCode.NotFound && (await release.Content.ReadAsStringAsync()).Contains("release_unavailable"), "Release descriptor answers 404 without a published installer, even unconfigured");
    Check(landing.Headers.Contains("Content-Security-Policy"), "Security headers present");
    var landingHtml = await landing.Content.ReadAsStringAsync();
    Check(landingHtml.Contains("How to start") && landingHtml.Contains("/Levels") && !landingHtml.Contains("/auth/signup"), "Landing explains the product and never offers sign-up while unconfigured");
    var pricing = await client.GetAsync("/Levels");
    var pricingHtml = await pricing.Content.ReadAsStringAsync();
    Check(pricing.IsSuccessStatusCode && new[] { "Flying as a Pilot", "Premium pilot", "Community airline", "Pro airline", "Link", "Pricing announced" }.All(pricingHtml.Contains) && !pricingHtml.Contains("$") && !pricingHtml.Contains("<script"),
        "Levels page lists every level anonymously, names Link, shows no prices and stays script-free");
}
var connection = Environment.GetEnvironmentVariable("ALPHA6_SERVER_TEST_DATABASE");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("ALPHA6_SERVER_TEST_DATABASE is required for authenticated integration tests.");
    return 1;
}
foreach (var productionConnection in new string?[] { null, connection })
{
    try
    {
        using var production = new ServerFactory(productionConnection, "Production");
        using var unexpectedClient = production.CreateClient();
        Check(false, "Production rejects missing identity or persistent encrypted keyring configuration");
    }
    catch (InvalidOperationException exception)
    {
        Check(exception.Message.Contains(productionConnection is null ? "Configure Auth0" : "DataProtection"),
            productionConnection is null ? "Production rejects missing identity configuration" : "Production rejects missing encrypted keyring configuration");
    }
}
using var host = new ServerFactory(connection);
using var anonymous = host.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
using (var scope = host.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
Check((await anonymous.GetAsync("/api/v1/me/bootstrap")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous API is 401, never a browser redirect");
Check((await anonymous.GetStringAsync("/Levels")).Contains("href=\"/auth/signup\""), "Configured levels page sends new visitors to sign-up");
var run = Guid.NewGuid().ToString("N");
var owner = "owner-" + run;
using var pilot = host.Client(owner);
var bootstrap = await pilot.GetAsync("/api/v1/me/bootstrap");
Check(bootstrap.IsSuccessStatusCode, "Verified signed account bootstrap succeeds");
var bootstrapBody = await bootstrap.Content.ReadFromJsonAsync<BootstrapResponse>();
Check(string.Equals(bootstrapBody?.Account.Email, owner + "@example.test", StringComparison.OrdinalIgnoreCase), "Bootstrap uses trusted token identity");
Check(bootstrapBody?.Profile is { WeightUnit: "LBS", LastSeenAt: not null }, "Bootstrap includes a provisioned profile");
pilot.DefaultRequestHeaders.UserAgent.ParseAdd("Alpha6OPS/0.16.0");
Check((await pilot.GetFromJsonAsync<BootstrapResponse>("/api/v1/me/bootstrap"))?.Profile?.LastSeenVersion == "0.16.0", "Bootstrap records the desktop version from its user agent");
var profilePut = await pilot.PutAsJsonAsync("/api/v1/me/profile", new UpdateProfileRequest("reece74", "A6-001", "kjfk", "KG", "FT", "FT", "personal", "", "RC"));
Check(profilePut.IsSuccessStatusCode && (await profilePut.Content.ReadFromJsonAsync<UserProfile>())?.HomeBaseIcao == "KJFK", "Profile round trip over the API");
Check((await pilot.GetFromJsonAsync<UserProfile>("/api/v1/me/profile"))?.SimBriefUsername == "reece74", "Profile GET returns the saved values");
var badProfile = await pilot.PutAsJsonAsync("/api/v1/me/profile", new UpdateProfileRequest("x", "", "", "LBS", "FT", "FT", "last_used", "", ""));
Check(badProfile.StatusCode == HttpStatusCode.BadRequest && (await badProfile.Content.ReadAsStringAsync()).Contains("invalid_profile"), "Invalid profile is rejected with a stable code");
var planPut = await pilot.PutAsJsonAsync("/api/v1/me/plan", new SetPersonalPlanRequest(PersonalPlan.Premium));
Check(planPut.IsSuccessStatusCode && (await planPut.Content.ReadFromJsonAsync<PersonalEntitlement>()) is { Plan: PersonalPlan.Premium, Status: "complimentary" }, "Complimentary Premium applied over the API");
using (var noMfa = host.Client(owner, "no-mfa"))
{
    var blocked = await noMfa.PostAsJsonAsync("/api/v1/virtual-airlines", new CreateAirlineRequest("Blocked " + run[..8], "blocked-" + run[..12], "BLK"));
    Check(blocked.StatusCode == HttpStatusCode.Forbidden && (await blocked.Content.ReadAsStringAsync()).Contains("\"code\":\"mfa_required\""), "Creation without fresh MFA returns the mfa_required problem code the desktop keys on");
    Check((await noMfa.GetAsync("/api/v1/me/bootstrap")).IsSuccessStatusCode, "Bootstrap never requires MFA");
}

foreach (var scenario in new[] { "missing-claim", "wrong-issuer", "wrong-audience", "expired", "wrong-signature", "future-mfa" })
{
    using var bad = host.Client(owner, scenario);
    var response = await bad.GetAsync("/api/v1/me/bootstrap");
    Check(response.StatusCode == HttpStatusCode.Unauthorized, $"Reject {scenario}");
}
var oidc = host.Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>>().Get("Auth0");
Check(!oidc.ClaimActions.Any(a => a.ClaimType == "iss") && !oidc.GetClaimsFromUserInfoEndpoint, "Portal login keeps the issuer claim the cookie principal is validated against");
using var browser = host.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
var cookieOptions = host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
var identity = new ClaimsIdentity(host.Claims(owner).Select(p => new Claim(p.Key, p.Value.ToString()!)), CookieAuthenticationDefaults.AuthenticationScheme);
var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }, CookieAuthenticationDefaults.AuthenticationScheme);
browser.DefaultRequestHeaders.Add("Cookie", "__Host-Alpha6.Session=" + cookieOptions.TicketDataFormat.Protect(ticket));
Check((await browser.GetAsync("/api/v1/me/bootstrap")).StatusCode == HttpStatusCode.Unauthorized, "Website cookie cannot authenticate the desktop API");
var accountPage = await browser.GetAsync("/Account");
Check(accountPage.IsSuccessStatusCode, "Authenticated portal workspace renders");
var accountHtml = await accountPage.Content.ReadAsStringAsync();
Check(accountHtml.Contains("__RequestVerificationToken"), "Portal mutation forms contain antiforgery token");
Check((await browser.PostAsync("/Account?handler=Logout", new FormUrlEncodedContent([]))).StatusCode == HttpStatusCode.BadRequest, "Logout without CSRF token is rejected");
var csp = accountPage.Headers.GetValues("Content-Security-Policy").Single();
Check(Regex.IsMatch(csp, @"form-action 'self' " + Regex.Escape(ServerFactory.Issuer.TrimEnd('/')) + "(;|$)") && csp.Contains("script-src 'none'"), "CSP lets the sign-out form redirect to the identity provider and nowhere else");
Check((await browser.PostAsync("/Account/Create", new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "Forged" }))).StatusCode == HttpStatusCode.BadRequest, "Airline creation without CSRF token is rejected");
Check(accountHtml.Contains("/Account/Profile") && accountHtml.Contains("/Account/Plan") && accountHtml.Contains("/Download"), "Workspace page links profile, account level and download");
Check(accountHtml.Contains("from v0.16.0"), "Workspace page reports the last desktop version seen");
var profilePage = await browser.GetAsync("/Account/Profile");
var profileHtml = await profilePage.Content.ReadAsStringAsync();
Check(profilePage.IsSuccessStatusCode && profileHtml.Contains("value=\"reece74\"") && Regex.IsMatch(profileHtml, "<option (selected=\"selected\" value=\"personal\"|value=\"personal\" selected=\"selected\")>"), "Profile page renders the saved profile");
Check((await browser.PostAsync("/Account/Profile", new FormUrlEncodedContent(new Dictionary<string, string> { ["SimBriefUsername"] = "forged" }))).StatusCode == HttpStatusCode.BadRequest, "Profile update without CSRF token is rejected");
var planPage = await browser.GetAsync("/Account/Plan");
var planHtml = await planPage.Content.ReadAsStringAsync();
Check(planPage.IsSuccessStatusCode && planHtml.Contains("value=\"Premium\" checked"), "Account level page shows the current level");
Check((await browser.PostAsync("/Account/Plan", new FormUrlEncodedContent(new Dictionary<string, string> { ["plan"] = "Free" }))).StatusCode == HttpStatusCode.BadRequest, "Account level change without CSRF token is rejected");
// A genuine browser submission: antiforgery cookie from the GET plus the hidden form token.
static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, HttpResponseMessage page, string html, string path, Dictionary<string, string> fields)
{
    var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    var antiforgery = page.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal)) : null;
    using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(fields.Append(new("__RequestVerificationToken", token))) };
    request.Headers.Add("Cookie", string.Join("; ", client.DefaultRequestHeaders.GetValues("Cookie").Append(antiforgery ?? "")));
    return await client.SendAsync(request);
}
var profilePost = await SubmitAsync(browser, profilePage, profileHtml, "/Account/Profile", new() { ["SimBriefUsername"] = "", ["Callsign"] = "", ["HomeBaseIcao"] = "kmke", ["WeightUnit"] = "LBS", ["AltitudeUnit"] = "FT", ["LandingDistanceUnit"] = "FT", ["PreferredWorkspace"] = "portal", ["TimeZone"] = "", ["AvatarInitials"] = "rc" });
var profileAfter = await pilot.GetFromJsonAsync<UserProfile>("/api/v1/me/profile");
Check(profilePost.StatusCode == HttpStatusCode.Redirect && profileAfter is { SimBriefUsername: "", HomeBaseIcao: "KMKE", PreferredWorkspace: "portal", AvatarInitials: "RC" }, "Profile form accepts blank optional fields and saves the rest");
var planPost = await SubmitAsync(browser, planPage, planHtml, "/Account/Plan", new() { ["plan"] = "Free" });
Check(planPost.StatusCode == HttpStatusCode.Redirect && (await pilot.GetFromJsonAsync<BootstrapResponse>("/api/v1/me/bootstrap"))?.PersonalEntitlement.Plan == PersonalPlan.Free, "Account level form applies the chosen level");
Check((await SubmitAsync(browser, planPage, planHtml, "/Account/Plan", new() { ["plan"] = "Premium" })).StatusCode == HttpStatusCode.Redirect, "Account level can be restored from the web");

var created = await pilot.PostAsJsonAsync("/api/v1/virtual-airlines", new CreateAirlineRequest("Server Test " + run[..8], "server-" + run[..12], "TEST"));
Check(created.IsSuccessStatusCode, "Owner creates airline with recent MFA proof");
if (created.IsSuccessStatusCode)
{
    var airline = (await created.Content.ReadFromJsonAsync<AirlineWorkspace>())!;
    using var outsider = host.Client("outsider-" + run);
    var denied = await outsider.GetAsync($"/api/v1/virtual-airlines/{airline.Id}/members");
    Check(denied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, "Cross-tenant roster is denied");
    var airlinePlan = await pilot.PutAsJsonAsync($"/api/v1/virtual-airlines/{airline.Id}/plan", new SetAirlinePlanRequest(AirlinePlan.Pro));
    Check(airlinePlan.IsSuccessStatusCode && (await airlinePlan.Content.ReadFromJsonAsync<AirlineWorkspace>())?.Plan == AirlinePlan.Pro, "Owner applies complimentary Pro to the airline");
    Check((await outsider.PutAsJsonAsync($"/api/v1/virtual-airlines/{airline.Id}/plan", new SetAirlinePlanRequest(AirlinePlan.Community))).StatusCode == HttpStatusCode.Forbidden, "Non-members cannot change an airline plan");
    var invite = await pilot.PostAsJsonAsync($"/api/v1/virtual-airlines/{airline.Id}/invitations", new InviteMemberRequest("outsider-" + run + "@example.test", [AirlineRole.Pilot]));
    Check(invite.IsSuccessStatusCode, "Authorized invitation issuance");
    if (invite.IsSuccessStatusCode)
    {
        var invitation = (await invite.Content.ReadFromJsonAsync<IssuedInvitation>())!;
        Check((await outsider.PostAsJsonAsync("/api/v1/invitations/accept", new { token = invitation.Token })).IsSuccessStatusCode, "Invitation accepts body token without secret in URL");
        Check(!(await pilot.GetStringAsync($"/api/v1/virtual-airlines/{airline.Id}/invitations")).Contains(invitation.Token), "Invitation listing never returns raw token");
        var activityLog = await pilot.GetStringAsync($"/api/v1/virtual-airlines/{airline.Id}/activity");
        Check(activityLog.Contains("invitation.issued") && !activityLog.Contains(invitation.Token), "Activity log lists operator actions without secrets");
        Check((await outsider.GetAsync($"/api/v1/virtual-airlines/{airline.Id}/activity")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, "Activity log is administrator-only");
        var airlinePage = await browser.GetAsync($"/Account/Airline/{airline.Id}");
        var airlineHtml = await airlinePage.Content.ReadAsStringAsync();
        Check(airlinePage.IsSuccessStatusCode && airlineHtml.Contains("Airline level") && airlineHtml.Contains("value=\"Pro\" checked"), "Airline page offers the owner the level form with the current level");
        Check(airlineHtml.Contains("invitation.issued") && airlineHtml.Contains("airline.plan_changed") && !airlineHtml.Contains(invitation.Token), "Airline page shows the activity log without secrets");
        Check((await browser.PostAsync($"/Account/Airline/{airline.Id}?handler=Plan", new FormUrlEncodedContent(new Dictionary<string, string> { ["plan"] = "Community" }))).StatusCode == HttpStatusCode.BadRequest, "Airline level change without CSRF token is rejected");
        var airlinePlanPost = await SubmitAsync(browser, airlinePage, airlineHtml, $"/Account/Airline/{airline.Id}?handler=Plan", new() { ["plan"] = "Community" });
        Check(airlinePlanPost.StatusCode == HttpStatusCode.Redirect && (await pilot.GetFromJsonAsync<AirlineWorkspace>($"/api/v1/virtual-airlines/{airline.Id}"))?.Plan == AirlinePlan.Community, "Airline level form applies the chosen level");

        // Schedule and fleet: API for the desktop, console pages for the browser, dispatcher-level access without MFA.
        var routePost = await pilot.PostAsJsonAsync($"/api/v1/virtual-airlines/{airline.Id}/routes", new RouteRequest("a6 101", "kmke", "kord", "14:35", 65, 31, "a20n", "First wave", true));
        var route = await routePost.Content.ReadFromJsonAsync<RouteResponse>();
        Check(routePost.IsSuccessStatusCode && route is { FlightNumber: "A6101", Origin: "KMKE", DaysOfWeek: 31 }, "Route created over the API");
        Check((await pilot.PostAsJsonAsync($"/api/v1/virtual-airlines/{airline.Id}/routes", new RouteRequest("A6101", "KMKE", "KORD", "14:35", 65, 31, "", null, true))).StatusCode == HttpStatusCode.Conflict, "Duplicate route is a conflict");
        using (var noMfa = host.Client(owner, "no-mfa"))
            Check((await noMfa.PutAsJsonAsync($"/api/v1/virtual-airlines/{airline.Id}/routes/{route!.Id}", new RouteRequest("A6101", "KMKE", "KORD", "15:00", 70, 127, "A20N", "", true))).IsSuccessStatusCode, "Schedule edits do not require a fresh security check");
        Check((await outsider.GetAsync($"/api/v1/virtual-airlines/{airline.Id}/routes")).StatusCode == HttpStatusCode.OK, "Members read the schedule");
        Check((await outsider.PostAsJsonAsync($"/api/v1/virtual-airlines/{airline.Id}/routes", new RouteRequest("A6102", "KORD", "KMKE", "17:10", 55, 96, "", null, true))).StatusCode == HttpStatusCode.Forbidden, "Pilots cannot edit the schedule");
        var import = await (await pilot.PostAsJsonAsync($"/api/v1/virtual-airlines/{airline.Id}/routes/import", new ScheduleImportRequest("A6102,KORD,KMKE,17:10,55,67,,Weekend return\nbad,KMKE,KMKE,17:10,55,12"))).Content.ReadFromJsonAsync<ScheduleImportResult>();
        Check(import is { Created: 1, Skipped: 1 }, "CSV import over the API");
        var tailPost = await pilot.PostAsJsonAsync($"/api/v1/virtual-airlines/{airline.Id}/fleet", new AircraftRequest("n123a6", "b738", "Spirit of Milwaukee", "kmke", "active", null));
        var tail = await tailPost.Content.ReadFromJsonAsync<AircraftResponse>();
        Check(tailPost.IsSuccessStatusCode && tail is { Registration: "N123A6", TypeIcao: "B738" }, "Aircraft added over the API");
        var schedulePage = await browser.GetAsync($"/Account/Airline/{airline.Id}/Schedule");
        var scheduleHtml = await schedulePage.Content.ReadAsStringAsync();
        Check(schedulePage.IsSuccessStatusCode && scheduleHtml.Contains("A6101") && scheduleHtml.Contains("A6102") && scheduleHtml.Contains("Add a route") && scheduleHtml.Contains("Export CSV"), "Schedule console lists routes with the editor for operations roles");
        var routeForm = await SubmitAsync(browser, schedulePage, scheduleHtml, $"/Account/Airline/{airline.Id}/Schedule?handler=Save",
            new() { ["flightNumber"] = "A6103", ["origin"] = "KMKE", ["destination"] = "KMSP", ["departureUtc"] = "09:15", ["blockMinutes"] = "75", ["days"] = "0", ["aircraftType"] = "", ["notes"] = "", ["active"] = "true" });
        Check(routeForm.StatusCode == HttpStatusCode.Redirect && (await pilot.GetFromJsonAsync<RouteResponse[]>($"/api/v1/virtual-airlines/{airline.Id}/routes"))!.Any(r => r.FlightNumber == "A6103" && r.DaysOfWeek == 1), "Schedule form adds a route");
        var export = await browser.GetAsync($"/Account/Airline/{airline.Id}/Schedule?handler=Export");
        Check(export.Content.Headers.ContentType?.MediaType == "text/csv" && (await export.Content.ReadAsStringAsync()).Contains("A6103,KMKE,KMSP,09:15,75,1,,"), "Schedule exports as CSV");
        var fleetPage = await browser.GetAsync($"/Account/Airline/{airline.Id}/Fleet");
        var fleetHtml = await fleetPage.Content.ReadAsStringAsync();
        Check(fleetPage.IsSuccessStatusCode && fleetHtml.Contains("N123A6") && fleetHtml.Contains("Spirit of Milwaukee"), "Fleet console lists aircraft");
        var fleetForm = await SubmitAsync(browser, fleetPage, fleetHtml, $"/Account/Airline/{airline.Id}/Fleet?handler=Save",
            new() { ["aircraftId"] = tail!.Id.ToString(), ["registration"] = "N123A6", ["typeIcao"] = "B738", ["name"] = "Spirit of Milwaukee", ["homeBase"] = "KMKE", ["status"] = "maintenance", ["notes"] = "C check" });
        Check(fleetForm.StatusCode == HttpStatusCode.Redirect && (await pilot.GetFromJsonAsync<AircraftResponse[]>($"/api/v1/virtual-airlines/{airline.Id}/fleet"))!.Single().Status == "maintenance", "Fleet form updates an aircraft");
        Check((await browser.PostAsync($"/Account/Airline/{airline.Id}/Fleet?handler=Delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["aircraftId"] = tail.Id.ToString() }))).StatusCode == HttpStatusCode.BadRequest, "Fleet removal without CSRF token is rejected");
    }
}
using (var limited = host.Client("rate-" + run, "missing-claim"))
{
    var beforeLimit = true;
    for (var i = 0; i < 120; i++)
        beforeLimit &= (await limited.GetAsync("/api/v1/me/bootstrap")).StatusCode == HttpStatusCode.Unauthorized;
    Check(beforeLimit && (await limited.GetAsync("/api/v1/me/bootstrap")).StatusCode == HttpStatusCode.TooManyRequests,
        "Validated bearer subject receives per-account rate limit");
    using var other = host.Client("other-rate-" + run, "missing-claim");
    Check((await other.GetAsync("/api/v1/me/bootstrap")).StatusCode == HttpStatusCode.Unauthorized,
        "A second account on the same network has its own endpoint rate bucket");
}
var anonymousLimited = false;
for (var i = 0; i < 301 && !anonymousLimited; i++)
    anonymousLimited = (await anonymous.GetAsync("/api/v1/me/bootstrap")).StatusCode == HttpStatusCode.TooManyRequests;
Check(anonymousLimited, "Anonymous authentication traffic is rate limited before token validation");
Console.WriteLine($"Server integration tests complete: {failures} failures.");
return failures == 0 ? 0 : 1;

sealed class ServerFactory(string? connection, string environment = "Development") : WebApplicationFactory<ServerSettings>
{
    private readonly RSA key = RSA.Create(2048);
    internal const string Issuer = "https://identity.example.test/";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Alpha6Ops.Server")));
        builder.UseEnvironment(environment);
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
        builder.UseSetting("Auth0:Authority", connection is null ? "" : Issuer);
        builder.UseSetting("Auth0:Audience", connection is null ? "" : "https://api.example.test");
        builder.UseSetting("Auth0:ClientId", connection is null ? "" : "test-client");
        builder.UseSetting("Auth0:ClientSecret", connection is null ? "" : "test-only-not-a-real-secret");
        builder.UseSetting("ConnectionStrings:Accounts", connection ?? "");
        builder.UseSetting("DataProtection:KeyDirectory", "");
        builder.UseSetting("DataProtection:CertificatePath", "");
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
            var config = new OpenIdConnectConfiguration { Issuer = Issuer };
            config.SigningKeys.Add(new RsaSecurityKey(key) { KeyId = "integration-only" });
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(config);
            });
        });
    }
    public Dictionary<string, object> Claims(string subject) => new()
    {
        ["sub"] = subject, ["iss"] = Issuer,
        [TrustedActor.Prefix + "email"] = subject + "@example.test",
        [TrustedActor.Prefix + "email_verified"] = true,
        [TrustedActor.Prefix + "display_name"] = subject,
        [TrustedActor.Prefix + "mfa"] = true,
        [TrustedActor.Prefix + "mfa_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };
    public HttpClient Client(string subject, string? variant = null)
    {
        var claims = Claims(subject);
        claims.Remove("iss");
        if (variant == "missing-claim") claims.Remove(TrustedActor.Prefix + "email_verified");
        if (variant == "future-mfa") claims[TrustedActor.Prefix + "mfa_at"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        if (variant == "no-mfa") { claims[TrustedActor.Prefix + "mfa"] = false; claims.Remove(TrustedActor.Prefix + "mfa_at"); }
        using var otherKey = RSA.Create(2048);
        var securityKey = new RsaSecurityKey(variant == "wrong-signature" ? otherKey : key) { KeyId = "integration-only" };
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = variant == "wrong-issuer" ? "https://attacker.example.test/" : Issuer,
            Audience = variant == "wrong-audience" ? "wrong" : "https://api.example.test",
            Claims = claims, IssuedAt = DateTime.UtcNow.AddMinutes(-20), NotBefore = DateTime.UtcNow.AddMinutes(-20),
            Expires = variant == "expired" ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new(securityKey, SecurityAlgorithms.RsaSha256)
        };
        var client = CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JsonWebTokenHandler().CreateToken(descriptor));
        return client;
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) key.Dispose(); }
}
