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
// Hosted (disk-less) production: certificate from configuration, key ring in the account database, shared by replicas.
string hostedPfx;
using (var rsa = RSA.Create(2048))
{
    var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Alpha6Ops test keyring", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    hostedPfx = Convert.ToBase64String(certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, "test-pfx"));
}
string protectedValue;
using (var hosted = new ServerFactory(connection, "Production", hostedPfx))
{
    using var hostedClient = hosted.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
    Check((await hostedClient.GetStringAsync("/healthz")) == "ok", "Hosted production starts with a base64 certificate and database key ring");
    protectedValue = hosted.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("hosting-check").Protect("same sky");
    using var scope = hosted.Services.CreateScope();
    Check(await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().DataProtectionKeys.AnyAsync(), "Data-protection keys are persisted in the database");
    using var proxied = new HttpRequestMessage(HttpMethod.Get, "http://localhost/healthz");
    proxied.Headers.Add("X-Forwarded-Proto", "https");
    Check((await hostedClient.SendAsync(proxied)).StatusCode == HttpStatusCode.OK, "Requests arriving through the hosting proxy are treated as HTTPS");
    // Render's health check arrives with the public host name and no forwarded headers; Hosting:PublicHosts admits it.
    using var publicHost = new HttpRequestMessage(HttpMethod.Get, "http://localhost/healthz");
    publicHost.Headers.Host = "alpha6ops.example.test";
    Check((await hostedClient.SendAsync(publicHost)).StatusCode == HttpStatusCode.OK, "Health check under the configured public host name is 200");
    using var strangerHost = new HttpRequestMessage(HttpMethod.Get, "http://localhost/healthz");
    strangerHost.Headers.Host = "evil.example.test";
    Check((await hostedClient.SendAsync(strangerHost)).StatusCode == HttpStatusCode.BadRequest, "Host filtering still rejects unlisted host names");
    // Cloudflare in front of Render: Render appends the connecting peer (a Cloudflare edge) to X-Forwarded-For, and
    // Cloudflare names the visitor in CF-Connecting-IP. The visitor counts only when the peer really is Cloudflare.
    // Each case exhausts one bucket, then sends a single request as a different visitor: it must be refused only when
    // that request is (correctly) attributed to the exhausted bucket.
    async Task<HttpStatusCode> ViaAsync(string peer, string visitor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/v1/me/bootstrap");
        request.Headers.Add("X-Forwarded-For", peer);
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("CF-Connecting-IP", visitor);
        return (await hostedClient.SendAsync(request)).StatusCode;
    }
    async Task<bool> ExhaustAsync(string peer, Func<int, string> visitor)
    {
        for (var i = 0; i <= 300; i++)
            if (await ViaAsync(peer, visitor(i)) == HttpStatusCode.TooManyRequests) return true;
        return false;
    }
    Check(await ExhaustAsync("173.245.48.10", _ => "203.0.113.7") && await ViaAsync("173.245.48.10", "203.0.113.7") == HttpStatusCode.TooManyRequests,
        "One visitor behind Cloudflare is rate limited by their own address");
    Check(await ViaAsync("173.245.48.10", "203.0.113.8") != HttpStatusCode.TooManyRequests,
        "Another visitor through the same Cloudflare edge keeps a separate bucket");
    Check(await ExhaustAsync("198.51.100.9", i => "192.0.2." + (i % 250 + 1)) && await ViaAsync("198.51.100.9", "192.0.2.251") == HttpStatusCode.TooManyRequests,
        "A forged CF-Connecting-IP from outside Cloudflare cannot escape the per-address limit");
    Check(await ExhaustAsync("2400:cb00::1", _ => "garbage") && await ViaAsync("2400:cb00::1", "not-an-address") == HttpStatusCode.TooManyRequests,
        "An unparseable CF-Connecting-IP falls back to the Cloudflare peer address");
}
Check(CloudflareNetworks.Contains(IPAddress.Parse("104.16.1.1")) && CloudflareNetworks.Contains(IPAddress.Parse("2606:4700::6810:1"))
    && CloudflareNetworks.Contains(IPAddress.Parse("::ffff:173.245.48.1")) && !CloudflareNetworks.Contains(IPAddress.Parse("203.0.113.7"))
    && !CloudflareNetworks.Contains(IPAddress.Parse("10.0.0.1")), "Cloudflare edge ranges match published IPv4, IPv6 and mapped addresses only");
