using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.EntityFrameworkCore;

var connection = Environment.GetEnvironmentVariable("ALPHA6_TEST_DATABASE");
if (args.Contains("--schema"))
{
    using var schema = new AccountsDbContext(new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql("Host=localhost;Database=unused").Options);
    Console.WriteLine(schema.Database.GenerateCreateScript());
    return;
}

var now = DateTimeOffset.UtcNow;
var actor = new ActorIdentity("https://accounts.example/", "owner", "Owner", "owner@example.com", true, true, now);
var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    passed++; Console.WriteLine("PASS: " + name);
}
void Reject(Action action, string code)
{
    try { action(); } catch (IdentityException ex) when (ex.Code == code) { Check(true, code); return; }
    throw new InvalidOperationException("Expected " + code);
}
async Task RejectAsync(Func<Task> action, string code)
{
    try { await action(); } catch (IdentityException ex) when (ex.Code == code) { Check(true, code); return; }
    throw new InvalidOperationException("Expected " + code);
}
async Task RejectDatabaseAsync(Func<Task> action, string name)
{
    try { await action(); }
    catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState is "23503" or "23514")
    { Check(true, name); return; }
    throw new InvalidOperationException("Expected database rejection: " + name);
}

Check(AccountRules.Email(" Pilot@Example.com ") == "PILOT@EXAMPLE.COM", "email normalization");
Reject(() => AccountRules.Email("Name <pilot@example.com>"), "invalid_email");
Reject(() => AccountRules.Roles([(AirlineRole)99]), "invalid_roles");
Reject(() => AccountRules.Roles([AirlineRole.Owner]), "invalid_roles");
Reject(() => AccountRules.Roles([]), "invalid_roles");
Check(AccountRules.Roles([AirlineRole.Pilot, AirlineRole.Pilot]).Length == 1, "duplicate roles normalized");
Reject(() => AccountRules.RequireRecentMfa(actor with { HasMfa = false }, now), "mfa_required");
Reject(() => AccountRules.RequireRecentMfa(actor with { AuthenticatedAt = now.AddMinutes(-6) }, now), "mfa_required");
Reject(() => AccountRules.RequireRecentMfa(actor with { AuthenticatedAt = now.AddMinutes(1) }, now), "mfa_required");
Reject(() => AccountRules.RequireRecentMfa(actor with { EmailVerified = false }, now), "email_verification_required");
Check(AccountRules.Slug(" Test-Airline ") == "test-airline", "slug normalized");
Reject(() => AccountRules.Slug("../admin"), "invalid_slug");
Check(AccountRules.HashToken("secret").Length == 64 && AccountRules.HashToken("secret") != AccountRules.HashToken("other"), "invitation hash stable and nonplaintext");
var profileRequest = new UpdateProfileRequest(" reece74 ", " a6-001 ", "kjfk", "kg", "m", "ft", "Personal", "", " rc ");
var cleanProfile = AccountRules.Profile(profileRequest);
Check(cleanProfile is { SimBriefUsername: "reece74", Callsign: "A6-001", HomeBaseIcao: "KJFK", WeightUnit: "KG", AltitudeUnit: "M", LandingDistanceUnit: "FT", PreferredWorkspace: "personal", AvatarInitials: "RC" }, "profile normalization");
Reject(() => AccountRules.Profile(profileRequest with { SimBriefUsername = "x" }), "invalid_profile");
Reject(() => AccountRules.Profile(profileRequest with { SimBriefUsername = "has space" }), "invalid_profile");
Reject(() => AccountRules.Profile(profileRequest with { HomeBaseIcao = "TOOLONG" }), "invalid_profile");
Reject(() => AccountRules.Profile(profileRequest with { WeightUnit = "ST" }), "invalid_profile");
Reject(() => AccountRules.Profile(profileRequest with { PreferredWorkspace = "airline" }), "invalid_profile");
Reject(() => AccountRules.Profile(profileRequest with { TimeZone = "Mars/Olympus" }), "invalid_profile");
Reject(() => AccountRules.Profile(profileRequest with { AvatarInitials = "ABCD" }), "invalid_profile");
Check(AccountRules.Profile(profileRequest with { TimeZone = "UTC" }).TimeZone == "UTC", "known time zone accepted");
Check(AccountRules.Profile(profileRequest with { PreferredWorkspace = "portal" }).PreferredWorkspace == "portal", "portal start preference accepted");
Check(AccountRules.ClientVersion("Alpha6OPS/0.16.0 (Windows)") == "0.16.0" && AccountRules.ClientVersion("curl/8 <script>").Length <= 32 && !AccountRules.ClientVersion("curl/8 <script>").Contains('<'), "client version extraction is sanitized");
Check(SubscriptionStatuses.IsCurrent("complimentary", null, now) && !SubscriptionStatuses.IsCurrent("canceled", null, now) && !SubscriptionStatuses.IsCurrent("active", now.AddMinutes(-1), now), "complimentary counts as current until expiry");
var routeRequest = new RouteRequest(" a6 101 ", "kmke", "kord", "14:35", 65, AccountRules.Days("12345"), "a20n", " First wave ", true);
var cleanRoute = AccountRules.Route(routeRequest);
Check(cleanRoute is { FlightNumber: "A6101", Origin: "KMKE", Destination: "KORD", DepartureUtc: "14:35", DaysOfWeek: 31, AircraftType: "A20N", Notes: "First wave" }, "route normalization");
Reject(() => AccountRules.Route(routeRequest with { Destination = "KMKE" }), "invalid_route");
Reject(() => AccountRules.Route(routeRequest with { DepartureUtc = "25:00" }), "invalid_route");
Reject(() => AccountRules.Route(routeRequest with { BlockMinutes = 0 }), "invalid_route");
Reject(() => AccountRules.Route(routeRequest with { DaysOfWeek = 0 }), "invalid_route");
Reject(() => AccountRules.Route(routeRequest with { FlightNumber = "A" }), "invalid_route");
Reject(() => AccountRules.Days("8"), "invalid_route");
Check(AccountRules.Days("7") == 64 && AccountRules.DaysText(AccountRules.AllDays) == "1234567" && AccountRules.DaysText(AccountRules.Days("6 7".Replace(" ", ""))) == "67", "day masks round trip");
var aircraftRequest = new AircraftRequest(" n123a6 ", "b738", " Spirit of Milwaukee ", "kmke", "Active", null);
Check(AccountRules.Aircraft(aircraftRequest) is { Registration: "N123A6", TypeIcao: "B738", Name: "Spirit of Milwaukee", HomeBase: "KMKE", Status: "active", Notes: "" }, "aircraft normalization");
Reject(() => AccountRules.Aircraft(aircraftRequest with { Registration = "-" }), "invalid_aircraft");
Reject(() => AccountRules.Aircraft(aircraftRequest with { TypeIcao = "" }), "invalid_aircraft");
Reject(() => AccountRules.Aircraft(aircraftRequest with { Status = "flying" }), "invalid_aircraft");
// Images are recognised by content and bounded by pixels and bytes; nothing else is accepted.
static byte[] Png(int w, int h) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
    (byte)(w >> 24), (byte)(w >> 16), (byte)(w >> 8), (byte)w, (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h, 8, 6, 0, 0, 0];
byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x02, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x40, 0x00, 0x80, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xD9];
byte[] webp = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P', (byte)'V', (byte)'P', (byte)'8', (byte)'X', 10, 0, 0, 0, 0, 0, 0, 0, 0x3F, 0, 0, 0x3F, 0, 0, 0];
Check(ImageRules.Validate(Png(256, 256)) is { ContentType: "image/png", Width: 256, Height: 256 }, "PNG header read");
Check(ImageRules.Validate(jpeg) is { ContentType: "image/jpeg", Width: 128, Height: 64 }, "JPEG SOF header read");
Check(ImageRules.Validate(webp) is { ContentType: "image/webp", Width: 64, Height: 64 }, "WebP VP8X header read");
Reject(() => ImageRules.Validate(Png(16, 16)), "invalid_image");
Reject(() => ImageRules.Validate(Png(4096, 100)), "invalid_image");
Reject(() => ImageRules.Validate(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")), "invalid_image");
Reject(() => ImageRules.Validate(new byte[ImageRules.MaxBytes + 1]), "invalid_image");
// Logbook CSV: synonyms, separators, time formats and durations from other platforms.
var parsed = LogbookCsv.Parse("Date;Flight Number;From;To;Aircraft Type;Block Time;Landing Rate\n14/03/2026 18:20;CZK101;kmke;kord;A20N;0:55;-180\n2026-03-15T09:00:00;;KORD;KMKE;;1.5h;\nnot a date;X;KMKE;KMSP;;30;");
Check(parsed.Columns.SequenceEqual(new[] { "aircraft", "block", "departure", "destination", "flight", "landing", "origin" }), "logbook columns mapped by synonym");
Check(parsed.Rows[0].Flight is { FlightNumber: "CZK101", Origin: "kmke", BlockMinutes: 55, LandingRateFpm: -180 } firstRow && firstRow.DepartureUtc == new DateTimeOffset(2026, 3, 14, 18, 20, 0, TimeSpan.Zero), "dd/MM/yyyy row parsed as UTC");
Check(parsed.Rows[1].Flight is { BlockMinutes: 90, FlightNumber: "" } && parsed.Rows[2].Error is not null, "decimal hours parsed and bad dates reported per line");
Check(LogbookCsv.ParseMinutes("1h 35m") == 95 && LogbookCsv.ParseMinutes("01:35:20") == 95 && LogbookCsv.ParseMinutes("95") == 95 && LogbookCsv.ParseMinutes("1.58") == 95, "duration formats normalised");
Check(LogbookCsv.ParseTime("1710440400") == DateTimeOffset.FromUnixTimeSeconds(1710440400) && LogbookCsv.ParseTime("") is null, "unix and blank times");
Reject(() => LogbookCsv.Parse("a,b\n1,2"), "invalid_logbook");
var flightRequest = new FlightLogRequest("czk 101", "kmke", "kord", "a20n", "n201cz", new DateTimeOffset(2026, 3, 14, 18, 20, 0, TimeSpan.Zero), null, 55, null, 200, -180, 900, "vatsim", " smooth ");
Check(AccountRules.Flight(flightRequest) is { FlightNumber: "CZK101", Origin: "KMKE", AircraftType: "A20N", Registration: "N201CZ", Network: "VATSIM", Notes: "smooth" }, "flight normalization");
Reject(() => AccountRules.Flight(flightRequest with { Origin = "K" }), "invalid_flight");
Reject(() => AccountRules.Flight(flightRequest with { DepartureUtc = DateTimeOffset.UtcNow.AddDays(3) }), "invalid_flight");

if (string.IsNullOrWhiteSpace(connection))
{
    Console.WriteLine($"{passed} pure checks passed. SKIPPED PostgreSQL integration: ALPHA6_TEST_DATABASE is not configured.");
    return;
}
var builder = new Npgsql.NpgsqlConnectionStringBuilder(connection);
if (builder.Database != "alpha6_identity_test" || builder.Host is not ("127.0.0.1" or "localhost"))
    throw new InvalidOperationException("Integration checks require the disposable local alpha6_identity_test database.");

// Start from an empty schema on every run, without dropping any existing user or test data.
var schemaName = "account_test_" + Guid.NewGuid().ToString("N");
await using (var admin = new Npgsql.NpgsqlConnection(connection))
{
    await admin.OpenAsync();
    await using var command = new Npgsql.NpgsqlCommand($"CREATE SCHEMA {schemaName}", admin);
    await command.ExecuteNonQueryAsync();
}
builder.SearchPath = schemaName;
connection = builder.ConnectionString;

AccountsDbContext Context() => new(new DbContextOptionsBuilder<AccountsDbContext>().UseNpgsql(connection).Options);
async Task<T> Run<T>(Func<AccountsService, Task<T>> action)
{
    await using var db = Context();
    return await action(new AccountsService(db));
}
async Task Mutation(Func<AccountsService, Task> action)
{
    await using var db = Context();
    await action(new AccountsService(db));
}
await using (var db = Context())
{
    Check(!db.Database.HasPendingModelChanges(), "migration snapshot matches current model");
    await db.Database.MigrateAsync();
    await db.Database.MigrateAsync();
    Check((await db.Database.GetAppliedMigrationsAsync()).Count() == 5, "migrations are repeatable");
}
var prefix = Guid.NewGuid().ToString("N")[..10];
actor = actor with { Subject = prefix + "-owner", Email = prefix + "-owner@example.com" };
var pilot = actor with { Subject = prefix + "-pilot", DisplayName = "Pilot", Email = prefix + "-pilot@example.com", HasMfa = false };
var outsider = actor with { Subject = prefix + "-outsider", Email = prefix + "-outsider@example.com" };
var ownerBootstrap = await Run(s => s.BootstrapAsync(actor));
Check(ownerBootstrap.PersonalEntitlement.Plan == PersonalPlan.Free && ownerBootstrap.Airlines.Length == 0, "new account is Personal Free");
Check(ownerBootstrap.Profile is { SimBriefUsername: "", WeightUnit: "LBS", AltitudeUnit: "FT", PreferredWorkspace: "last_used", LastSeenAt: not null }, "first bootstrap provisions a default profile and stamps last seen");
var versioned = await Run(s => s.BootstrapAsync(actor, "Alpha6OPS/0.16.0 (Windows NT 10.0)"));
Check(versioned.Profile!.LastSeenVersion == "0.16.0", "bootstrap records the client version");
var savedProfile = await Run(s => s.UpdateProfileAsync(actor, new("reece74", "a6-001", "kjfk", "KG", "FT", "M", "personal", "UTC", "rc")));
Check(savedProfile is { SimBriefUsername: "reece74", Callsign: "A6-001", HomeBaseIcao: "KJFK", WeightUnit: "KG", LandingDistanceUnit: "M", AvatarInitials: "RC", UpdatedAt: not null }, "profile update round trip");
Check((await Run(s => s.GetProfileAsync(actor))).SimBriefUsername == "reece74" && (await Run(s => s.BootstrapAsync(actor))).Profile!.HomeBaseIcao == "KJFK", "profile is durable and included in bootstrap");
var portalProfile = await Run(s => s.UpdateProfileAsync(actor, new("reece74", "A6-001", "KJFK", "KG", "FT", "M", "portal", "UTC", "RC")));
Check(portalProfile.PreferredWorkspace == "portal", "portal preference is stored by the database");
await RejectAsync(() => Run(s => s.UpdateProfileAsync(actor, new("x", "", "", "LBS", "FT", "FT", "last_used", "", ""))), "invalid_profile");
var unchanged = await Run(s => s.UpdateProfileAsync(actor, new("reece74", "A6-001", "KJFK", "KG", "FT", "M", "portal", "UTC", "RC")));
Check(unchanged.UpdatedAt is { } same && portalProfile.UpdatedAt is { } first && (same - first).Duration() < TimeSpan.FromMilliseconds(1), "identical profile update is a no-op");
await RejectAsync(() => Run(s => s.SetPersonalPlanAsync(actor with { EmailVerified = false }, new(PersonalPlan.Premium))), "email_verification_required");
var premium = await Run(s => s.SetPersonalPlanAsync(actor, new(PersonalPlan.Premium)));
Check(premium.Plan == PersonalPlan.Premium && premium.Status == "complimentary" && premium.ExpiresAt is null, "complimentary Premium applied");
Check((await Run(s => s.BootstrapAsync(actor))).PersonalEntitlement.Plan == PersonalPlan.Premium, "bootstrap reports complimentary Premium as current");
Check((await Run(s => s.SetPersonalPlanAsync(actor, new(PersonalPlan.Free)))).Plan == PersonalPlan.Free, "plan can return to Free");
var concurrentUsers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Run(s => s.BootstrapAsync(pilot))));
Check(concurrentUsers.Select(x => x.Account.Id).Distinct().Count() == 1, "concurrent account provisioning is idempotent");
var pilotId = concurrentUsers[0].Account.Id;
var changedProfile = await Run(s => s.BootstrapAsync(actor with { DisplayName = "New name" }));
Check(changedProfile.Account.Id == ownerBootstrap.Account.Id, "issuer and subject survive profile changes");
var separate = await Run(s => s.BootstrapAsync(outsider with { Email = actor.Email, EmailVerified = false }));
Check(separate.Account.Id != ownerBootstrap.Account.Id, "matching email never links accounts");
await RejectAsync(() => Run(s => s.CreateAirlineAsync(pilot, new("Blocked", prefix + "blocked", "BLK"))), "mfa_required");
var va = await Run(s => s.CreateAirlineAsync(actor, new("Test Airline", prefix + "va", "TEST")));
Check(va.Roles.Contains(AirlineRole.Owner) && va.IsFounder, "creator is current owner and historical founder");
await RejectDatabaseAsync(async () =>
{
    await using var db = Context();
    (await db.Airlines.SingleAsync(x => x.Id == va.Id)).OwnerUserId = pilotId;
    await db.SaveChangesAsync();
}, "database rejects owner without same-airline membership");
await RejectDatabaseAsync(async () =>
{
    await using var db = Context();
    (await db.Memberships.SingleAsync(x => x.AirlineId == va.Id && x.UserId == ownerBootstrap.Account.Id)).Status = MembershipStatus.Suspended;
    await db.SaveChangesAsync();
}, "database rejects suspending current owner membership");
await RejectDatabaseAsync(async () =>
{
    await using var db = Context();
    (await db.Airlines.SingleAsync(x => x.Id == va.Id)).FounderUserId = pilotId;
    await db.SaveChangesAsync();
}, "database preserves immutable founder");
await RejectAsync(() => Run(s => s.CreateAirlineAsync(actor, new("Second", prefix + "second", "TWO"))), "plan_limit_reached");
await RejectAsync(() => Run(s => s.SetAirlinePlanAsync(outsider, va.Id, new(AirlinePlan.Pro))), "membership_required");
var proVa = await Run(s => s.SetAirlinePlanAsync(actor, va.Id, new(AirlinePlan.Pro)));
Check(proVa.Plan == AirlinePlan.Pro && proVa.SubscriptionStatus == "complimentary", "owner applies complimentary Pro");
var secondVa = await Run(s => s.CreateAirlineAsync(actor, new("Second", prefix + "second", "TWO")));
Check(secondVa.Plan == AirlinePlan.Community, "complimentary Pro airline no longer occupies the Community slot");
await RejectAsync(() => Run(s => s.SetAirlinePlanAsync(actor, va.Id, new(AirlinePlan.Community))), "plan_limit_reached");
Check((await Run(s => s.SetAirlinePlanAsync(actor, secondVa.Id, new(AirlinePlan.Pro)))).Plan == AirlinePlan.Pro && (await Run(s => s.SetAirlinePlanAsync(actor, va.Id, new(AirlinePlan.Community)))).Plan == AirlinePlan.Community, "Community slot frees once the other airline is Pro");
await Run(s => s.SetAirlinePlanAsync(actor, va.Id, new(AirlinePlan.Community)));
await using (var db = Context())
{
    (await db.Airlines.SingleAsync(x => x.Id == secondVa.Id)).Status = "suspended";
    await db.SaveChangesAsync();
}
await RejectAsync(() => Run(s => s.GetAirlineAsync(outsider, va.Id)), "membership_required");
await RejectAsync(() => Run(s => s.ListMembersAsync(outsider, va.Id)), "membership_required");
await RejectAsync(() => Run(s => s.ListActivityAsync(outsider, va.Id)), "membership_required");
var activity = await Run(s => s.ListActivityAsync(actor, va.Id));
Check(activity.Any(x => x.Action == "airline.created" && x.ActorDisplayName == actor.DisplayName) && activity.Any(x => x.Action == "airline.plan_changed") && activity.All(x => x.Details.Length <= 2000), "administrators can read the airline activity log with current actor names");
await RejectAsync(() => Mutation(s => s.SetWorkspaceAsync(outsider, new(va.Id))), "membership_required");
await RejectAsync(() => Run(s => s.InviteAsync(actor, va.Id, new(pilot.Email, [AirlineRole.Owner]))), "invalid_roles");
var issued = await Run(s => s.InviteAsync(actor, va.Id, new(pilot.Email, [AirlineRole.Pilot])));
await using (var db = Context())
{
    var stored = await db.Invitations.SingleAsync(x => x.Id == issued.Invitation.Id);
    Check(stored.TokenHash == AccountRules.HashToken(issued.Token) && stored.TokenHash != issued.Token, "database stores only invitation hash");
}
await RejectAsync(() => Run(s => s.AcceptInvitationAsync(outsider, issued.Token)), "invitation_email_mismatch");
await RejectAsync(() => Run(s => s.AcceptInvitationAsync(pilot with { EmailVerified = false }, issued.Token)), "email_verification_required");
var accepted = await Run(s => s.AcceptInvitationAsync(pilot, issued.Token));
Check(accepted.Id == va.Id && accepted.Roles.SequenceEqual([AirlineRole.Pilot]), "verified invited pilot joins");
var repeated = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Run(s => s.AcceptInvitationAsync(pilot, issued.Token))));
Check(repeated.All(x => x.Id == va.Id), "concurrent acceptance retries are idempotent");
await RejectAsync(() => Run(s => s.ListMembersAsync(pilot, va.Id)), "administrator_required");
await Mutation(s => s.SetWorkspaceAsync(pilot, new(va.Id)));
Check((await Run(s => s.BootstrapAsync(pilot))).LastWorkspace.AirlineId == va.Id, "workspace selection persisted");
var members = await Run(s => s.ListMembersAsync(actor, va.Id));
var pilotMembership = members.Single(x => x.UserId == pilotId);
await RejectDatabaseAsync(async () =>
{
    await using var db = Context();
    db.MembershipRoles.Add(new() { AirlineId = va.Id, MembershipId = pilotMembership.Id, Role = (AirlineRole)99 });
    await db.SaveChangesAsync();
}, "database rejects unknown role values");
await using (var db = Context())
{
    db.MembershipRoles.RemoveRange(await db.MembershipRoles.Where(x => x.MembershipId == pilotMembership.Id).ToArrayAsync());
    await db.SaveChangesAsync();
}
await RejectAsync(() => Run(s => s.GetAirlineAsync(pilot, va.Id)), "invalid_membership_roles");
await using (var db = Context())
{
    db.MembershipRoles.Add(new() { AirlineId = va.Id, MembershipId = pilotMembership.Id, Role = AirlineRole.Pilot });
    await db.SaveChangesAsync();
}
await RejectAsync(() => Mutation(s => s.ChangeRolesAsync(actor, va.Id, pilotMembership.Id, new([(AirlineRole)666]))), "invalid_roles");
await Mutation(s => s.ChangeRolesAsync(actor, va.Id, pilotMembership.Id, new([AirlineRole.Pilot, AirlineRole.Administrator])));
await RejectAsync(() => Run(s => s.ListMembersAsync(pilot, va.Id)), "mfa_required");
var pilotMfa = pilot with { HasMfa = true, AuthenticatedAt = DateTimeOffset.UtcNow };
Check((await Run(s => s.ListMembersAsync(pilotMfa, va.Id))).Length == 2, "administrator requires recent MFA");
await Mutation(s => s.ChangeRolesAsync(actor, va.Id, pilotMembership.Id, new([AirlineRole.Pilot])));
await RejectAsync(() => Run(s => s.ListMembersAsync(pilotMfa, va.Id)), "administrator_required");
var outsiderVa = await Run(s => s.CreateAirlineAsync(outsider, new("Other airline", prefix + "other", "OTHR")));
await RejectDatabaseAsync(async () =>
{
    await using var db = Context();
    db.MembershipRoles.Add(new() { AirlineId = outsiderVa.Id, MembershipId = pilotMembership.Id, Role = AirlineRole.Dispatcher });
    await db.SaveChangesAsync();
}, "composite foreign key rejects role from another tenant");
await RejectAsync(() => Mutation(s => s.ChangeRolesAsync(outsider, outsiderVa.Id, pilotMembership.Id, new([AirlineRole.Dispatcher]))), "membership_required");
var otherInvite = await Run(s => s.InviteAsync(outsider, outsiderVa.Id, new(pilot.Email, [AirlineRole.Dispatcher])));
await Run(s => s.AcceptInvitationAsync(pilot, otherInvite.Token));
Check((await Run(s => s.BootstrapAsync(pilot))).Airlines.Length == 2, "pilot can belong to multiple airlines");
// Schedules and fleet: any member reads, dispatchers and up write without a security check.
var route = await Run(s => s.SaveRouteAsync(actor, va.Id, null, routeRequest));
Check(route.FlightNumber == "A6101" && route.DaysOfWeek == 31, "owner adds a route");
await RejectAsync(() => Run(s => s.SaveRouteAsync(actor, va.Id, null, routeRequest)), "route_exists");
await RejectAsync(() => Run(s => s.SaveRouteAsync(pilot, va.Id, null, routeRequest with { FlightNumber = "A6102" })), "dispatcher_required");
await RejectAsync(() => Run(s => s.ListRoutesAsync(outsider, va.Id)), "membership_required");
Check((await Run(s => s.ListRoutesAsync(pilot, va.Id))).Length == 1, "pilot reads the schedule");
Check((await Run(s => s.SaveRouteAsync(pilot, outsiderVa.Id, null, routeRequest))).Origin == "KMKE", "dispatcher edits the schedule without MFA");
var edited = await Run(s => s.SaveRouteAsync(actor, va.Id, route.Id, routeRequest with { BlockMinutes = 70, Active = false }));
Check(edited.Id == route.Id && edited.BlockMinutes == 70 && !edited.Active, "route update keeps its identity");
await RejectAsync(() => Run(s => s.SaveRouteAsync(actor, outsiderVa.Id, route.Id, routeRequest)), "membership_required");
var import = await Run(s => s.ImportRoutesAsync(actor, va.Id, new("flight,origin,destination,departure_utc,block_minutes,days,aircraft_type,notes\nA6101,KMKE,KORD,15:00,60,1234567,A20N,Updated by import\nA6102,KORD,KMKE,17:10,55,67,,Weekend return\nbad,KMKE,KMKE,17:10,55,12\n")));
Check(import is { Created: 1, Updated: 1, Skipped: 1 } && import.Errors[0].StartsWith("Line 4"), "CSV import creates, updates and reports bad rows");
var schedule = await Run(s => s.ListRoutesAsync(actor, va.Id));
Check(schedule.Length == 2 && schedule.Single(x => x.FlightNumber == "A6101").DepartureUtc == "15:00" && schedule.Single(x => x.FlightNumber == "A6102").DaysOfWeek == 96, "imported schedule is stored");
await RejectDatabaseAsync(async () =>
{
    await using var db = Context();
    db.Routes.Add(new() { Id = Guid.NewGuid(), AirlineId = va.Id, FlightNumber = "A6103", Origin = "KMKE", Destination = "KMKE", DepartureUtc = new(1, 0), BlockMinutes = 30, DaysOfWeek = 1, CreatedAt = now, UpdatedAt = now });
    await db.SaveChangesAsync();
}, "database rejects a route to its own origin");
await Mutation(s => s.DeleteRouteAsync(actor, va.Id, schedule.Single(x => x.FlightNumber == "A6102").Id));
await RejectAsync(() => Mutation(s => s.DeleteRouteAsync(actor, va.Id, Guid.NewGuid())), "route_not_found");
var tail = await Run(s => s.SaveAircraftAsync(actor, va.Id, null, aircraftRequest));
Check(tail.Registration == "N123A6" && tail.Status == "active", "owner adds an aircraft");
await RejectAsync(() => Run(s => s.SaveAircraftAsync(actor, va.Id, null, aircraftRequest)), "aircraft_exists");
await RejectAsync(() => Run(s => s.SaveAircraftAsync(pilot, va.Id, null, aircraftRequest with { Registration = "N124A6" })), "dispatcher_required");
Check((await Run(s => s.SaveAircraftAsync(actor, va.Id, tail.Id, aircraftRequest with { Status = "maintenance" }))).Status == "maintenance", "aircraft status changes");
Check((await Run(s => s.ListFleetAsync(pilot, va.Id))).Single().Status == "maintenance", "pilot reads the fleet");
await Mutation(s => s.DeleteAircraftAsync(actor, va.Id, tail.Id));
Check((await Run(s => s.ListFleetAsync(actor, va.Id))).Length == 0, "aircraft removed");
// Logbook: import with duplicates and bad rows, undo by batch, manual add, delete.
var logbook = await Run(s => s.ImportLogbookAsync(pilot, new("date,flight,origin,destination,aircraft,block,landing_rate\n2026-03-14 18:20,CZK101,KMKE,KORD,A20N,0:55,-180\n2026-03-14 18:20,CZK101,KMKE,KORD,A20N,0:55,-180\n2026-03-15 09:00,CZK102,KORD,KMKE,A20N,50,-240\nbad,CZK103,KMKE,KMSP,,30,\n")));
Check(logbook is { Created: 2, Duplicates: 1, Skipped: 1 } && logbook.Errors.Length == 1, "logbook import counts created, duplicate and skipped rows");
var again = await Run(s => s.ImportLogbookAsync(pilot, new("date,flight,origin,destination\n2026-03-14 18:20,CZK101,KMKE,KORD\n")));
Check(again is { Created: 0, Duplicates: 1 }, "re-importing the same flight is a duplicate, not a copy");
var summary = await Run(s => s.LogbookSummaryAsync(pilot));
Check(summary is { Flights: 2, BlockMinutes: 105, Airports: 2 } && summary.TopAircraft.SequenceEqual(new[] { "A20N" }), "logbook summary totals");
Check((await Run(s => s.ListFlightsAsync(actor, 1, 50))).Total == 0, "logbooks are per pilot");
var added = await Run(s => s.AddFlightAsync(pilot, flightRequest with { DepartureUtc = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero) }, "desktop"));
Check(added.Source == "desktop" && (await Run(s => s.ListFlightsAsync(pilot, 1, 2))).Entries[0].Id == added.Id, "desktop flights are listed newest first");
await RejectAsync(() => Run(s => s.AddFlightAsync(pilot, flightRequest with { DepartureUtc = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero) }, "desktop")), "flight_exists");
Check(await Run(s => s.UndoImportAsync(pilot, logbook.BatchId)) == 2 && (await Run(s => s.LogbookSummaryAsync(pilot))).Flights == 1, "undoing an import removes only that batch");
await Mutation(s => s.DeleteFlightAsync(pilot, added.Id));
await RejectAsync(() => Mutation(s => s.DeleteFlightAsync(pilot, added.Id)), "flight_not_found");
// Media: avatars and logos are validated, versioned in the profile and workspace, and readable anonymously by id.
var withAvatar = await Run(s => s.SetAvatarAsync(pilot, Png(128, 128)));
Check(withAvatar.AvatarUrl.StartsWith($"/media/avatar/{pilotId:N}?v="), "avatar url carries a version");
Check((await Run(s => s.BootstrapAsync(pilot))).Profile!.AvatarUrl == withAvatar.AvatarUrl, "bootstrap reports the avatar");
await RejectAsync(() => Run(s => s.SetAvatarAsync(pilot, System.Text.Encoding.UTF8.GetBytes("GIF89a"))), "invalid_image");
var blob = await Run(s => s.GetMediaAsync("avatar", pilotId));
Check(blob is { ContentType: "image/png", Width: 128 } && blob.Sha256.Length == 64, "media blob stored with hash");
await Mutation(s => s.RemoveAvatarAsync(pilot));
Check((await Run(s => s.GetProfileAsync(pilot))).AvatarUrl == "" && await Run(s => s.GetMediaAsync("avatar", pilotId)) is null, "avatar removed");
await RejectAsync(() => Run(s => s.SetAirlineLogoAsync(pilot, va.Id, Png(200, 100))), "administrator_required");
var branded = await Run(s => s.SetAirlineLogoAsync(actor with { HasMfa = false, AuthenticatedAt = null }, va.Id, Png(200, 100)));
Check(branded.LogoUrl.StartsWith($"/media/airline-logo/{va.Id:N}?v="), "administrators set the logo without a fresh security check");
Check((await Run(s => s.BootstrapAsync(pilot))).Airlines.Single(x => x.Id == va.Id).LogoUrl == branded.LogoUrl, "members see the airline logo");
await Mutation(s => s.RemoveAirlineLogoAsync(actor, va.Id));
Check((await Run(s => s.GetAirlineAsync(actor, va.Id))).LogoUrl == "", "logo removed");
var revoked = await Run(s => s.InviteAsync(actor, va.Id, new(outsider.Email, [AirlineRole.Pilot])));
await Mutation(s => s.RevokeInvitationAsync(actor, va.Id, revoked.Invitation.Id));
await RejectAsync(() => Run(s => s.AcceptInvitationAsync(outsider, revoked.Token)), "invitation_invalid");
var expired = await Run(s => s.InviteAsync(actor, va.Id, new(outsider.Email, [AirlineRole.Pilot])));
await using (var db = Context())
{
    (await db.Invitations.SingleAsync(x => x.Id == expired.Invitation.Id)).ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
    await db.SaveChangesAsync();
}
await RejectAsync(() => Run(s => s.AcceptInvitationAsync(outsider, expired.Token)), "invitation_expired");
await using (var db = Context())
{
    var airline = await db.Airlines.SingleAsync(x => x.Id == outsiderVa.Id);
    airline.Plan = AirlinePlan.Pro; airline.SubscriptionExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
    await db.SaveChangesAsync();
}
Check((await Run(s => s.GetAirlineAsync(outsider, outsiderVa.Id))).Plan == AirlinePlan.Community, "expired Pro resolves to Community");
await RejectAsync(() => Run(s => s.CreateAirlineAsync(outsider, new("Third", prefix + "third", "THRD"))), "plan_limit_reached");
var ownerInvite = await Run(s => s.InviteAsync(actor, va.Id, new(outsider.Email, [AirlineRole.Pilot])));
await Run(s => s.AcceptInvitationAsync(outsider, ownerInvite.Token));
await RejectAsync(() => Mutation(s => s.TransferOwnershipAsync(actor, va.Id, new(separate.Account.Id))), "plan_limit_reached");
await using (var db = Context())
{
    var airline = await db.Airlines.SingleAsync(x => x.Id == outsiderVa.Id);
    airline.SubscriptionExpiresAt = null; airline.SubscriptionStatus = "canceled";
    await db.SaveChangesAsync();
}
await RejectAsync(() => Run(s => s.CreateAirlineAsync(outsider, new("Fourth", prefix + "fourth", "FRTH"))), "plan_limit_reached");
await Mutation(s => s.TransferOwnershipAsync(actor, va.Id, new(pilotId)));
var after = await Run(s => s.GetAirlineAsync(actor, va.Id));
Check(after.IsFounder && !after.Roles.Contains(AirlineRole.Owner) && after.Roles.Contains(AirlineRole.Administrator), "transfer retains founder history and previous owner's administrator access");
Check((await Run(s => s.GetAirlineAsync(pilotMfa, va.Id))).Roles.Contains(AirlineRole.Owner), "transfer assigns exactly one new owner");
await RejectAsync(() => Mutation(s => s.TransferOwnershipAsync(actor, va.Id, new(ownerBootstrap.Account.Id))), "owner_required");
await RejectAsync(() => Mutation(s => s.ChangeRolesAsync(actor, va.Id, pilotMembership.Id, new([AirlineRole.Pilot]))), "ownership_invariant");
await using (var db = Context())
{
    var otherMembership = await db.Memberships.SingleAsync(x => x.UserId == pilotId && x.AirlineId == outsiderVa.Id);
    otherMembership.Status = MembershipStatus.Suspended;
    await db.SaveChangesAsync();
}
await RejectAsync(() => Run(s => s.AcceptInvitationAsync(pilot, otherInvite.Token)), "membership_inactive");
Check((await Run(s => s.BootstrapAsync(pilot))).Airlines.Length == 1, "suspended membership filtered");
await Mutation(s => s.SetWorkspaceAsync(outsider, new(outsiderVa.Id)));
await using (var db = Context())
{
    (await db.Airlines.SingleAsync(x => x.Id == outsiderVa.Id)).Status = "suspended";
    await db.SaveChangesAsync();
}
var hidden = await Run(s => s.BootstrapAsync(outsider));
Check(hidden.Airlines.All(x => x.Id != outsiderVa.Id) && hidden.LastWorkspace.AirlineId is null, "suspended airline hidden and stale preference returns to personal");
await using (var db = Context())
{
    (await db.Users.SingleAsync(x => x.Id == pilotId)).Status = AccountStatus.Suspended;
    await db.SaveChangesAsync();
}
await RejectAsync(() => Run(s => s.BootstrapAsync(pilot)), "account_suspended");
await RejectAsync(() => Run(s => s.GetAirlineAsync(pilot, va.Id)), "account_suspended");
await using (var db = Context())
{
    var audits = await db.AuditEvents.Where(x => x.AirlineId == va.Id).ToArrayAsync();
    Check(audits.Any(x => x.Action == "airline.ownership_transferred"), "ownership changes are audited");
    Check(audits.All(x => !x.Details.Contains(issued.Token) && !x.Details.Contains(revoked.Token)), "audit excludes invitation secrets");
    Check(audits.Count(x => x.Action == "invitation.accepted" && x.TargetId == issued.Invitation.Id) == 1, "concurrent acceptance has exactly one audit event");
    Check(audits.Any(x => x.Action == "airline.plan_changed" && x.Details.Contains("Pro")), "airline plan changes are audited");
    var personalAudits = await db.AuditEvents.Where(x => x.ActorUserId == ownerBootstrap.Account.Id && x.AirlineId == null).ToArrayAsync();
    Check(personalAudits.Any(x => x.Action == "account.plan_changed" && x.Details.Contains("Premium")), "personal plan changes are audited");
    var profileAudits = personalAudits.Where(x => x.Action == "profile.updated").OrderBy(x => x.CreatedAt).ToArray();
    Check(profileAudits.Length == 2 && profileAudits[0].Details.Contains("simbrief") && profileAudits[1].Details == "workspace" && profileAudits.All(x => !x.Details.Contains("reece74")),
        "profile audit lists changed fields without values and skips no-op saves");
}
var racingOwner = actor with { Subject = prefix + "-race", Email = prefix + "-race@example.com" };
var races = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
{
    try { await Run(s => s.CreateAirlineAsync(racingOwner, new("Race", prefix + "race" + i, "RACE"))); return "created"; }
    catch (IdentityException ex) { return ex.Code; }
}));
Check(races.Count(x => x == "created") == 1 && races.Count(x => x == "plan_limit_reached") == 3, "concurrent creation cannot bypass Community ownership limit");
Console.WriteLine($"{passed} account checks passed, including real PostgreSQL integration.");
Console.WriteLine($"Disposable test schema retained for inspection: {schemaName}");
