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
// Preview builds: SigV4 against AWS's published presigned-URL example, then the R2 store over a fake transport.
var awsExample = SigV4.Presign("GET", "examplebucket.s3.amazonaws.com", "/test.txt", new Dictionary<string, string>(), "us-east-1", "s3",
    "AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", new DateTimeOffset(2013, 5, 24, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(1));
Check(awsExample.Query.EndsWith("&X-Amz-Signature=aeeed9bbccd4d02ee5c0109b86d86835f995330da4c265957d157751f604d404"), "Presigned links match AWS's published SigV4 example");
const string listing = """
    <?xml version="1.0" encoding="UTF-8"?>
    <ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/"><Name>builds</Name>
    <Contents><Key>preview/</Key><Size>0</Size><LastModified>2026-10-01T00:00:00.000Z</LastModified></Contents>
    <Contents><Key>preview/Sierra4-Setup-0.23.0-aaaaaaa.exe</Key><Size>99614720</Size><LastModified>2026-10-10T08:00:00.000Z</LastModified></Contents>
    <Contents><Key>preview/Sierra4-Setup-0.23.0-bbbbbbb.exe</Key><Size>99614721</Size><LastModified>2026-10-18T09:30:00.000Z</LastModified></Contents>
    <Contents><Key>other/secret.txt</Key><Size>5</Size><LastModified>2026-10-19T00:00:00.000Z</LastModified></Contents>
    </ListBucketResult>
    """;
var parsedBuilds = R2PreviewBuildStore.Parse(listing, "preview/");
Check(parsedBuilds.Select(b => b.Name).SequenceEqual(["Sierra4-Setup-0.23.0-bbbbbbb.exe", "Sierra4-Setup-0.23.0-aaaaaaa.exe"]) && parsedBuilds[0].Size == 99614721,
    "A bucket listing yields the prefix's builds, newest first, without folder placeholders or other prefixes");
var r2Transport = new CapturingHandler(listing);
var r2Store = new R2PreviewBuildStore(new HttpClient(r2Transport), Options.Create(new PreviewBuildSettings { AccountId = "acct", Bucket = "builds", AccessKeyId = "id", SecretAccessKey = "secret" }),
    TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<R2PreviewBuildStore>.Instance);
var listed = await r2Store.ListAsync(CancellationToken.None);
var listUrl = r2Transport.Request!.RequestUri!;
Check(listed.Length == 2 && listUrl.Host == "acct.r2.cloudflarestorage.com" && listUrl.AbsolutePath == "/builds" && listUrl.Query.Contains("list-type=2") && listUrl.Query.Contains("prefix=preview%2F"),
    "The store lists the bucket's preview prefix with a signed request");
var link = r2Store.DownloadUrl(listed[0]);
Check(link.AbsoluteUri.StartsWith("https://acct.r2.cloudflarestorage.com/builds/preview/Sierra4-Setup-0.23.0-bbbbbbb.exe?") && link.Query.Contains("X-Amz-Expires=300")
    && link.Query.Contains("response-content-disposition=attachment%3B%20filename%3D%22Sierra4-Setup-0.23.0-bbbbbbb.exe%22"), "Download links are signed for five minutes and save under the build's name");
var refusingStore = new R2PreviewBuildStore(new HttpClient(new CapturingHandler("<Error/>", HttpStatusCode.Forbidden)), Options.Create(new PreviewBuildSettings { AccountId = "acct", Bucket = "builds", AccessKeyId = "id", SecretAccessKey = "wrong" }),
    TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<R2PreviewBuildStore>.Instance);
try { await refusingStore.ListAsync(CancellationToken.None); Check(false, "A refused listing is reported"); }
catch (PreviewBuildStoreException ex) { Check(ex.Message.Contains("403"), "A refused listing is reported with its status"); }
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
    Check(landingHtml.Contains("Your S4 OPS account") && landingHtml.Contains("https://s4ops.com/") && !landingHtml.Contains("/auth/signup")
        && !landingHtml.Contains("How it works") && !landingHtml.Contains("Demonstration data") && !landingHtml.Contains("home.css"),
        "Unconfigured entrance points to s4ops.com, carries no marketing and never offers sign-up");
    Check(!landingHtml.Contains("<script") && !Regex.IsMatch(landingHtml, @"\sstyle="""), "Entrance is script-free with no inline styles the CSP would block");
    Check(landingHtml.Contains("Account registration is not available"), "Unconfigured entrance explains that registration is unavailable");
    var leftoverCookie = setup.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
    using (var leftover = setup.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false }))
    {
        var leftoverTicket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "leftover")], CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }, CookieAuthenticationDefaults.AuthenticationScheme);
        leftover.DefaultRequestHeaders.Add("Cookie", "__Host-Alpha6.Session=" + leftoverCookie.TicketDataFormat.Protect(leftoverTicket));
        var leftoverHome = await leftover.GetAsync("/");
        Check(leftoverHome.StatusCode == HttpStatusCode.OK, "A leftover session on an unconfigured server sees the entrance, not a redirect into an unavailable account area");
    }
    var pricing = await client.GetAsync("/Levels");
    var pricingHtml = await pricing.Content.ReadAsStringAsync();
    Check(pricing.IsSuccessStatusCode && new[] { "Flying as a Pilot", "Premium pilot", "Community airline", "Pro airline", "Link", "Pricing announced" }.All(pricingHtml.Contains) && !pricingHtml.Contains("$") && !pricingHtml.Contains("<script"),
        "Levels page lists every level anonymously, names Link, shows no prices and stays script-free");
    // No contact address configured: nothing shows a placeholder or a broken mailto, and Help explains invitations.
    foreach (var page in new[] { "/", "/Privacy", "/Terms", "/Support", "/Download" })
    {
        var html = await client.GetStringAsync(page);
        Check(!html.Contains("[CONTACT") && !html.Contains("[STATE") && !html.Contains("mailto:"), $"{page} shows no contact placeholder or mailto without a configured contact");
    }
    Check((await client.GetStringAsync("/Terms")).Contains("governed by the laws of Wisconsin, United States"), "Terms name Wisconsin by default");
    var guide = await client.GetAsync("/Guide");
    var guideHtml = await guide.Content.ReadAsStringAsync();
    Check(guide.IsSuccessStatusCode && new[] { "Welcome to the S4 OPS test", "Your first flight", "Not working yet", "What we'd like you to try", "If something goes wrong", "Questions" }.All(guideHtml.Contains)
        && !guideHtml.Contains("<script") && !Regex.IsMatch(guideHtml, @"\sstyle="""), "The tester guide is public, script-free and has every section");
    Check(guideHtml.Contains("Fenix A320") && guideHtml.Contains("not code-signed") && guideHtml.Contains("stay in the app"), "The guide states the known limits plainly");
    Check(Regex.Matches(guideHtml, ">If something goes wrong</h2>").Count == 1 && Regex.Matches(guideHtml, ">Your first flight</h2>").Count == 1, "Each guide section appears once");
    var downloadForGuide = await client.GetStringAsync("/Download");
    Check(downloadForGuide.Contains("href=\"/Guide\"") && downloadForGuide.Contains("Confirm parked") && guideHtml.Contains("Confirm parked"), "Download and the guide share the first-flight steps and link each other");
    Check((await client.GetStringAsync("/Support")).Contains("href=\"/Guide\""), "Help links the tester guide");
    var unconfiguredReleases = await client.GetAsync("/Releases");
    Check(unconfiguredReleases.IsSuccessStatusCode && (await unconfiguredReleases.Content.ReadAsStringAsync()).Contains("No release notes yet"), "Releases renders without an account store");
    var help = await client.GetStringAsync("/Support");
    Check(help.Contains("id=\"invitations\"") && help.Contains("invite-only"), "Help explains how invitations work");
    var privacy = await client.GetStringAsync("/Privacy");
    Check(privacy.Contains("Cloudflare Web Analytics") && privacy.Contains("no cookies"), "Privacy discloses cookieless page-view counts on s4ops.com");
    Check(help.Contains("href=\"/Account/Feedback\"") && (await client.GetStringAsync("/Download")).Contains("href=\"/Account/Feedback\"") && privacy.Contains("Problem reports"),
        "Help and Download point at Report a problem, and Privacy lists problem reports");
}
using (var contactSetup = new ServerFactory(null, settings: new() { ["Legal:Contact"] = "help@s4ops.example", ["Legal:Jurisdiction"] = "Ruritania" }))
using (var contactClient = contactSetup.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false }))
{
    foreach (var page in new[] { "/Privacy", "/Terms", "/Support" })
        Check((await contactClient.GetStringAsync(page)).Contains("mailto:help@s4ops.example"), $"{page} links the configured contact address");
    Check((await contactClient.GetStringAsync("/Terms")).Contains("governed by the laws of Ruritania"), "Terms use the configured jurisdiction");
}
using (var badContact = new ServerFactory(null, settings: new() { ["Legal:Contact"] = "[CONTACT EMAIL]" }))
using (var badClient = badContact.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false }))
    Check(!(await badClient.GetStringAsync("/Privacy")).Contains("mailto:"), "A malformed contact setting is treated as no contact");
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
var signedInHome = await browser.GetAsync("/");
Check(signedInHome.StatusCode == HttpStatusCode.Redirect && signedInHome.Headers.Location?.OriginalString == "/Account", "Signed-in home goes straight to the account");
var entrance = await anonymous.GetStringAsync("/");
Check(entrance.Contains("/auth/login") && entrance.Contains("/auth/signup") && entrance.Contains("https://s4ops.com/") && entrance.Contains("invite-only")
    && !entrance.Contains("Every phase recorded") && !entrance.Contains("home.css"), "Signed-out home is a short entrance: sign in, invited sign-up, s4ops.com, no marketing");