using (var replica = new ServerFactory(connection, "Production", hostedPfx))
    Check(replica.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("hosting-check").Unprotect(protectedValue) == "same sky", "A second instance reads the same key ring, so cookies survive deploys");
Check((await anonymous.GetStringAsync("/healthz")) == "ok", "Health endpoint answers without authentication");
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
// Logbook: API import for the desktop, page with the same form, export, undo.
var logbookImport = await pilot.PostAsJsonAsync("/api/v1/me/flights/import", new LogbookImportRequest("Date;Flight;From;To;Aircraft;Block Time;Landing Rate\n14/03/2026 18:20;CZK101;KMKE;KORD;A20N;0:55;-180\n2026-03-15 09:00;CZK102;KORD;KMKE;A20N;50;-240\n"));
var imported = await logbookImport.Content.ReadFromJsonAsync<LogbookImportResult>();
Check(logbookImport.IsSuccessStatusCode && imported is { Created: 2, Duplicates: 0 }, "Logbook CSV imports over the API");
Check((await pilot.GetFromJsonAsync<LogbookSummary>("/api/v1/me/flights/summary")) is { Flights: 2, BlockMinutes: 105 }, "Logbook summary over the API");
var logbookPage = await browser.GetAsync("/Account/Logbook");
var logbookHtml = await logbookPage.Content.ReadAsStringAsync();
Check(logbookPage.IsSuccessStatusCode && logbookHtml.Contains("CZK101") && logbookHtml.Contains("-180 fpm") && logbookHtml.Contains("Import a logbook"), "Logbook page lists imported flights");
Check((await browser.GetStringAsync("/Account/Logbook?handler=Export")).Contains("2026-03-14 18:20,,CZK101,KMKE,KORD,A20N,,55,"), "Logbook exports as CSV");
var undo = await SubmitAsync(browser, logbookPage, logbookHtml, "/Account/Logbook?handler=Undo", new() { ["batchId"] = imported!.BatchId.ToString() });
Check(undo.StatusCode == HttpStatusCode.Redirect && (await pilot.GetFromJsonAsync<LogbookSummary>("/api/v1/me/flights/summary"))?.Flights == 0, "Undo removes the imported batch from the page");
var oversized = await browser.PostAsync("/Account/Logbook?handler=Import", new StringContent(new string('x', 4_000_000)));
Check(oversized.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest, "Oversized logbook uploads are refused");

// Media: upload through the profile form, serve from /media with caching, reject non-images.
// A complete, decodable PNG (solid RGBA fill) so snapshot screenshots show the image rather than a broken one.
static byte[] Png(int w, int h, uint rgb = 0x3CC8E8)
{
    static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data) { crc ^= b; for (var k = 0; k < 8; k++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1)); }
        return ~crc;
    }
    static void Chunk(MemoryStream png, string type, byte[] data)
    {
        var typed = new byte[4 + data.Length]; System.Text.Encoding.ASCII.GetBytes(type, typed); data.CopyTo(typed, 4);
        Span<byte> word = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(word, (uint)data.Length); png.Write(word);
        png.Write(typed);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(word, Crc(typed)); png.Write(word);
    }
    var header = new byte[13];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), (uint)w);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)h);
    header[8] = 8; header[9] = 6;
    var row = new byte[1 + w * 4];
    for (var x = 0; x < w; x++) { row[1 + x * 4] = (byte)(rgb >> 16); row[2 + x * 4] = (byte)(rgb >> 8); row[3 + x * 4] = (byte)rgb; row[4 + x * 4] = 0xFF; }
    var pixels = new MemoryStream();
    using (var zlib = new System.IO.Compression.ZLibStream(pixels, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        for (var y = 0; y < h; y++) zlib.Write(row);
    var png = new MemoryStream();
    png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    Chunk(png, "IHDR", header); Chunk(png, "IDAT", pixels.ToArray()); Chunk(png, "IEND", []);
    return png.ToArray();
}
static async Task<HttpResponseMessage> UploadAsync(HttpClient client, HttpResponseMessage page, string html, string path, string field, byte[] bytes, string fileName)
{
    var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    var antiforgery = page.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal)) : null;
    var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
    var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    content.Add(file, field, fileName);
    using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
    request.Headers.Add("Cookie", string.Join("; ", client.DefaultRequestHeaders.GetValues("Cookie").Append(antiforgery ?? "")));
    return await client.SendAsync(request);
}
var profileForAvatar = await browser.GetAsync("/Account/Profile");
var profileForAvatarHtml = await profileForAvatar.Content.ReadAsStringAsync();
var avatarUpload = await UploadAsync(browser, profileForAvatar, profileForAvatarHtml, "/Account/Profile?handler=Avatar", "avatar", Png(160, 160), "me.png");
var profileWithAvatar = await pilot.GetFromJsonAsync<UserProfile>("/api/v1/me/profile");
Check(avatarUpload.StatusCode == HttpStatusCode.Redirect && profileWithAvatar?.AvatarUrl.StartsWith("/media/avatar/") == true, "Avatar upload through the profile form");
var media = await anonymous.GetAsync(profileWithAvatar!.AvatarUrl);
Check(media.IsSuccessStatusCode && media.Content.Headers.ContentType?.MediaType == "image/png" && media.Headers.ETag is not null && media.Headers.CacheControl?.Public == true, "Avatar is served with content type, ETag and caching");
using (var conditional = new HttpRequestMessage(HttpMethod.Get, profileWithAvatar.AvatarUrl)) { conditional.Headers.IfNoneMatch.Add(media.Headers.ETag!); Check((await anonymous.SendAsync(conditional)).StatusCode == HttpStatusCode.NotModified, "Unchanged avatar answers 304"); }
var badUpload = await UploadAsync(browser, profileForAvatar, profileForAvatarHtml, "/Account/Profile?handler=Avatar", "avatar", "<svg onload=alert(1)></svg>"u8.ToArray(), "evil.png");
Check(badUpload.StatusCode == HttpStatusCode.BadRequest && (await badUpload.Content.ReadAsStringAsync()).Contains("PNG, JPEG or WebP"), "Non-image uploads are rejected by content, not file name");
Check((await anonymous.GetAsync($"/media/avatar/{Guid.NewGuid():N}")).StatusCode == HttpStatusCode.NotFound && (await anonymous.GetAsync($"/media/other/{Guid.NewGuid():N}")).StatusCode == HttpStatusCode.NotFound, "Unknown media is 404");
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
        var logoUpload = await UploadAsync(browser, airlinePage, airlineHtml, $"/Account/Airline/{airline.Id}?handler=Logo", "logo", Png(400, 200, 0xE15BE0), "logo.png");
        var branded = await pilot.GetFromJsonAsync<AirlineWorkspace>($"/api/v1/virtual-airlines/{airline.Id}");
        Check(logoUpload.StatusCode == HttpStatusCode.Redirect && branded?.LogoUrl.StartsWith("/media/airline-logo/") == true && (await anonymous.GetAsync(branded.LogoUrl)).IsSuccessStatusCode, "Airline logo upload through the console and served publicly");
        Check((await browser.GetStringAsync($"/Account/Airline/{airline.Id}/Schedule")).Contains(branded!.LogoUrl), "Console header shows the logo");
        // Design review aid: with S4OPS_PORTAL_SNAPSHOT_DIR set, save every rendered page (and the media it shows) so the
        // portal can be screenshotted signed in without a real identity provider. Not a check.
        if (Environment.GetEnvironmentVariable("S4OPS_PORTAL_SNAPSHOT_DIR") is { Length: > 0 } snapshotDir)
        {
            await pilot.PostAsJsonAsync("/api/v1/me/flights/import", new LogbookImportRequest("Date;Flight;From;To;Aircraft;Block Time;Landing Rate\n2026-09-20 18:20;S4601;KMKE;KORD;A20N;0:55;-180\n2026-09-21 20:30;S4602;KORD;KDEN;B738;2:35;-240\n"));
            Directory.CreateDirectory(snapshotDir);
            var snapshots = new Dictionary<string, (HttpClient Client, string Path)>
            {
                ["index"] = (anonymous, "/"), ["levels"] = (anonymous, "/Levels"), ["download"] = (anonymous, "/Download"),
                ["support"] = (anonymous, "/Support"), ["signed-out"] = (anonymous, "/SignedOut"), ["auth-error"] = (anonymous, "/AuthError"),
                ["account"] = (browser, "/Account"), ["profile"] = (browser, "/Account/Profile"), ["plan"] = (browser, "/Account/Plan"),
                ["logbook"] = (browser, "/Account/Logbook"), ["create"] = (browser, "/Account/Create"), ["join"] = (browser, "/Account/Join"),
                ["airline"] = (browser, $"/Account/Airline/{airline.Id}"), ["schedule"] = (browser, $"/Account/Airline/{airline.Id}/Schedule"),
                ["fleet"] = (browser, $"/Account/Airline/{airline.Id}/Fleet")
            };
            foreach (var (name, target) in snapshots)
                await File.WriteAllTextAsync(Path.Combine(snapshotDir, name + ".html"), await target.Client.GetStringAsync(target.Path));
            foreach (var mediaUrl in new[] { profileWithAvatar!.AvatarUrl, branded.LogoUrl })
            {
                var mediaPath = mediaUrl.Split('?')[0];
                var file = Path.Combine(snapshotDir, mediaPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllBytesAsync(file, await anonymous.GetByteArrayAsync(mediaUrl));
            }
            Console.WriteLine("Portal snapshot written to " + snapshotDir);
        }
        // Privacy, terms, data export and self-service deletion.
        foreach (var legal in new[] { "/Privacy", "/Terms" })
        {
            var legalPage = await anonymous.GetAsync(legal);
            var legalHtml = await legalPage.Content.ReadAsStringAsync();
            Check(legalPage.IsSuccessStatusCode && legalHtml.Contains("Sierra 4") && !legalHtml.Contains("<script"), $"{legal} renders anonymously without scripts");
        }
        var dataExport = await browser.GetAsync("/Account/Profile?handler=Export");
        using (var zip = new System.IO.Compression.ZipArchive(await dataExport.Content.ReadAsStreamAsync()))
            Check(dataExport.Content.Headers.ContentType?.MediaType == "application/zip" && zip.Entries.Any(e => e.FullName == "logbook.json")
                && zip.Entries.Any(e => e.FullName == "profile-picture.png"), "Profile exports the account as a ZIP with the profile picture");
        var ownerDelete = await browser.GetStringAsync("/Account/Delete");
        Check(ownerDelete.Contains("Transfer your airlines first") && !ownerDelete.Contains("handler=Request"), "Delete page asks an owner to transfer airlines first");
        // A pilot without MFA: a stale sign-in is sent to sign in again; a fresh one schedules deletion, which locks
        // every other page to the delete page until it is cancelled.
        HttpClient Leaver(DateTimeOffset signedInAt)
        {
            var claims = host.Claims("leaver-" + run);
            claims[TrustedActor.Prefix + "mfa"] = false; claims.Remove(TrustedActor.Prefix + "mfa_at");
            claims["auth_time"] = signedInAt.ToUnixTimeSeconds();
            var leaverTicket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims.Select(p => new Claim(p.Key, p.Value.ToString()!)), CookieAuthenticationDefaults.AuthenticationScheme)),
                new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }, CookieAuthenticationDefaults.AuthenticationScheme);
            var client = host.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add("Cookie", "__Host-Alpha6.Session=" + cookieOptions.TicketDataFormat.Protect(leaverTicket));
            return client;
        }
        using var staleLeaver = Leaver(DateTimeOffset.UtcNow.AddHours(-1));
        Check((await staleLeaver.GetAsync("/Account")).IsSuccessStatusCode, "A pilot without MFA signs in to the portal");
        var stalePage = await staleLeaver.GetAsync("/Account/Delete");
        var stale = await SubmitAsync(staleLeaver, stalePage, await stalePage.Content.ReadAsStringAsync(), "/Account/Delete?handler=Request", new() { ["Confirm"] = "true" });
        var staleHtml = await stale.Content.ReadAsStringAsync();
        Check(stale.StatusCode == HttpStatusCode.Forbidden && staleHtml.Contains("/auth/reauth?returnUrl=%2FAccount%2FDelete"), "Deletion after a stale sign-in asks the pilot to sign in again");
        using var leaver = Leaver(DateTimeOffset.UtcNow);
        var deletePage = await leaver.GetAsync("/Account/Delete");
        var deleteHtml = await deletePage.Content.ReadAsStringAsync();
        Check((await SubmitAsync(leaver, deletePage, deleteHtml, "/Account/Delete?handler=Request", new() { ["Confirm"] = "false" })).StatusCode == HttpStatusCode.BadRequest, "Deletion needs the confirmation box");
        Check((await SubmitAsync(leaver, deletePage, deleteHtml, "/Account/Delete?handler=Request", new() { ["Confirm"] = "true" })).StatusCode == HttpStatusCode.Redirect, "A fresh sign-in schedules deletion without MFA");
        var locked = await leaver.GetAsync("/Account");
        Check(locked.StatusCode == HttpStatusCode.Redirect && locked.Headers.Location?.OriginalString == "/Account/Delete", "A scheduled account is locked to the delete page");
        var scheduledPage = await leaver.GetAsync("/Account/Delete");
        var scheduledHtml = await scheduledPage.Content.ReadAsStringAsync();
        Check(scheduledHtml.Contains("Erased after") && (await leaver.GetAsync("/Account/Profile?handler=Export")).Content.Headers.ContentType?.MediaType == "application/zip",
            "The delete page shows the erase date and the data can still be downloaded");
        Check((await SubmitAsync(leaver, scheduledPage, scheduledHtml, "/Account/Delete?handler=Cancel", [])).StatusCode == HttpStatusCode.Redirect
            && (await leaver.GetAsync("/Account")).IsSuccessStatusCode, "Cancelling unlocks the account");
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

sealed class ServerFactory(string? connection, string environment = "Development", string? certificateBase64 = null) : WebApplicationFactory<ServerSettings>
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
        builder.UseSetting("DataProtection:CertificateBase64", certificateBase64 ?? "");
        builder.UseSetting("DataProtection:CertificatePassword", certificateBase64 is null ? "" : "test-pfx");
        builder.UseSetting("Hosting:BehindProxy", certificateBase64 is null ? "" : "true");
        if (certificateBase64 is not null) builder.UseSetting("Hosting:PublicHosts", "alpha6ops.example.test");
        builder.UseSetting("Hosting:TrustCloudflare", certificateBase64 is null ? "" : "true");
        builder.ConfigureServices(services =>
        {
            if (certificateBase64 is null) services.AddDataProtection().UseEphemeralDataProtectionProvider();
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