var retired = new[] { "/css/home.css", "/images/hero/kmke-plan.svg", "/images/flight-deck.png", "/images/globe.jpg", "/images/pilot.jpg", "/images/routes.jpg", "/images/runway.jpg" };
foreach (var anonymousPage in new[] { "/", "/Levels", "/Download", "/Support", "/Privacy", "/Terms", "/SignedOut", "/AuthError" })
{
    var pageHtml = await anonymous.GetStringAsync(anonymousPage);
    Check(!retired.Any(pageHtml.Contains) && !pageHtml.Contains("s4-lockup"), $"{anonymousPage} references no retired asset");
}
foreach (var gone in retired)
    Check((await anonymous.GetAsync(gone)).StatusCode == HttpStatusCode.NotFound, $"Retired asset {gone} is gone");
var missingPage = await anonymous.GetAsync("/No/Such/Page");
var missingHtml = await missingPage.Content.ReadAsStringAsync();
Check(missingPage.StatusCode == HttpStatusCode.NotFound && missingHtml.Contains("find that page") && missingHtml.Contains("href=\"/\"") && !missingHtml.Contains("<script"),
    "An unknown address is a 404 page with a way home");
var missingApi = await anonymous.GetAsync("/api/v1/no-such-endpoint");
Check(missingApi.StatusCode == HttpStatusCode.NotFound && !(await missingApi.Content.ReadAsStringAsync()).Contains("<html"), "The API keeps plain 404s");
var errorPage = await anonymous.GetAsync("/Error");
var errorHtml = await errorPage.Content.ReadAsStringAsync();
Check(errorPage.StatusCode == HttpStatusCode.InternalServerError && errorHtml.Contains("Something went wrong") && errorHtml.Contains("<code class=\"break\">") && errorHtml.Contains("href=\"/Support\"")
    && !errorHtml.Contains("installation administrator") && !errorHtml.Contains("migrate-accounts"), "The error page gives a reference and a way out, and no database hint without a database failure");
Check(accountPage.IsSuccessStatusCode, "Authenticated portal workspace renders");
var accountHtml = await accountPage.Content.ReadAsStringAsync();
Check(accountHtml.Contains("__RequestVerificationToken"), "Portal mutation forms contain antiforgery token");
Check(Regex.IsMatch(accountHtml, "(Premium pilot|Flying as a Pilot)</a>") && !accountHtml.Contains("Personal Premium") && !accountHtml.Contains("Personal Free"), "The account page names the level the way the Levels page does");
Check((await browser.PostAsync("/Account?handler=Logout", new FormUrlEncodedContent([]))).StatusCode == HttpStatusCode.BadRequest, "Logout without CSRF token is rejected");
var csp = accountPage.Headers.GetValues("Content-Security-Policy").Single();
Check(Regex.IsMatch(csp, @"form-action 'self' " + Regex.Escape(ServerFactory.Issuer.TrimEnd('/')) + "(;|$)") && csp.Contains("script-src 'none'"), "CSP lets the sign-out form redirect to the identity provider and nowhere else");
Check((await browser.PostAsync("/Account/Create", new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "Forged" }))).StatusCode == HttpStatusCode.BadRequest, "Airline creation without CSRF token is rejected");
Check(accountHtml.Contains("/Account/Profile") && accountHtml.Contains("/Account/Plan") && accountHtml.Contains("/Download") && accountHtml.Contains("/Account/Feedback"), "Workspace page links profile, account level, download and Report a problem");
Check(accountHtml.Contains("from v0.16.0"), "Workspace page reports the last desktop version seen");
var profilePage = await browser.GetAsync("/Account/Profile");
var profileHtml = await profilePage.Content.ReadAsStringAsync();
Check(!Regex.IsMatch(profileHtml, @"\sstyle="""), "Profile form has no inline styles the CSP would block");
Check(profilePage.IsSuccessStatusCode && profileHtml.Contains("value=\"reece74\"") && Regex.IsMatch(profileHtml, "<option (selected=\"selected\" value=\"personal\"|value=\"personal\" selected=\"selected\")>"), "Profile page renders the saved profile");
Check((await browser.PostAsync("/Account/Profile", new FormUrlEncodedContent(new Dictionary<string, string> { ["SimBriefUsername"] = "forged" }))).StatusCode == HttpStatusCode.BadRequest, "Profile update without CSRF token is rejected");
var planPage = await browser.GetAsync("/Account/Plan");
var planHtml = await planPage.Content.ReadAsStringAsync();
Check(planPage.IsSuccessStatusCode && planHtml.Contains("value=\"Premium\" checked"), "Account level page shows the current level");
Check(!planHtml.Contains("Weather briefings") && !planHtml.Contains("Everything in Free"), "Account level page claims no weather briefings and names the included level");
Check((await browser.PostAsync("/Account/Plan", new FormUrlEncodedContent(new Dictionary<string, string> { ["plan"] = "Free" }))).StatusCode == HttpStatusCode.BadRequest, "Account level change without CSRF token is rejected");
var expiredForm = await browser.PostAsync("/Account/Plan", new FormUrlEncodedContent(new Dictionary<string, string> { ["plan"] = "Free" }));
Check((await expiredForm.Content.ReadAsStringAsync()).Contains("That form had expired") && (await expiredForm.Content.ReadAsStringAsync()).Contains("href=\"/Account\""), "A form without a valid token explains itself and links back to the account");
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
    var freshOverview = await browser.GetStringAsync($"/Account/Airline/{airline.Id}");
    Check(freshOverview.Contains("setup-title") && freshOverview.Contains("Add your first route") && freshOverview.Contains("Invite your first pilot") && freshOverview.Contains("0 of 4 done"),
        "A new airline's overview shows the four setup steps");
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
        var airlinePage = await browser.GetAsync($"/Account/Airline/{airline.Id}/Settings");
        var airlineHtml = await airlinePage.Content.ReadAsStringAsync();
        Check(airlinePage.IsSuccessStatusCode && airlineHtml.Contains("Airline level") && airlineHtml.Contains("value=\"Pro\" checked"), "Settings page offers the owner the level form with the current level");
        Check(airlineHtml.Contains("invitation.issued") && airlineHtml.Contains("airline.plan_changed") && !airlineHtml.Contains(invitation.Token), "Settings page shows the activity log without secrets");
        Check(!airlineHtml.Contains("dispatch board"), "Settings promises no shared dispatch board, which doesn't exist");
        Check((await browser.PostAsync($"/Account/Airline/{airline.Id}/Settings?handler=Plan", new FormUrlEncodedContent(new Dictionary<string, string> { ["plan"] = "Community" }))).StatusCode == HttpStatusCode.BadRequest, "Airline level change without CSRF token is rejected");
        var airlinePlanPost = await SubmitAsync(browser, airlinePage, airlineHtml, $"/Account/Airline/{airline.Id}/Settings?handler=Plan", new() { ["plan"] = "Community" });
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
        Check(!Regex.IsMatch(scheduleHtml, @"\sstyle=""") && scheduleHtml.Contains("placeholder=\"S4101\""), "Schedule form has no inline styles and uses S4 sample flight numbers");
        Check(schedulePage.IsSuccessStatusCode && scheduleHtml.Contains("A6101") && scheduleHtml.Contains("A6102") && scheduleHtml.Contains("Add a route") && scheduleHtml.Contains("Export CSV"), "Schedule console lists routes with the editor for operations roles");
        var routeForm = await SubmitAsync(browser, schedulePage, scheduleHtml, $"/Account/Airline/{airline.Id}/Schedule?handler=Save",
            new() { ["flightNumber"] = "A6103", ["origin"] = "KMKE", ["destination"] = "KMSP", ["departureUtc"] = "09:15", ["blockMinutes"] = "75", ["days"] = "0", ["aircraftType"] = "", ["notes"] = "", ["active"] = "true" });
        Check(routeForm.StatusCode == HttpStatusCode.Redirect && (await pilot.GetFromJsonAsync<RouteResponse[]>($"/api/v1/virtual-airlines/{airline.Id}/routes"))!.Any(r => r.FlightNumber == "A6103" && r.DaysOfWeek == 1), "Schedule form adds a route");
        var export = await browser.GetAsync($"/Account/Airline/{airline.Id}/Schedule?handler=Export");
        Check(export.Content.Headers.ContentType?.MediaType == "text/csv" && (await export.Content.ReadAsStringAsync()).Contains("A6103,KMKE,KMSP,09:15,75,1,,"), "Schedule exports as CSV");
        var fleetPage = await browser.GetAsync($"/Account/Airline/{airline.Id}/Fleet");
        var fleetHtml = await fleetPage.Content.ReadAsStringAsync();
        Check(!Regex.IsMatch(fleetHtml, @"\sstyle="""), "Fleet form has no inline styles the CSP would block");
        Check(fleetPage.IsSuccessStatusCode && fleetHtml.Contains("N123A6") && fleetHtml.Contains("Spirit of Milwaukee"), "Fleet console lists aircraft");
        var fleetForm = await SubmitAsync(browser, fleetPage, fleetHtml, $"/Account/Airline/{airline.Id}/Fleet?handler=Save",
            new() { ["aircraftId"] = tail!.Id.ToString(), ["registration"] = "N123A6", ["typeIcao"] = "B738", ["name"] = "Spirit of Milwaukee", ["homeBase"] = "KMKE", ["status"] = "maintenance", ["notes"] = "C check" });
        Check(fleetForm.StatusCode == HttpStatusCode.Redirect && (await pilot.GetFromJsonAsync<AircraftResponse[]>($"/api/v1/virtual-airlines/{airline.Id}/fleet"))!.Single().Status == "maintenance", "Fleet form updates an aircraft");
        Check((await browser.PostAsync($"/Account/Airline/{airline.Id}/Fleet?handler=Delete", new FormUrlEncodedContent(new Dictionary<string, string> { ["aircraftId"] = tail.Id.ToString() }))).StatusCode == HttpStatusCode.BadRequest, "Fleet removal without CSRF token is rejected");
        var logoUpload = await UploadAsync(browser, airlinePage, airlineHtml, $"/Account/Airline/{airline.Id}/Settings?handler=Logo", "logo", Png(400, 200, 0xE15BE0), "logo.png");
        var branded = await pilot.GetFromJsonAsync<AirlineWorkspace>($"/api/v1/virtual-airlines/{airline.Id}");
        Check(logoUpload.StatusCode == HttpStatusCode.Redirect && branded?.LogoUrl.StartsWith("/media/airline-logo/") == true && (await anonymous.GetAsync(branded.LogoUrl)).IsSuccessStatusCode, "Airline logo upload through the console and served publicly");
        Check((await browser.GetStringAsync($"/Account/Airline/{airline.Id}/Schedule")).Contains(branded!.LogoUrl), "Console header shows the logo");

        // Overview, crew and joining: any member reads the overview; the crew page is for administrators.
        var overviewHtml = await browser.GetStringAsync($"/Account/Airline/{airline.Id}");
        Check(overviewHtml.Contains("Routes · ") && overviewHtml.Contains($"/Account/Airline/{airline.Id}/Crew") && overviewHtml.Contains($"/Account/Airline/{airline.Id}/Settings"), "Overview shows counts and links crew and settings for the owner");
        using var outsiderBrowser = host.Browser("outsider-" + run);
        var memberOverview = await outsiderBrowser.GetAsync($"/Account/Airline/{airline.Id}");
        var memberHtml = await memberOverview.Content.ReadAsStringAsync();
        Check(memberOverview.IsSuccessStatusCode && memberHtml.Contains("You can read the") && !memberHtml.Contains(owner + "@example.test") && !memberHtml.Contains("/Crew") && !memberHtml.Contains("setup-title"),
            "A pilot reads the overview without crew emails, crew links or setup steps");
        Check((await outsiderBrowser.GetAsync($"/Account/Airline/{airline.Id}/Crew")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, "A pilot cannot open the crew page");
        Check((await anonymous.GetAsync($"/Account/Airline/{airline.Id}")).StatusCode != HttpStatusCode.OK, "The overview needs a signed-in member");
        var crewPage = await browser.GetAsync($"/Account/Airline/{airline.Id}/Crew");
        var crewHtml = await crewPage.Content.ReadAsStringAsync();
        Check(crewPage.IsSuccessStatusCode && crewHtml.Contains("outsider-" + run + "@example.test"), "Crew page lists the roster");
        var crewInvite = await SubmitAsync(browser, crewPage, crewHtml, $"/Account/Airline/{airline.Id}/Crew?handler=Invite", new() { ["email"] = $"joiner-{run}@example.test", ["roles"] = "Pilot" });
        var crewInviteHtml = await crewInvite.Content.ReadAsStringAsync();
        Check(crewInvite.IsSuccessStatusCode && crewInviteHtml.Contains($"Invitation created for joiner-{run}@example.test") && crewInviteHtml.Contains("Join an airline"), "Crew page issues an invitation and explains accepting it");
        using var joiner = host.Browser("joiner-" + run);
        Check((await joiner.GetStringAsync("/Account")).Contains("An airline invited you"), "Account page points to a waiting invitation");
        var joinPage = await joiner.GetAsync("/Account/Join");
        var joinHtml = await joinPage.Content.ReadAsStringAsync();
        var waitingId = Regex.Match(joinHtml, "name=\"invitationId\" value=\"([0-9a-f-]{36})\"").Groups[1].Value;
        Check(joinHtml.Contains(airline.Name) && waitingId.Length == 36, "Join page lists the invitation waiting for the verified email");
        var joined = await SubmitAsync(joiner, joinPage, joinHtml, "/Account/Join?handler=Accept", new() { ["invitationId"] = waitingId });
        Check(joined.StatusCode == HttpStatusCode.Redirect && joined.Headers.Location?.OriginalString.Contains($"/Account/Airline/{airline.Id}") == true, "Accepting a waiting invitation opens the airline");
        Check((await joiner.GetStringAsync("/Account/Join")).Contains("No invitations are waiting"), "An accepted invitation leaves the list");
        // The airline journey by role, through the browser pages: a pilot reads, a dispatcher edits operations, a non-member sees nothing.
        var pilotSchedule = await joiner.GetStringAsync($"/Account/Airline/{airline.Id}/Schedule");
        Check(pilotSchedule.Contains("A6101") && !pilotSchedule.Contains("handler=Save") && !pilotSchedule.Contains("handler=Import"), "A pilot reads the schedule with no edit or import forms");
        var tokenPage = await joiner.GetAsync("/Account/Join");
        var pilotRoutePost = await SubmitAsync(joiner, tokenPage, await tokenPage.Content.ReadAsStringAsync(), $"/Account/Airline/{airline.Id}/Schedule?handler=Save",
            new() { ["flightNumber"] = "S4999", ["origin"] = "KMKE", ["destination"] = "KORD", ["departureUtc"] = "09:00", ["blockMinutes"] = "60", ["days"] = "1" });
        Check(pilotRoutePost.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound
            && !(await pilot.GetStringAsync($"/api/v1/virtual-airlines/{airline.Id}/routes")).Contains("S4999"), "A pilot posting the schedule form directly is refused and nothing is added");
        Check((await joiner.GetAsync($"/Account/Airline/{airline.Id}/Settings")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, "A pilot cannot open settings");
        var dispatcherInvite = await pilot.PostAsJsonAsync($"/api/v1/virtual-airlines/{airline.Id}/invitations", new InviteMemberRequest($"dispatch-{run}@example.test", [AirlineRole.Dispatcher]));
        using (var dispatcherApi = host.Client("dispatch-" + run))
            Check((await dispatcherApi.PostAsJsonAsync("/api/v1/invitations/accept", new { token = (await dispatcherInvite.Content.ReadFromJsonAsync<IssuedInvitation>())!.Token })).IsSuccessStatusCode,
                "A dispatcher accepts an invitation");
        using var dispatcher = host.Browser("dispatch-" + run, mfa: false);
        var dispatchSchedule = await dispatcher.GetAsync($"/Account/Airline/{airline.Id}/Schedule");
        var dispatchScheduleHtml = await dispatchSchedule.Content.ReadAsStringAsync();
        Check(dispatchSchedule.IsSuccessStatusCode && dispatchScheduleHtml.Contains("Add a route"), "A dispatcher gets the schedule editor without MFA");
        var dispatchRoute = await SubmitAsync(dispatcher, dispatchSchedule, dispatchScheduleHtml, $"/Account/Airline/{airline.Id}/Schedule?handler=Save",
            new() { ["flightNumber"] = "S4777", ["origin"] = "KMKE", ["destination"] = "KMSP", ["departureUtc"] = "07:15", ["blockMinutes"] = "80", ["days"] = "2", ["active"] = "true" });
        Check(dispatchRoute.StatusCode == HttpStatusCode.Redirect && (await joiner.GetStringAsync($"/Account/Airline/{airline.Id}/Schedule")).Contains("S4777"),
            "A dispatcher adds a route that the pilots then see");
        Check((await dispatcher.GetStringAsync($"/Account/Airline/{airline.Id}/Fleet")).Contains("handler=Save"), "A dispatcher gets the fleet editor");
        Check((await dispatcher.GetAsync($"/Account/Airline/{airline.Id}/Crew")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound
            && (await dispatcher.GetAsync($"/Account/Airline/{airline.Id}/Settings")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, "A dispatcher cannot manage crew or settings");
        using var nonMember = host.Browser("nonmember-" + run);
        foreach (var page in new[] { "", "/Schedule", "/Fleet", "/Crew", "/Settings" })
        {
            var response = await nonMember.GetAsync($"/Account/Airline/{airline.Id}{page}");
            Check(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound && !(await response.Content.ReadAsStringAsync()).Contains(airline.Name),
                $"A non-member gets nothing from the airline page {(page.Length == 0 ? "/" : page)}");
        }
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
                ["guide"] = (anonymous, "/Guide"), ["delete"] = (browser, "/Account/Delete"),
                ["account"] = (browser, "/Account"), ["profile"] = (browser, "/Account/Profile"), ["plan"] = (browser, "/Account/Plan"),
                ["logbook"] = (browser, "/Account/Logbook"), ["create"] = (browser, "/Account/Create"), ["join"] = (browser, "/Account/Join"),
                ["airline"] = (browser, $"/Account/Airline/{airline.Id}"), ["crew"] = (browser, $"/Account/Airline/{airline.Id}/Crew"),
                ["settings"] = (browser, $"/Account/Airline/{airline.Id}/Settings"), ["schedule"] = (browser, $"/Account/Airline/{airline.Id}/Schedule"),
                ["fleet"] = (browser, $"/Account/Airline/{airline.Id}/Fleet"),
                ["pilot-airline"] = (joiner, $"/Account/Airline/{airline.Id}"), ["pilot-schedule"] = (joiner, $"/Account/Airline/{airline.Id}/Schedule"),
                ["pilot-fleet"] = (joiner, $"/Account/Airline/{airline.Id}/Fleet"), ["pilot-account"] = (joiner, "/Account")
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
        var chrome = await anonymous.GetStringAsync("/Levels");
        Check(!chrome.Contains("Weather briefings") && !chrome.Contains("Shared dispatch board") && !chrome.Contains("in testing") && !chrome.Contains("Create an account")
            && chrome.Contains("Create your invited account") && chrome.Contains("invite-only"),
            "Levels makes only the claims s4ops.com makes and reflects invite-only accounts");
        Check(chrome.Contains("Cloud logbook sync across PCs (coming soon)") && chrome.Contains("Extended flight history (coming soon)") && chrome.Contains("Priority support (coming soon)")
            && chrome.Contains("Operations reporting (coming soon)") && chrome.Contains("Receipts and card changes will live"), "Levels marks every unbuilt paid feature as coming soon");
        Check(chrome.Contains("s4-tile.svg") && !chrome.Contains("s4-lockup") && !chrome.Contains("topbar-plus") && chrome.Contains("content=\"dark\""),
            "Shared header uses the S4 tile, no lockup or stray plus, and declares a dark-only colour scheme");
        Check(Regex.IsMatch(chrome, @"<a class=""brand"" href=""/""><img [^>]*alt=""""[^>]*/><span>SIERRA 4<small>AIRLINE OPERATIONS</small></span></a>"),
            "Header brand link is named by its visible text (WCAG 2.5.3 label in name)");
        var css = await anonymous.GetStringAsync("/css/site.css");
        Check(!css.Contains("prefers-color-scheme: light") && css.Contains("--surface: #090c0f") && css.Contains("Bahnschrift SemiCondensed") && css.Contains("calc(0.3125vw + 12px)"),
            "Portal styles are charcoal-only, use the Sierra display face and the fluid base size");
        Check(css.Contains(".brand small { display: block; margin-top: 0.25rem; font: 600 0.6875rem") && css.Contains(".footer-row nav { display: flex; flex-wrap: wrap;")
            && Regex.Matches(css, @"(?m)^\.site-footer \{").Count == 1 && Regex.Matches(css, @"(?m)^\.site-nav \{").Count == 1
            && css.Contains(".entrance { min-height: calc(100vh - 12rem); display: grid; place-items: center; padding: var(--space-8) 0; }"),
            "Brand subtitle is readable, footer links wrap, header and footer rules are defined once, and the entrance uses the page gutter once");
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
        var leaverInvite = await pilot.PostAsJsonAsync($"/api/v1/virtual-airlines/{airline.Id}/invitations", new InviteMemberRequest($"leaver-{run}@example.test", [AirlineRole.Pilot]));
        using (var leaverApi = host.Client("leaver-" + run))
            await leaverApi.PostAsJsonAsync("/api/v1/invitations/accept", new { token = (await leaverInvite.Content.ReadFromJsonAsync<IssuedInvitation>())!.Token });
        Check((await leaver.GetAsync($"/Account/Airline/{airline.Id}/Schedule")).IsSuccessStatusCode, "A member reads the schedule before asking for deletion");
        var deletePage = await leaver.GetAsync("/Account/Delete");
        var deleteHtml = await deletePage.Content.ReadAsStringAsync();
        Check((await SubmitAsync(leaver, deletePage, deleteHtml, "/Account/Delete?handler=Request", new() { ["Confirm"] = "false" })).StatusCode == HttpStatusCode.BadRequest, "Deletion needs the confirmation box");
        Check((await SubmitAsync(leaver, deletePage, deleteHtml, "/Account/Delete?handler=Request", new() { ["Confirm"] = "true" })).StatusCode == HttpStatusCode.Redirect, "A fresh sign-in schedules deletion without MFA");
        var locked = await leaver.GetAsync("/Account");
        Check(locked.StatusCode == HttpStatusCode.Redirect && locked.Headers.Location?.OriginalString == "/Account/Delete", "A scheduled account is locked to the delete page");
        var lockedAirline = await leaver.GetAsync($"/Account/Airline/{airline.Id}/Schedule");
        Check(lockedAirline.StatusCode == HttpStatusCode.Redirect && lockedAirline.Headers.Location?.OriginalString == "/Account/Delete", "A member scheduled for deletion is locked out of the airline pages too");
        using (var leaverApi = host.Client("leaver-" + run))
            Check(!(await leaverApi.GetAsync($"/api/v1/virtual-airlines/{airline.Id}/routes")).IsSuccessStatusCode, "The desktop API refuses an account scheduled for deletion");
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
// With no admission setting the server is invite-only: a stranger gets no account, an invited pilot does.
var inviteSuffix = Guid.NewGuid().ToString("N")[..8];
using (var inviteOnly = new ServerFactory(connection, admission: null, invitedEmails: $"invited-{inviteSuffix}@example.test"))
{
    using var stranger = inviteOnly.Client("stranger-" + inviteSuffix);
    var refused = await stranger.GetAsync("/api/v1/me/bootstrap");
    var refusal = await refused.Content.ReadAsStringAsync();
    Check(refused.StatusCode == HttpStatusCode.Forbidden && refusal.Contains("invite_required") && refusal.Contains("invite-only"), "An uninvited first sign-in is refused with an explanation");
    using var invitedPilot = inviteOnly.Client("invited-" + inviteSuffix);
    Check((await invitedPilot.GetAsync("/api/v1/me/bootstrap")).StatusCode == HttpStatusCode.OK, "An invited, verified email creates an account");
    // Signed in but not invited: the account page offers a one-click request for the signed-in address.
    using var strangerBrowser = inviteOnly.Browser("stranger-" + inviteSuffix);
    var strangerPage = await strangerBrowser.GetAsync("/Account");
    var strangerHtml = await strangerPage.Content.ReadAsStringAsync();
    Check(strangerPage.StatusCode == HttpStatusCode.Forbidden && strangerHtml.Contains("Ask for an invitation") && strangerHtml.Contains($"stranger-{inviteSuffix}@example.test")
        && strangerHtml.Contains("/Request?handler=SignedIn"), "An uninvited pilot's account page offers to ask for an invitation for their address");
    var asked = await SubmitAsync(strangerBrowser, strangerPage, strangerHtml, "/Request?handler=SignedIn", new() { ["note"] = "Fenix A320, VATSIM" });
    Check(asked.StatusCode == HttpStatusCode.Redirect && asked.Headers.Location?.OriginalString.Contains("account=True", StringComparison.OrdinalIgnoreCase) == true,
        "Asking from the account page sends the request");
    using (var scope = inviteOnly.Services.CreateScope())
        Check(await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().InviteRequests.AnyAsync(x => x.Email == $"stranger-{inviteSuffix}@example.test" && x.Note == "Fenix A320, VATSIM"),
            "The request carries the signed-in address and the note");
    var waiting = await (await strangerBrowser.GetAsync("/Account")).Content.ReadAsStringAsync();
    Check(waiting.Contains("is waiting") && !waiting.Contains("/Request?handler=SignedIn"), "Signing in again while the request waits says so instead of offering another");
    var thanks = await strangerBrowser.GetStringAsync("/Request?sent=true&account=true");
    Check(thanks.Contains("your request is in") && thanks.Contains("href=\"/Account\""), "The confirmation sends the pilot back to their account");
}
// Problem reports: pilots send them from the portal; allowlisted staff with a fresh MFA read them.
var feedbackMail = new RecordingEmailSender();
var previewStore = new FakePreviewBuildStore();
using (var feedbackHost = new ServerFactory(connection, settings: new() { ["Staff:Emails"] = $"staff-{run}@example.test",
    ["Release:Version"] = "7.7.7", ["Release:DownloadUrl"] = "https://example.test/Sierra4-Setup-7.7.7.exe", ["Release:Sha256"] = new string('a', 64),
    ["Email:BaseUrl"] = "https://app.example.test", ["Legal:Contact"] = "support@s4ops.example" },
    extraServices: s => s.AddSingleton<IEmailSender>(feedbackMail).AddSingleton<IPreviewBuildStore>(previewStore)))
{
    using var reporterBrowser = feedbackHost.Browser($"reporter-{run}");
    using var staffBrowser = feedbackHost.Browser($"staff-{run}");
    using var staffNoMfa = feedbackHost.Browser($"staff-{run}", mfa: false);
    using var anon = feedbackHost.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
    // Pilot home: the getting-started checklist and the logbook insights.
    using var ownerHome = feedbackHost.Browser(owner);
    var homeHtml = await ownerHome.GetStringAsync("/Account");
    Check(homeHtml.Contains("href=\"/Guide\""), "The getting-started card links the tester guide");
    Check(homeHtml.Contains("Getting started") && homeHtml.Contains("href=\"/Account/Feedback\"") && homeHtml.Contains("class=\"step done\""),
    "The account page shows the getting-started checklist, with finished steps marked");
    // Airline invitations by email carry a link, never the invitation code.
    using (var ownerApi = feedbackHost.Client(owner))
    {
        var ownedAirline = (await ownerApi.GetFromJsonAsync<BootstrapResponse>("/api/v1/me/bootstrap"))!.Airlines.First(a => a.Roles.Contains(AirlineRole.Owner));
        var crewPage = await ownerHome.GetAsync($"/Account/Airline/{ownedAirline.Id}/Crew");
        var crewHtml = await crewPage.Content.ReadAsStringAsync();
        var mailedInvite = await SubmitAsync(ownerHome, crewPage, crewHtml, $"/Account/Airline/{ownedAirline.Id}/Crew?handler=Invite", new() { ["email"] = $"reporter-{run}@example.test", ["roles"] = "Pilot" });
        var invitedHtml = await mailedInvite.Content.ReadAsStringAsync();
        var code = Regex.Match(invitedHtml, "<code class=\"break\">([^<]+)</code>").Groups[1].Value;
        Check(code.Length > 20 && await WaitForAsync(() => feedbackMail.Sent.Any(m => m.To.SequenceEqual([$"reporter-{run}@example.test"]) && m.Subject == $"Join {ownedAirline.Name} on S4 OPS"
            && m.Text.Contains("https://app.example.test/Account/Join") && !m.Text.Contains(code) && !m.Html.Contains(code))),
            "An airline invitation emails a link to the join page without the invitation code");
    }
    using (var ownerApi = feedbackHost.Client(owner))
        await ownerApi.PostAsJsonAsync("/api/v1/me/flights/import", new LogbookImportRequest(
            $"Date;Flight;From;To;Aircraft;Block Time;Landing Rate\n{DateTime.UtcNow.AddDays(-3):yyyy-MM-dd} 18:20;S4701;KMKE;KORD;A20N;0:55;-180\n{DateTime.UtcNow.AddDays(-4):yyyy-MM-dd} 20:30;S4702;KORD;KMKE;A20N;1:05;-140\n"));
    var logbookHome = await ownerHome.GetStringAsync("/Account/Logbook");
    if (Environment.GetEnvironmentVariable("S4OPS_PORTAL_SNAPSHOT_DIR") is { Length: > 0 } homeSnapshots)
    {
        Directory.CreateDirectory(homeSnapshots);
        await File.WriteAllTextAsync(Path.Combine(homeSnapshots, "home-start.html"), homeHtml);
        await File.WriteAllTextAsync(Path.Combine(homeSnapshots, "logbook-insights.html"), logbookHome);
    }
    Check(logbookHome.Contains("Your flying") && logbookHome.Contains("Top routes") && Regex.Matches(logbookHome, "class=\"bar h\\d+\"").Count == 12
    && logbookHome.Contains("<table class=\"visually-hidden\"") && !Regex.IsMatch(logbookHome, @"\sstyle="""), "The logbook shows insights with a twelve-month chart and a table for screen readers");
    Check(Regex.IsMatch(logbookHome, @"stat-value"">\d+<small>h</small>") && !logbookHome.Contains(".ToString("), "The logbook totals block time in hours, not a raw expression");
    Check(!logbookHome.Contains("land here automatically"), "The logbook no longer claims desktop flights arrive automatically");
    // Anonymous requests are challenged; here the challenge can't reach the fake identity provider, so assert no form is served.
    var anonForm = await anon.GetAsync("/Account/Feedback");
    Check(!anonForm.IsSuccessStatusCode && !(await anonForm.Content.ReadAsStringAsync()).Contains("Send report"), "The report form needs sign-in");
    var anonStaff = await anon.GetAsync("/Staff/Feedback");
    Check(!anonStaff.IsSuccessStatusCode && !(await anonStaff.Content.ReadAsStringAsync()).Contains("Problem reports"), "The staff inbox needs sign-in");
    var formPage = await reporterBrowser.GetAsync("/Account/Feedback");
    var formHtml = await formPage.Content.ReadAsStringAsync();
    Check(formPage.IsSuccessStatusCode && formHtml.Contains("enctype=\"multipart/form-data\"") && !Regex.IsMatch(formHtml, @"\sstyle=""") && !formHtml.Contains("<script"), "The report form renders without scripts or inline styles");
    Check((await reporterBrowser.PostAsync("/Account/Feedback", new FormUrlEncodedContent(new Dictionary<string, string> { ["Message"] = "forged report text" }))).StatusCode == HttpStatusCode.BadRequest, "Report without CSRF token is rejected");
    var fields = new Dictionary<string, string> { ["Category"] = "Bug", ["Message"] = "The map froze after takeoff from KMKE.", ["FlightNumber"] = "s4 601", ["FlownOn"] = "" };
    var sentWithFile = await PostMultipartAsync(reporterBrowser, formPage, formHtml, "/Account/Feedback", fields, ("Attachment", Png(4, 4), "frozen map.png"));
    Check(sentWithFile.StatusCode == HttpStatusCode.Redirect, "A report with a screenshot is accepted");
    Check(await WaitForAsync(() => feedbackMail.Sent.Any(m => m.Subject.StartsWith("New problem report") && m.To.SequenceEqual([$"staff-{run}@example.test"])
        && m.Text.Contains("https://app.example.test/Staff/Feedback/") && m.ReplyTo == "support@s4ops.example")), "Staff are emailed about a new report, with a link and the contact as reply-to");
    var sentEmptyFile = await PostMultipartAsync(reporterBrowser, formPage, formHtml, "/Account/Feedback", fields, ("Attachment", [], ""));
    Check(sentEmptyFile.StatusCode == HttpStatusCode.Redirect, "An empty file input sends the report without an attachment");
    var lying = await PostMultipartAsync(reporterBrowser, formPage, formHtml, "/Account/Feedback", fields, ("Attachment", "<html>hi</html>"u8.ToArray(), "evil.png"));
    Check(lying.StatusCode == HttpStatusCode.BadRequest && (await lying.Content.ReadAsStringAsync()).Contains("PNG or JPEG"), "A file that isn't what its name says is refused with a readable message");
    var emptyLogbook = await reporterBrowser.GetStringAsync("/Account/Logbook");
    Check(emptyLogbook.Contains("Nothing logged yet.") && !emptyLogbook.Contains("Your flying"), "An empty logbook explains itself instead of showing empty charts");
    var mine = await reporterBrowser.GetStringAsync("/Account/Feedback");
    Check(mine.Contains("The map froze after takeoff") && mine.Contains("S4601"), "Pilots see their own reports");
    Check((await reporterBrowser.GetAsync("/Staff/Feedback")).StatusCode == HttpStatusCode.NotFound, "Staff inbox is 404 for pilots");
    using (var noStaffHost = new ServerFactory(connection))
    using (var noStaffBrowser = noStaffHost.Browser($"staff-{run}"))
        Check((await noStaffBrowser.GetAsync("/Staff/Feedback")).StatusCode == HttpStatusCode.NotFound, "Staff inbox is 404 when no staff are configured");
    var noMfaInbox = await staffNoMfa.GetAsync("/Staff/Feedback");
    Check(noMfaInbox.StatusCode == HttpStatusCode.Forbidden && (await noMfaInbox.Content.ReadAsStringAsync()).Contains("/auth/step-up"), "Staff without a fresh MFA are sent to a security check");
    var inboxHtml = await staffBrowser.GetStringAsync("/Staff/Feedback");
    var reportId = Regex.Match(inboxHtml, @"<li><a href=""/Staff/Feedback/([0-9a-f-]{36})"">(?:(?!</li>).)*badge"">file<", RegexOptions.Singleline).Groups[1].Value;
    Check(inboxHtml.Contains($"reporter-{run}") && reportId.Length == 36, "Staff see the new reports");
    var reportPage = await staffBrowser.GetAsync($"/Staff/Feedback/{reportId}");
    var reportHtml = await reportPage.Content.ReadAsStringAsync();
    var download = await staffBrowser.GetAsync($"/Staff/Feedback/{reportId}?handler=Attachment");
    Check(download.Content.Headers.ContentDisposition?.DispositionType == "attachment" && download.Headers.TryGetValues("X-Content-Type-Options", out var sniff) && sniff.Contains("nosniff"), "Attachments download, never render inline");
    if (Environment.GetEnvironmentVariable("S4OPS_PORTAL_SNAPSHOT_DIR") is { Length: > 0 } feedbackSnapshots)
    {
        Directory.CreateDirectory(feedbackSnapshots);
        await File.WriteAllTextAsync(Path.Combine(feedbackSnapshots, "feedback.html"), await reporterBrowser.GetStringAsync("/Account/Feedback?sent=true"));
        await File.WriteAllTextAsync(Path.Combine(feedbackSnapshots, "staff-inbox.html"), inboxHtml);
        await File.WriteAllTextAsync(Path.Combine(feedbackSnapshots, "staff-report.html"), reportHtml);
    }
    // Replies: staff answer from the report page; the pilot sees it signed by the team, with a notice until read.
    Check(reportHtml.Contains("Reply to the pilot"), "Staff can reply from the report page");
    var replyPost = await SubmitAsync(staffBrowser, reportPage, reportHtml, $"/Staff/Feedback/{reportId}?handler=Reply", new() { ["body"] = "Thanks, we can see the freeze in your log.", ["markHandled"] = "false" });
    Check(replyPost.StatusCode == HttpStatusCode.Redirect && (await staffBrowser.GetStringAsync($"/Staff/Feedback/{reportId}")).Contains("Thanks, we can see the freeze"), "Staff post a reply");
    Check((await reporterBrowser.GetStringAsync("/Account")).Contains("reply to your problem report"), "The pilot's account page says a reply is waiting");
    Check(await WaitForAsync(() => feedbackMail.Sent.Any(m => m.To.SequenceEqual([$"reporter-{run}@example.test"]) && m.Subject.Contains("replied")
        && m.Text.Contains("https://app.example.test/Account/Feedback") && !m.Text.Contains("freeze") && !m.Html.Contains("freeze"))), "The pilot is emailed a link to the reply, not the reply itself");
    var withReply = await reporterBrowser.GetStringAsync("/Account/Feedback");
    Check(withReply.Contains("Thanks, we can see the freeze in your log.") && withReply.Contains("Sierra 4 team") && !withReply.Contains($"staff-{run}"), "The pilot reads the reply, signed by the team");
    Check(!(await reporterBrowser.GetStringAsync("/Account")).Contains("reply to your problem report"), "Reading the reports clears the notice");
    reportPage = await staffBrowser.GetAsync($"/Staff/Feedback/{reportId}");
    reportHtml = await reportPage.Content.ReadAsStringAsync();
    var markHandled = await SubmitAsync(staffBrowser, reportPage, reportHtml, $"/Staff/Feedback/{reportId}?handler=Status", new() { ["status"] = "Handled" });
    Check(markHandled.StatusCode == HttpStatusCode.Redirect && !(await staffBrowser.GetStringAsync("/Staff/Feedback")).Contains(reportId), "Handled reports leave the New list");
    // Testers: staff invite and remove testers without a hosting configuration change.
    Check((await reporterBrowser.GetAsync("/Staff/Testers")).StatusCode == HttpStatusCode.NotFound, "Tester invitations are 404 for pilots");
    Check(inboxHtml.Contains("href=\"/Staff/Testers\""), "Staff pages link to each other");
    Check((await staffBrowser.GetStringAsync("/Account")).Contains("href=\"/Staff\"") && !(await reporterBrowser.GetStringAsync("/Account")).Contains("/Staff"),
        "The account page shows staff tools to staff only");
    Check((await reporterBrowser.GetAsync("/Staff")).StatusCode == HttpStatusCode.NotFound, "The staff overview is 404 for pilots");
    var overviewHtml = await staffBrowser.GetStringAsync("/Staff");
    Check(overviewHtml.Contains("Tester funnel") && overviewHtml.Contains("Desktop versions") && overviewHtml.Contains("href=\"/Staff/Feedback\"")
        && !Regex.IsMatch(overviewHtml, @"\sstyle=""") && !overviewHtml.Contains("<script"), "Staff see the overview with the funnel, versions and links");
    var testersPage = await staffBrowser.GetAsync("/Staff/Testers");
    var testersHtml = await testersPage.Content.ReadAsStringAsync();
    Check(testersPage.IsSuccessStatusCode && testersHtml.Contains("Invite a tester") && !Regex.IsMatch(testersHtml, @"\sstyle=""") && !testersHtml.Contains("<script"), "Staff open the tester invitations page");
    var newTester = $"New-Tester-{run}@Example.test";
    var invited = await SubmitAsync(staffBrowser, testersPage, testersHtml, "/Staff/Testers?handler=Add", new() { ["email"] = newTester, ["note"] = "Met at the fly-in" });
    var afterInvite = await staffBrowser.GetStringAsync("/Staff/Testers");
    Check(invited.StatusCode == HttpStatusCode.Redirect && afterInvite.Contains(newTester.ToLowerInvariant()) && afterInvite.Contains("Met at the fly-in"), "Staff invite a tester");
    Check(afterInvite.Contains("Message to send") && afterInvite.Contains("https://localhost/Guide") && afterInvite.Contains("https://localhost/auth/signup"), "Staff get a ready-to-send message with the sign-up and guide links");
    Check(await WaitForAsync(() => feedbackMail.Sent.Any(m => m.To.SequenceEqual([newTester.ToLowerInvariant()]) && m.Subject.Contains("invited")
        && m.Text.Contains("https://app.example.test/auth/signup") && m.Text.Contains("https://app.example.test/Guide"))), "An invited tester is emailed the sign-up and guide links");
    var invitesBefore = feedbackMail.Sent.Count(m => m.To.Contains(newTester.ToLowerInvariant()));
    await SubmitAsync(staffBrowser, testersPage, testersHtml, "/Staff/Testers?handler=Add", new() { ["email"] = newTester, ["note"] = "Note edited" });
    await Task.Delay(300);
    Check(feedbackMail.Sent.Count(m => m.To.Contains(newTester.ToLowerInvariant())) == invitesBefore, "Editing an invitation's note doesn't email the tester again");
    var badInvite = await SubmitAsync(staffBrowser, testersPage, testersHtml, "/Staff/Testers?handler=Add", new() { ["email"] = "not-an-email", ["note"] = "" });
    Check(badInvite.StatusCode == HttpStatusCode.BadRequest && (await badInvite.Content.ReadAsStringAsync()).Contains("valid email"), "A bad email is refused with a readable message");
    Check(testersHtml.Contains("covers staff actions until"), "The testers page says when the security check runs out");
    // A stale security check: nothing is sent, and the check returns to the form with the invitation filled in.
    var staleTester = $"stale-{run}@example.test";
    var stalePage = await staffNoMfa.GetAsync("/Staff/Testers");
    var staleHtml = await stalePage.Content.ReadAsStringAsync();
    Check(stalePage.StatusCode == HttpStatusCode.Forbidden && !staleHtml.Contains("Invite a tester") && staleHtml.Contains("/auth/step-up?returnUrl=%2FStaff%2FTesters"),
        "Without a fresh check the testers page shows only the security check, which returns to the page");
    var staleInvite = await SubmitAsync(staffNoMfa, stalePage, staleHtml, "/Staff/Testers?handler=Add", new() { ["email"] = staleTester, ["note"] = "From the fly-in" });
    var staleInviteHtml = await staleInvite.Content.ReadAsStringAsync();
    Check(staleInvite.StatusCode == HttpStatusCode.Forbidden && staleInviteHtml.Contains("been sent. Staff actions need a security check")
        && staleInviteHtml.Contains("returnUrl=%2FStaff%2FTesters%3Femail%3Dstale-"), "An invitation refused for a stale check says so and keeps it for after the check");
    await Task.Delay(300);
    Check(!feedbackMail.Sent.Any(m => m.To.Contains(staleTester)), "Nothing is emailed before the security check");
    var prefilled = await staffBrowser.GetStringAsync($"/Staff/Testers?email={Uri.EscapeDataString(staleTester)}&note=From%20the%20fly-in");
    Check(prefilled.Contains($"value=\"{staleTester}\"") && prefilled.Contains("value=\"From the fly-in\""), "After the check the invitation form comes back filled in");
    // Longer text survives a stale check: it stays on the page and the check opens in a new tab.
    var staleReply = await SubmitAsync(staffNoMfa, stalePage, staleHtml, $"/Staff/Feedback/{reportId}?handler=Reply", new() { ["body"] = "Thanks, fixed in the next build.", ["markHandled"] = "true" });
    var staleReplyHtml = await staleReply.Content.ReadAsStringAsync();
    Check(staleReply.StatusCode == HttpStatusCode.Forbidden && staleReplyHtml.Contains("Thanks, fixed in the next build.</textarea>") && staleReplyHtml.Contains("target=\"_blank\""),
        "A reply refused for a stale check stays on the page, with the check in a new tab");
    var staleNotes = await SubmitAsync(staffNoMfa, stalePage, staleHtml, "/Staff/Releases?handler=Save", new() { ["Version"] = "9.9.9", ["Title"] = "Kept", ["Notes"] = "- A long change list" });
    var staleNotesHtml = await staleNotes.Content.ReadAsStringAsync();
    Check(staleNotes.StatusCode == HttpStatusCode.Forbidden && staleNotesHtml.Contains("- A long change list</textarea>") && staleNotesHtml.Contains("value=\"Kept\"")
        && !(await anon.GetStringAsync("/Releases")).Contains("9.9.9"), "Release notes refused for a stale check are kept on the page and not published");
    if (Environment.GetEnvironmentVariable("S4OPS_PORTAL_SNAPSHOT_DIR") is { Length: > 0 } testerSnapshots)
    {
        await File.WriteAllTextAsync(Path.Combine(testerSnapshots, "staff-testers.html"), afterInvite);
        await File.WriteAllTextAsync(Path.Combine(testerSnapshots, "account-staff.html"), await staffBrowser.GetStringAsync("/Account"));
        await File.WriteAllTextAsync(Path.Combine(testerSnapshots, "privacy.html"), await anon.GetStringAsync("/Privacy"));
        await File.WriteAllTextAsync(Path.Combine(testerSnapshots, "terms.html"), await anon.GetStringAsync("/Terms"));
    }
    // Release notes: staff write them, everyone reads them on Releases and Download, and pilots on an older desktop are told.
    Check((await reporterBrowser.GetAsync("/Staff/Releases")).StatusCode == HttpStatusCode.NotFound, "Release notes editing is 404 for pilots");
    var releasesPage = await staffBrowser.GetAsync("/Staff/Releases");
    var releasesHtml = await releasesPage.Content.ReadAsStringAsync();
    Check(releasesPage.IsSuccessStatusCode && releasesHtml.Contains("Write release notes") && !Regex.IsMatch(releasesHtml, @"\sstyle="""), "Staff open the release notes editor");
    var savedNotes = await SubmitAsync(staffBrowser, releasesPage, releasesHtml, "/Staff/Releases?handler=Save",
        new() { ["Version"] = "7.7.7", ["Title"] = "Block-in you can trust", ["Notes"] = "- Engine shutdown confirms block in\n- <script>alert(1)</script> is shown as text" });
    Check(savedNotes.StatusCode == HttpStatusCode.Redirect, "Staff publish release notes");
    var history = await anon.GetStringAsync("/Releases");
    Check(history.Contains("Block-in you can trust") && history.Contains("&lt;script&gt;") && !history.Contains("<script>alert"), "Releases lists the notes as plain text");
    var downloadNotes = await anon.GetStringAsync("/Download");
    Check(downloadNotes.Contains("What's new in 7.7.7") && downloadNotes.Contains("Engine shutdown confirms block in") && downloadNotes.Contains("href=\"/Releases\""), "Download shows the current release's notes");
    if (Environment.GetEnvironmentVariable("S4OPS_PORTAL_SNAPSHOT_DIR") is { Length: > 0 } releaseSnapshots)
    {
        await File.WriteAllTextAsync(Path.Combine(releaseSnapshots, "releases.html"), history);
        await File.WriteAllTextAsync(Path.Combine(releaseSnapshots, "download-notes.html"), downloadNotes);
        await File.WriteAllTextAsync(Path.Combine(releaseSnapshots, "staff-releases.html"), await staffBrowser.GetStringAsync("/Staff/Releases"));
    }
    // Preview builds: staff with a fresh check download private builds; everyone else gets 404, and only listed keys are signed.
    var buildKey = Uri.EscapeDataString(previewStore.Builds[0].Key);
    Check((await reporterBrowser.GetAsync("/Staff/Builds")).StatusCode == HttpStatusCode.NotFound
        && (await reporterBrowser.GetAsync($"/Staff/Builds?handler=Download&key={buildKey}")).StatusCode == HttpStatusCode.NotFound, "Preview builds are 404 for pilots");
    Check((await anon.GetAsync($"/Staff/Builds?handler=Download&key={buildKey}")).Headers.Location?.OriginalString.Contains("builds.example.test") != true, "Signed-out visitors get no build link");
    var staleBuilds = await staffNoMfa.GetAsync($"/Staff/Builds?handler=Download&key={buildKey}");
    Check(staleBuilds.StatusCode == HttpStatusCode.Forbidden && (await staleBuilds.Content.ReadAsStringAsync()).Contains("returnUrl=%2FStaff%2FBuilds"),
        "A stale security check gets the check, which returns to the builds page, not a link");
    var buildsPage = await staffBrowser.GetAsync("/Staff/Builds");
    var buildsHtml = await buildsPage.Content.ReadAsStringAsync();
    Check(buildsPage.IsSuccessStatusCode && buildsHtml.Contains("Sierra4-Setup-0.23.0-abc1234.exe") && buildsHtml.Contains("95.0 MB") && buildsHtml.Contains("href=\"/Staff/Builds\"")
        && !Regex.IsMatch(buildsHtml, @"\sstyle=""") && !buildsHtml.Contains("<script"), "Staff see the private builds with their size");
    var buildDownload = await staffBrowser.GetAsync($"/Staff/Builds?handler=Download&key={buildKey}");
    Check(buildDownload.StatusCode == HttpStatusCode.Redirect && buildDownload.Headers.Location?.AbsoluteUri == "https://builds.example.test/preview/Sierra4-Setup-0.23.0-abc1234.exe?signed=1"
        && buildDownload.Headers.CacheControl?.NoStore == true, "Staff download a build through a signed link that isn't cached");
    Check((await staffBrowser.GetAsync("/Staff/Builds?handler=Download&key=other%2Fsecret.txt")).StatusCode == HttpStatusCode.NotFound, "A key the store didn't list is never signed");
    previewStore.Broken = true;
    var brokenBuilds = await staffBrowser.GetAsync("/Staff/Builds");
    Check(brokenBuilds.StatusCode == HttpStatusCode.BadGateway && (await brokenBuilds.Content.ReadAsStringAsync()).Contains("Builds unavailable"), "An unreachable build store is explained");
    previewStore.Broken = false;
    previewStore.Enabled = false;
    Check((await staffBrowser.GetStringAsync("/Staff/Builds")).Contains("PreviewBuilds__SecretAccessKey")
        && (await staffBrowser.GetAsync($"/Staff/Builds?handler=Download&key={buildKey}")).StatusCode == HttpStatusCode.NotFound, "Without settings the page says what to set and offers no download");
    previewStore.Enabled = true;
    using (var oldDesktop = feedbackHost.Client($"reporter-{run}"))
    {
        oldDesktop.DefaultRequestHeaders.UserAgent.ParseAdd("Alpha6OPS/0.1.0");
        await oldDesktop.GetAsync("/api/v1/me/bootstrap");
    }
    Check((await reporterBrowser.GetStringAsync("/Account")).Contains("Update available"), "A pilot on an older desktop is told an update is available");
    Check(!(await staffBrowser.GetStringAsync("/Account")).Contains("Update available"), "Pilots who haven't used the desktop aren't nagged");
    var testersPageAgain = await staffBrowser.GetAsync("/Staff/Testers");
    var removed = await SubmitAsync(staffBrowser, testersPageAgain, await testersPageAgain.Content.ReadAsStringAsync(), "/Staff/Testers?handler=Remove", new() { ["email"] = newTester.ToLowerInvariant() });
    Check(removed.StatusCode == HttpStatusCode.Redirect && !(await staffBrowser.GetStringAsync("/Staff/Testers")).Contains(newTester.ToLowerInvariant()), "Staff remove a tester invitation");
}
// Emails go out from a background service, so wait briefly for them.
static async Task<bool> WaitForAsync(Func<bool> condition)
{
    for (var i = 0; i < 50; i++) { if (condition()) return true; await Task.Delay(100); }
    return condition();
}
static async Task<HttpResponseMessage> PostMultipartAsync(HttpClient client, HttpResponseMessage page, string html, string path, Dictionary<string, string> fields, (string Field, byte[] Bytes, string FileName)? file)
{
    var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    var antiforgery = page.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal)) : null;
    var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
    foreach (var (key, value) in fields) content.Add(new StringContent(value), key);
    if (file is { } f)
    {
        var part = new ByteArrayContent(f.Bytes); part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        // Set by hand: MultipartFormDataContent refuses the empty filename a browser sends for an empty file input.
        part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = $"\"{f.Field}\"", FileName = $"\"{f.FileName}\"" };
        content.Add(part);
    }
    using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
    request.Headers.Add("Cookie", string.Join("; ", client.DefaultRequestHeaders.GetValues("Cookie").Append(antiforgery ?? "")));
    return await client.SendAsync(request);
}
// Invitation requests: a public form with a honeypot and a per-address limit; staff invite or decline from Testers.
var requestMail = new RecordingEmailSender();
using (var requestHost = new ServerFactory(connection, settings: new() { ["Staff:Emails"] = $"staff-{run}@example.test" },
    extraServices: s => s.AddSingleton<IEmailSender>(requestMail)))
{
    using var visitor = requestHost.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
    visitor.DefaultRequestHeaders.Add("Cookie", "s4-test=1");
    using var requestStaff = requestHost.Browser($"staff-{run}");
    var form = await visitor.GetAsync("/Request");
    var formHtml = await form.Content.ReadAsStringAsync();
    Check(form.IsSuccessStatusCode && formHtml.Contains("Ask for an invitation") && !formHtml.Contains("<script") && !Regex.IsMatch(formHtml, @"\sstyle=""")
        && !formHtml.Contains("cf-turnstile") && form.Headers.GetValues("Content-Security-Policy").Single().Contains("script-src 'none'"), "The request form renders script-free without Turnstile keys");
    var requester = $"asker-{run}@example.test";
    var asked = await SubmitAsync(visitor, form, formHtml, "/Request", new() { ["Email"] = requester, ["Name"] = "Sam", ["Note"] = "Fenix A320", ["website"] = "" });
    Check(asked.StatusCode == HttpStatusCode.Redirect && asked.Headers.Location!.OriginalString.Contains("sent=true", StringComparison.OrdinalIgnoreCase), "A visitor asks for an invitation");
    Check(await WaitForAsync(() => requestMail.Sent.Any(m => m.Subject == "New invitation request" && m.Text.Contains(requester) && m.Text.Contains("https://app.s4ops.com/Staff/Testers"))),
        "Staff are emailed about a new request (links default to app.s4ops.com)");
    var bot = $"bot-{run}@example.test";
    var botPost = await SubmitAsync(visitor, form, formHtml, "/Request", new() { ["Email"] = bot, ["Name"] = "", ["Note"] = "", ["website"] = "https://spam.example" });
    Check(botPost.StatusCode == HttpStatusCode.Redirect, "A filled honeypot looks like success to the bot");
    var badEmail = await SubmitAsync(visitor, form, formHtml, "/Request", new() { ["Email"] = "nope", ["Name"] = "", ["Note"] = "", ["website"] = "" });
    Check(badEmail.StatusCode == HttpStatusCode.BadRequest && (await badEmail.Content.ReadAsStringAsync()).Contains("valid email"), "A bad email is explained");
    var limited = await SubmitAsync(visitor, form, formHtml, "/Request", new() { ["Email"] = $"fourth-{run}@example.test", ["Name"] = "", ["Note"] = "", ["website"] = "" });
    Check(limited.StatusCode == HttpStatusCode.TooManyRequests, "A fourth request from the same address within the hour is refused");
    var testersWithRequests = await requestStaff.GetAsync("/Staff/Testers");
    var testersWithRequestsHtml = await testersWithRequests.Content.ReadAsStringAsync();
    Check(testersWithRequestsHtml.Contains(requester) && testersWithRequestsHtml.Contains("Fenix A320") && !testersWithRequestsHtml.Contains(bot), "Staff see real requests, not the bot's");
    if (Environment.GetEnvironmentVariable("S4OPS_PORTAL_SNAPSHOT_DIR") is { Length: > 0 } requestSnapshots)
    {
        Directory.CreateDirectory(requestSnapshots);
        await File.WriteAllTextAsync(Path.Combine(requestSnapshots, "request.html"), formHtml);
        await File.WriteAllTextAsync(Path.Combine(requestSnapshots, "staff-testers-requests.html"), testersWithRequestsHtml);
        await File.WriteAllTextAsync(Path.Combine(requestSnapshots, "staff-overview.html"), await requestStaff.GetStringAsync("/Staff"));
    }
    // The shared test database keeps earlier runs' requests, so pick this run's request by its email.
    var requestId = Regex.Match(testersWithRequestsHtml, Regex.Escape(requester) + @"</strong>(?:(?!</li>).)*?name=""id"" value=""([0-9a-f-]{36})""", RegexOptions.Singleline).Groups[1].Value;
    Check(requestId.Length == 36, "This run's request is listed with its own Invite button");
    var approvedRequest = await SubmitAsync(requestStaff, testersWithRequests, testersWithRequestsHtml, "/Staff/Testers?handler=Approve", new() { ["id"] = requestId });
    var afterApprove = await requestStaff.GetStringAsync("/Staff/Testers");
    Check(approvedRequest.StatusCode == HttpStatusCode.Redirect && afterApprove.Contains(requester) && afterApprove.Contains("Requested: Sam") && !afterApprove.Contains($"value=\"{requestId}\""), "Inviting a request moves it to the invited list");
    Check(await WaitForAsync(() => requestMail.Sent.Any(m => m.To.SequenceEqual([requester]) && m.Subject.Contains("invited"))), "Inviting a request emails the requester");
    Check(!requestMail.Sent.Any(m => m.To.Contains(bot)), "Nobody is emailed about the bot's request");
    Check((await requestStaff.GetStringAsync("/Staff")).Contains("pending request"), "The overview shows pending requests");
}
using (var turnstileHost = new ServerFactory(connection, settings: new() { ["Turnstile:SiteKey"] = "site-key-test", ["Turnstile:SecretKey"] = "secret-test" },
    extraServices: s => s.AddSingleton<ITurnstileVerifier>(new FakeTurnstile())))
{
    using var visitor = turnstileHost.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
    visitor.DefaultRequestHeaders.Add("Cookie", "s4-test=1");
    var form = await visitor.GetAsync("/Request");
    var formHtml = await form.Content.ReadAsStringAsync();
    var turnstileCsp = form.Headers.GetValues("Content-Security-Policy").Single();
    Check(formHtml.Contains("class=\"cf-turnstile\" data-sitekey=\"site-key-test\"") && turnstileCsp.Contains("script-src https://challenges.cloudflare.com") && turnstileCsp.Contains("frame-src https://challenges.cloudflare.com"),
        "With Turnstile keys the form shows the widget and the page allows only Cloudflare's script");
    var noToken = await SubmitAsync(visitor, form, formHtml, "/Request", new() { ["Email"] = $"turnstile-{run}@example.test", ["Name"] = "", ["Note"] = "", ["website"] = "" });
    Check(noToken.StatusCode == HttpStatusCode.BadRequest && (await noToken.Content.ReadAsStringAsync()).Contains("security check"), "Without a Turnstile pass the request is refused");
    var withToken = await SubmitAsync(visitor, form, formHtml, "/Request", new() { ["Email"] = $"turnstile-{run}@example.test", ["Name"] = "", ["Note"] = "", ["website"] = "", ["cf-turnstile-response"] = "good" });
    Check(withToken.StatusCode == HttpStatusCode.Redirect, "With a Turnstile pass the request is accepted");
    Check((await visitor.GetAsync("/Download")).Headers.GetValues("Content-Security-Policy").Single().Contains("script-src 'none'"), "Other pages keep the script-free policy");
}
// Resend: the HTTP request the sender makes, captured without the network.
{
    var handler = new CapturingHandler();
    var sender = new ResendEmailSender(new HttpClient(handler), Options.Create(new EmailSettings { ResendApiKey = "re_test_key" }),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ResendEmailSender>.Instance);
    await sender.SendAsync(new EmailMessage(["pilot@example.test"], "Subject <b>", "Plain text", "<p>Html</p>", "S4 OPS <noreply@s4ops.com>", "support@s4ops.example"), CancellationToken.None);
    var body = handler.Body ?? "";
    Check(handler.Request?.RequestUri?.ToString() == "https://api.resend.com/emails" && handler.Request.Method == HttpMethod.Post
        && handler.Request.Headers.Authorization?.ToString() == "Bearer re_test_key", "Resend is called with the API key");
    Check(body.Contains("\"from\":\"S4 OPS \\u003Cnoreply@s4ops.com\\u003E\"") && body.Contains("\"to\":[\"pilot@example.test\"]") && body.Contains("\"reply_to\":\"support@s4ops.example\"")
        && body.Contains("\"text\":\"Plain text\""), "The Resend request carries from, to, reply-to and both bodies");
    Check(!new EmailSettings().Enabled && new EmailSettings { ResendApiKey = "re_x" }.Enabled, "Email is off until a Resend key is set");
}
Console.WriteLine($"Server integration tests complete: {failures} failures.");
return failures == 0 ? 0 : 1;

sealed class RecordingEmailSender : IEmailSender
{
    public System.Collections.Concurrent.ConcurrentQueue<EmailMessage> Sent { get; } = new();
    public Task SendAsync(EmailMessage message, CancellationToken ct) { Sent.Enqueue(message); return Task.CompletedTask; }
}

sealed class CapturingHandler(string response = "{\"id\":\"test\"}", HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }
    public string? Body { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Request = request;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return new HttpResponseMessage(status) { Content = new StringContent(response) };
    }
}

sealed class FakePreviewBuildStore : IPreviewBuildStore
{
    public bool Enabled { get; set; } = true;
    public bool Broken { get; set; }
    public PreviewBuild[] Builds { get; } = [new("preview/Sierra4-Setup-0.23.0-abc1234.exe", "Sierra4-Setup-0.23.0-abc1234.exe", 99614720, new(2026, 10, 18, 9, 30, 0, TimeSpan.Zero))];
    public Task<PreviewBuild[]> ListAsync(CancellationToken ct) => Broken ? throw new PreviewBuildStoreException("The build store didn't answer. Try again in a minute.") : Task.FromResult(Builds);
    public Uri DownloadUrl(PreviewBuild build) => new("https://builds.example.test/" + build.Key + "?signed=1");
}

sealed class FakeTurnstile : ITurnstileVerifier
{
    public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct) => Task.FromResult(token == "good");
}

sealed class ServerFactory(string? connection, string environment = "Development", string? certificateBase64 = null, string? admission = "Open", string? invitedEmails = null, Dictionary<string, string>? settings = null, Action<IServiceCollection>? extraServices = null) : WebApplicationFactory<ServerSettings>
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
        builder.UseSetting("Accounts:Admission", admission ?? "");
        builder.UseSetting("Accounts:InvitedEmails", invitedEmails ?? "");
        builder.UseSetting("DataProtection:KeyDirectory", "");
        builder.UseSetting("DataProtection:CertificatePath", "");
        builder.UseSetting("DataProtection:CertificateBase64", certificateBase64 ?? "");
        builder.UseSetting("DataProtection:CertificatePassword", certificateBase64 is null ? "" : "test-pfx");
        builder.UseSetting("Hosting:BehindProxy", certificateBase64 is null ? "" : "true");
        if (certificateBase64 is not null) builder.UseSetting("Hosting:PublicHosts", "alpha6ops.example.test");
        builder.UseSetting("Hosting:TrustCloudflare", certificateBase64 is null ? "" : "true");
        foreach (var (key, value) in settings ?? []) builder.UseSetting(key, value);
        builder.ConfigureServices(services =>
        {
            if (certificateBase64 is null) services.AddDataProtection().UseEphemeralDataProtectionProvider();
            extraServices?.Invoke(services);
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
    // A signed-in website session for `subject`, as the cookie the portal issues after login.
    public HttpClient Browser(string subject, bool mfa = true)
    {
        var claims = Claims(subject);
        if (!mfa) { claims[TrustedActor.Prefix + "mfa"] = false; claims.Remove(TrustedActor.Prefix + "mfa_at"); }
        var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims.Select(p => new Claim(p.Key, p.Value.ToString()!)), CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }, CookieAuthenticationDefaults.AuthenticationScheme);
        var client = CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", "__Host-Alpha6.Session=" + options.TicketDataFormat.Protect(ticket));
        return client;
    }
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
