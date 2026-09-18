using System.Security.Cryptography;
using Alpha6Ops.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alpha6Ops.Accounts;

/// <summary>Scoped service. Each request rechecks authorization inside a PostgreSQL transaction.</summary>
public sealed class AccountsService(AccountsDbContext db, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    // One transaction-scoped lock deliberately serializes this first, low-volume account service.
    // It also makes concurrent first-login provisioning and cross-airline ownership limits safe.
    private const long LockId = 0x416C706861364F;

    private async Task<T> Execute<T>(ActorIdentity actor, Func<UserAccount, DateTimeOffset, Task<T>> action, CancellationToken ct)
    {
        if (db.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
            throw new InvalidOperationException("Accounts requires PostgreSQL; an in-memory authentication store is not supported.");
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockId})", ct);
        var now = clock.GetUtcNow();
        var issuer = AccountRules.Text(actor.Issuer, 255, "Issuer");
        var subject = AccountRules.Text(actor.Subject, 255, "Subject");
        if (issuer != actor.Issuer || subject != actor.Subject)
            throw new IdentityException("invalid_identity", "The identity issuer and subject must be exact identifiers.", 401);
        // Never merge accounts by email. Only the validated OIDC issuer and subject establish identity.
        var user = await db.Users.SingleOrDefaultAsync(x => x.Issuer == issuer && x.Subject == subject, ct);
        if (user is not null && user.Status != AccountStatus.Active)
            throw new IdentityException("account_suspended", "This account is suspended.");
        var email = AccountRules.Email(actor.Email);
        var displayName = AccountRules.Text(actor.DisplayName, 150, "Display name");
        if (user is null)
        {
            user = new UserAccount { Id = Guid.NewGuid(), Issuer = issuer, Subject = subject, DisplayName = displayName,
                Email = email, EmailVerified = actor.EmailVerified, CreatedAt = now };
            db.Users.Add(user);
            Audit(user.Id, null, "account.created", user.Id, "", now);
            await db.SaveChangesAsync(ct);
        }
        else
        {
            user.DisplayName = displayName; user.Email = email; user.EmailVerified = actor.EmailVerified;
        }
        var result = await action(user, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public Task<BootstrapResponse> BootstrapAsync(ActorIdentity actor, CancellationToken ct = default) => BootstrapAsync(actor, null, ct);

    public Task<BootstrapResponse> BootstrapAsync(ActorIdentity actor, string? clientVersion, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        var profile = await EnsureProfile(user, ct);
        profile.LastSeenAt = now;
        var version = AccountRules.ClientVersion(clientVersion);
        if (version.Length > 0) profile.LastSeenVersion = version;
        var memberships = await db.Memberships.Include(x => x.Roles).Where(x => x.UserId == user.Id && x.Status == MembershipStatus.Active).ToArrayAsync(ct);
        var ids = memberships.Select(x => x.AirlineId).ToArray();
        var airlines = await db.Airlines.Where(x => ids.Contains(x.Id) && x.Status == "active").ToArrayAsync(ct);
        var workspaces = airlines.OrderBy(x => x.Name).Select(x => Workspace(x, memberships.Single(m => m.AirlineId == x.Id), user, now)).ToArray();
        if (user.LastAirlineId is { } last && workspaces.All(x => x.Id != last)) user.LastAirlineId = null;
        var plan = IsCurrent(user.SubscriptionStatus, user.SubscriptionExpiresAt, now) && Enum.IsDefined(user.Plan) ? user.Plan : PersonalPlan.Free;
        return new BootstrapResponse(new(user.Id, user.DisplayName, user.Email, user.EmailVerified, user.Status),
            new(plan, user.SubscriptionStatus, user.SubscriptionExpiresAt), Capabilities.Personal, workspaces,
            new(user.LastAirlineId), now, now.AddDays(30), ProfileView(profile));
    }, ct);

    public Task<UserProfile> GetProfileAsync(ActorIdentity actor, CancellationToken ct = default) => Execute(actor, async (user, now) =>
        ProfileView(await EnsureProfile(user, ct)), ct);

    public Task<UserProfile> UpdateProfileAsync(ActorIdentity actor, UpdateProfileRequest request, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        var clean = AccountRules.Profile(request);
        var profile = await EnsureProfile(user, ct);
        var changed = new List<string>();
        void Set(string field, string current, string next, Action apply) { if (current != next) { changed.Add(field); apply(); } }
        Set("simbrief", profile.SimBriefUsername, clean.SimBriefUsername, () => profile.SimBriefUsername = clean.SimBriefUsername);
        Set("callsign", profile.Callsign, clean.Callsign, () => profile.Callsign = clean.Callsign);
        Set("home_base", profile.HomeBaseIcao, clean.HomeBaseIcao, () => profile.HomeBaseIcao = clean.HomeBaseIcao);
        Set("weight_unit", profile.WeightUnit, clean.WeightUnit, () => profile.WeightUnit = clean.WeightUnit);
        Set("altitude_unit", profile.AltitudeUnit, clean.AltitudeUnit, () => profile.AltitudeUnit = clean.AltitudeUnit);
        Set("landing_unit", profile.LandingDistanceUnit, clean.LandingDistanceUnit, () => profile.LandingDistanceUnit = clean.LandingDistanceUnit);
        Set("workspace", profile.PreferredWorkspace, clean.PreferredWorkspace, () => profile.PreferredWorkspace = clean.PreferredWorkspace);
        Set("time_zone", profile.TimeZone, clean.TimeZone, () => profile.TimeZone = clean.TimeZone);
        Set("initials", profile.AvatarInitials, clean.AvatarInitials, () => profile.AvatarInitials = clean.AvatarInitials);
        if (changed.Count > 0)
        {
            profile.UpdatedAt = now;
            // Field names only: profile values are personal data and never belong in the audit trail.
            Audit(user.Id, null, "profile.updated", user.Id, string.Join(",", changed), now);
        }
        return ProfileView(profile);
    }, ct);

    public Task<PersonalEntitlement> SetPersonalPlanAsync(ActorIdentity actor, SetPersonalPlanRequest request, CancellationToken ct = default) => Execute(actor, (user, now) =>
    {
        if (!Enum.IsDefined(request.Plan)) throw new IdentityException("invalid_plan", "Choose Free or Premium.", 400);
        if (!actor.EmailVerified) throw new IdentityException("email_verification_required", "Verify your email before changing your plan.");
        var current = IsCurrent(user.SubscriptionStatus, user.SubscriptionExpiresAt, now);
        var before = current ? user.Plan : PersonalPlan.Free;
        if (before != request.Plan || !current)
        {
            user.Plan = request.Plan; user.SubscriptionStatus = SubscriptionStatuses.Complimentary; user.SubscriptionExpiresAt = null;
            Audit(user.Id, null, "account.plan_changed", user.Id, $"{before} -> {request.Plan} (complimentary)", now);
        }
        return Task.FromResult(new PersonalEntitlement(request.Plan, user.SubscriptionStatus, user.SubscriptionExpiresAt));
    }, ct);

    public Task<AirlineWorkspace> SetAirlinePlanAsync(ActorIdentity actor, Guid airlineId, SetAirlinePlanRequest request, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        if (!Enum.IsDefined(request.Plan)) throw new IdentityException("invalid_plan", "Choose Community or Pro.", 400);
        var (airline, member) = await MemberAccess(user, airlineId, true, actor, now, ct);
        if (airline.OwnerUserId != user.Id) throw new IdentityException("owner_required", "Only the airline owner can change its plan.");
        var before = EffectivePlan(airline, now);
        if (before != request.Plan)
        {
            if (request.Plan == AirlinePlan.Community && await HasCommunityAirline(user.Id, now, ct, excluding: airlineId))
                throw new IdentityException("plan_limit_reached", "You already own another Community airline.", 409);
            airline.Plan = request.Plan; airline.SubscriptionStatus = SubscriptionStatuses.Complimentary; airline.SubscriptionExpiresAt = null;
            Audit(user.Id, airlineId, "airline.plan_changed", airlineId, $"{before} -> {request.Plan} (complimentary)", now);
        }
        return Workspace(airline, member, user, now);
    }, ct);

    public async Task SetWorkspaceAsync(ActorIdentity actor, WorkspaceSelection request, CancellationToken ct = default) =>
        await Execute(actor, async (user, now) =>
        {
            if (request.AirlineId is { } id) await MemberAccess(user, id, false, actor, now, ct);
            user.LastAirlineId = request.AirlineId;
            return true;
        }, ct);

    public Task<AirlineWorkspace> CreateAirlineAsync(ActorIdentity actor, CreateAirlineRequest request, CancellationToken ct = default) =>
        Execute(actor, async (user, now) =>
        {
            AccountRules.RequireRecentMfa(actor, now);
            var slug = AccountRules.Slug(request.Slug);
            if (await HasCommunityAirline(user.Id, now, ct))
                throw new IdentityException("plan_limit_reached", "You already own a Community airline.", 409);
            if (await db.Airlines.AnyAsync(x => x.Slug == slug, ct)) throw new IdentityException("slug_unavailable", "This airline address is already in use.", 409);
            var airline = new VirtualAirline { Id = Guid.NewGuid(), Slug = slug, Name = AccountRules.Text(request.Name, 100, "Airline name"),
                Callsign = AccountRules.Text(request.Callsign, 20, "Callsign"), FounderUserId = user.Id, OwnerUserId = user.Id, CreatedAt = now };
            var member = new Membership { Id = Guid.NewGuid(), AirlineId = airline.Id, UserId = user.Id, JoinedAt = now };
            member.Roles.Add(new() { AirlineId = airline.Id, MembershipId = member.Id, Role = AirlineRole.Pilot });
            db.Airlines.Add(airline); db.Memberships.Add(member);
            Audit(user.Id, airline.Id, "airline.created", airline.Id, "Community; founder and owner assigned", now);
            return Workspace(airline, member, user, now);
        }, ct);

    public Task<AirlineWorkspace> GetAirlineAsync(ActorIdentity actor, Guid airlineId, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        var (airline, member) = await MemberAccess(user, airlineId, false, actor, now, ct);
        return Workspace(airline, member, user, now);
    }, ct);

    public Task<MemberResponse[]> ListMembersAsync(ActorIdentity actor, Guid airlineId, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        var (airline, _) = await MemberAccess(user, airlineId, true, actor, now, ct);
        var members = await db.Memberships.Include(x => x.Roles).Where(x => x.AirlineId == airlineId).ToArrayAsync(ct);
        var ids = members.Select(x => x.UserId).ToArray();
        var users = await db.Users.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return members.Select(m => new MemberResponse(m.Id, m.UserId, users[m.UserId].DisplayName, users[m.UserId].Email, m.Status, Roles(airline, m))).ToArray();
    }, ct);

    public Task<IssuedInvitation> InviteAsync(ActorIdentity actor, Guid airlineId, InviteMemberRequest request, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        await MemberAccess(user, airlineId, true, actor, now, ct);
        var email = AccountRules.Email(request.Email); var roles = AccountRules.Roles(request.Roles);
        var duplicates = await db.Invitations.Where(x => x.AirlineId == airlineId && x.Email == email && x.Status == "pending").ToArrayAsync(ct);
        foreach (var duplicate in duplicates)
        {
            duplicate.Status = "revoked";
            Audit(user.Id, airlineId, "invitation.revoked", duplicate.Id, "Replaced by a new invitation", now);
        }
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var invitation = new Invitation { Id = Guid.NewGuid(), AirlineId = airlineId, IssuedByUserId = user.Id, Email = email,
            Roles = roles.Select(x => (int)x).ToArray(), TokenHash = AccountRules.HashToken(token), ExpiresAt = now.AddDays(7), CreatedAt = now };
        db.Invitations.Add(invitation);
        Audit(user.Id, airlineId, "invitation.issued", invitation.Id, string.Join(',', roles), now);
        return new IssuedInvitation(InvitationView(invitation, now), token);
    }, ct);

    public Task<ActivityEntry[]> ListActivityAsync(ActorIdentity actor, Guid airlineId, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        await MemberAccess(user, airlineId, true, actor, now, ct);
        var events = await db.AuditEvents.Where(x => x.AirlineId == airlineId).OrderByDescending(x => x.CreatedAt).Take(100).ToArrayAsync(ct);
        var actorIds = events.Select(x => x.ActorUserId).Distinct().ToArray();
        var names = await db.Users.Where(x => actorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        return events.Select(x => new ActivityEntry(x.Id, x.Action, x.Details, x.CreatedAt, names.GetValueOrDefault(x.ActorUserId, "Former member"))).ToArray();
    }, ct);

    public Task<InvitationResponse[]> ListInvitationsAsync(ActorIdentity actor, Guid airlineId, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        await MemberAccess(user, airlineId, true, actor, now, ct);
        var invitations = await db.Invitations.Where(x => x.AirlineId == airlineId).OrderByDescending(x => x.CreatedAt).Take(200).ToArrayAsync(ct);
        return invitations.Select(x => InvitationView(x, now)).ToArray();
    }, ct);

    public Task<AirlineWorkspace> AcceptInvitationAsync(ActorIdentity actor, string token, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        if (!actor.EmailVerified) throw new IdentityException("email_verification_required", "Verify your email before accepting an invitation.");
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128) throw InvalidInvitation();
        var hash = AccountRules.HashToken(token);
        var invitation = await db.Invitations.SingleOrDefaultAsync(x => x.TokenHash == hash, ct) ?? throw InvalidInvitation();
        if (invitation.Email != user.Email) throw new IdentityException("invitation_email_mismatch", "Sign in with the verified email this invitation was sent to.");
        if (invitation.Status == "revoked") throw InvalidInvitation();
        var airline = await db.Airlines.SingleOrDefaultAsync(x => x.Id == invitation.AirlineId && x.Status == "active", ct) ?? throw InvalidInvitation();
        var member = await db.Memberships.Include(x => x.Roles).SingleOrDefaultAsync(x => x.AirlineId == airline.Id && x.UserId == user.Id, ct);
        if (member is not null && member.Status != MembershipStatus.Active) throw new IdentityException("membership_inactive", "This membership is no longer active.");
        if (invitation.Status == "accepted")
        {
            if (invitation.AcceptedByUserId != user.Id || member is null) throw InvalidInvitation();
            return Workspace(airline, member, user, now);
        }
        if (invitation.Status != "pending") throw InvalidInvitation();
        if (invitation.ExpiresAt <= now) throw new IdentityException("invitation_expired", "This invitation has expired.", 410);
        var roles = AccountRules.Roles(invitation.Roles.Select(x => (AirlineRole)x).ToArray());
        if (roles.Contains(AirlineRole.Administrator)) AccountRules.RequireRecentMfa(actor, now);
        if (member is null)
        {
            member = new() { Id = Guid.NewGuid(), AirlineId = airline.Id, UserId = user.Id, JoinedAt = now };
            db.Memberships.Add(member);
        }
        // Accepting a second invitation can add roles, but never removes current roles or ownership.
        foreach (var role in roles.Where(role => member.Roles.All(r => r.Role != role)))
            member.Roles.Add(new() { AirlineId = airline.Id, MembershipId = member.Id, Role = role });
        invitation.Status = "accepted"; invitation.AcceptedByUserId = user.Id; invitation.AcceptedAt = now;
        Audit(user.Id, airline.Id, "invitation.accepted", invitation.Id, string.Join(',', roles), now);
        return Workspace(airline, member, user, now);
    }, ct);

    public async Task RevokeInvitationAsync(ActorIdentity actor, Guid airlineId, Guid invitationId, CancellationToken ct = default) => await Execute(actor, async (user, now) =>
    {
        await MemberAccess(user, airlineId, true, actor, now, ct);
        var invitation = await db.Invitations.SingleOrDefaultAsync(x => x.Id == invitationId && x.AirlineId == airlineId, ct) ?? throw InvalidInvitation();
        if (invitation.Status == "accepted") throw new IdentityException("invitation_already_accepted", "This invitation has already been accepted.", 409);
        if (invitation.Status != "revoked")
        {
            invitation.Status = "revoked";
            Audit(user.Id, airlineId, "invitation.revoked", invitationId, "", now);
        }
        return true;
    }, ct);

    public async Task ChangeRolesAsync(ActorIdentity actor, Guid airlineId, Guid membershipId, ChangeRolesRequest request, CancellationToken ct = default) => await Execute(actor, async (user, now) =>
    {
        var (airline, _) = await MemberAccess(user, airlineId, true, actor, now, ct);
        var roles = AccountRules.Roles(request.Roles);
        var member = await db.Memberships.Include(x => x.Roles).SingleOrDefaultAsync(x => x.Id == membershipId && x.AirlineId == airlineId, ct)
            ?? throw new IdentityException("membership_required", "Membership not found.", 404);
        if (member.Status != MembershipStatus.Active) throw new IdentityException("membership_inactive", "Only active memberships may change roles.");
        if (member.UserId == airline.OwnerUserId) throw new IdentityException("ownership_invariant", "Use ownership transfer to change the owner's access.", 409);
        var before = string.Join(',', member.Roles.Select(x => x.Role));
        foreach (var old in member.Roles.Where(x => !roles.Contains(x.Role)).ToArray()) { db.MembershipRoles.Remove(old); member.Roles.Remove(old); }
        foreach (var role in roles.Where(r => member.Roles.All(x => x.Role != r))) member.Roles.Add(new() { MembershipId = member.Id, AirlineId = airlineId, Role = role });
        Audit(user.Id, airlineId, "membership.roles_changed", member.Id, $"{before} -> {string.Join(',', roles)}", now);
        return true;
    }, ct);

    public async Task TransferOwnershipAsync(ActorIdentity actor, Guid airlineId, TransferOwnershipRequest request, CancellationToken ct = default) => await Execute(actor, async (user, now) =>
    {
        var (airline, oldMember) = await MemberAccess(user, airlineId, true, actor, now, ct);
        if (airline.OwnerUserId != user.Id) throw new IdentityException("owner_required", "Only the current owner can transfer ownership.");
        if (request.NewOwnerUserId == user.Id) return true;
        var target = await db.Memberships.SingleOrDefaultAsync(x => x.AirlineId == airlineId && x.UserId == request.NewOwnerUserId && x.Status == MembershipStatus.Active, ct);
        var targetUser = await db.Users.SingleOrDefaultAsync(x => x.Id == request.NewOwnerUserId && x.Status == AccountStatus.Active && x.EmailVerified, ct);
        if (target is null || targetUser is null) throw new IdentityException("active_member_required", "The new owner must be an active member with a verified account.");
        if (EffectivePlan(airline, now) == AirlinePlan.Community && await HasCommunityAirline(request.NewOwnerUserId, now, ct))
            throw new IdentityException("plan_limit_reached", "The selected member already owns a Community airline.", 409);
        if (oldMember.Roles.All(x => x.Role != AirlineRole.Administrator)) oldMember.Roles.Add(new() { AirlineId = airlineId, MembershipId = oldMember.Id, Role = AirlineRole.Administrator });
        airline.OwnerUserId = request.NewOwnerUserId;
        Audit(user.Id, airlineId, "airline.ownership_transferred", airlineId, $"{user.Id} -> {request.NewOwnerUserId}", now);
        return true;
    }, ct);

    // Read: any active member. Operations: dispatchers and up, no security check (schedules, fleet).
    // Admin: administrators and owners with a recent MFA proof (people, invitations, ownership, level).
    private enum Access { Read, Operations, Admin }

    private Task<(VirtualAirline Airline, Membership Member)> MemberAccess(UserAccount user, Guid airlineId, bool manage, ActorIdentity actor, DateTimeOffset now, CancellationToken ct)
        => MemberAccess(user, airlineId, manage ? Access.Admin : Access.Read, actor, now, ct);

    private async Task<(VirtualAirline Airline, Membership Member)> MemberAccess(UserAccount user, Guid airlineId, Access access, ActorIdentity actor, DateTimeOffset now, CancellationToken ct)
    {
        var member = await db.Memberships.Include(x => x.Roles).SingleOrDefaultAsync(x => x.AirlineId == airlineId && x.UserId == user.Id && x.Status == MembershipStatus.Active, ct);
        var airline = await db.Airlines.SingleOrDefaultAsync(x => x.Id == airlineId && x.Status == "active", ct);
        if (member is null || airline is null) throw new IdentityException("membership_required", "An active airline membership is required.");
        var roles = Roles(airline, member);
        switch (access)
        {
            case Access.Operations when !roles.Contains(AirlineRole.Owner) && !roles.Contains(AirlineRole.Administrator) && !roles.Contains(AirlineRole.Dispatcher):
                throw new IdentityException("dispatcher_required", "A dispatcher or administrator role is required.");
            case Access.Admin:
                if (!roles.Contains(AirlineRole.Owner) && !roles.Contains(AirlineRole.Administrator))
                    throw new IdentityException("administrator_required", "An airline administrator is required.");
                AccountRules.RequireRecentMfa(actor, now);
                break;
        }
        return (airline, member);
    }

    public Task<RouteResponse[]> ListRoutesAsync(ActorIdentity actor, Guid airlineId, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        await MemberAccess(user, airlineId, Access.Read, actor, now, ct);
        var routes = await db.Routes.Where(x => x.AirlineId == airlineId).OrderBy(x => x.FlightNumber).ThenBy(x => x.Origin).ToArrayAsync(ct);
        return routes.Select(RouteView).ToArray();
    }, ct);

    public Task<RouteResponse> SaveRouteAsync(ActorIdentity actor, Guid airlineId, Guid? routeId, RouteRequest request, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        await MemberAccess(user, airlineId, Access.Operations, actor, now, ct);
        var clean = AccountRules.Route(request);
        var route = routeId is { } id
            ? await db.Routes.SingleOrDefaultAsync(x => x.Id == id && x.AirlineId == airlineId, ct) ?? throw new IdentityException("route_not_found", "Route not found.", 404)
            : null;
        var duplicate = await db.Routes.AnyAsync(x => x.AirlineId == airlineId && x.FlightNumber == clean.FlightNumber && x.Origin == clean.Origin && x.Destination == clean.Destination && x.Id != routeId, ct);
        if (duplicate) throw new IdentityException("route_exists", $"{clean.FlightNumber} {clean.Origin}-{clean.Destination} already exists.", 409);
        if (route is null) { route = new() { Id = Guid.NewGuid(), AirlineId = airlineId, CreatedAt = now }; db.Routes.Add(route); }
        Apply(route, clean, now);
        Audit(user.Id, airlineId, routeId is null ? "route.created" : "route.updated", route.Id, $"{clean.FlightNumber} {clean.Origin}-{clean.Destination}", now);
        return RouteView(route);
    }, ct);

    public async Task DeleteRouteAsync(ActorIdentity actor, Guid airlineId, Guid routeId, CancellationToken ct = default) => await Execute(actor, async (user, now) =>
    {
        await MemberAccess(user, airlineId, Access.Operations, actor, now, ct);
        var route = await db.Routes.SingleOrDefaultAsync(x => x.Id == routeId && x.AirlineId == airlineId, ct) ?? throw new IdentityException("route_not_found", "Route not found.", 404);
        db.Routes.Remove(route);
        Audit(user.Id, airlineId, "route.deleted", routeId, $"{route.FlightNumber} {route.Origin}-{route.Destination}", now);
        return true;
    }, ct);

    // CSV columns: flight,origin,destination,departure_utc,block_minutes,days,aircraft_type,notes. A header row
    // is optional. Existing flight+origin+destination rows are updated, others created; bad rows are reported.
    public Task<ScheduleImportResult> ImportRoutesAsync(ActorIdentity actor, Guid airlineId, ScheduleImportRequest request, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        await MemberAccess(user, airlineId, Access.Operations, actor, now, ct);
        var lines = (request.Csv ?? "").Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length > 0).ToArray();
        if (lines.Length == 0) throw new IdentityException("invalid_route", "Paste at least one route.", 400);
        if (lines.Length > 2000) throw new IdentityException("invalid_route", "Import at most 2,000 routes at a time.", 400);
        var existing = await db.Routes.Where(x => x.AirlineId == airlineId).ToListAsync(ct);
        int created = 0, updated = 0; var errors = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var cells = lines[i].Split(',').Select(c => c.Trim().Trim('"')).ToArray();
            if (i == 0 && cells.Length > 0 && cells[0].Equals("flight", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (cells.Length < 6) throw new IdentityException("invalid_route", "Expected flight,origin,destination,departure_utc,block_minutes,days[,aircraft_type,notes].", 400);
                if (!int.TryParse(cells[4], out var block)) throw new IdentityException("invalid_route", "Block minutes must be a whole number.", 400);
                var clean = AccountRules.Route(new(cells[0], cells[1], cells[2], cells[3], block, AccountRules.Days(cells[5]),
                    cells.Length > 6 ? cells[6] : "", cells.Length > 7 ? string.Join(",", cells.Skip(7)) : "", true));
                var route = existing.FirstOrDefault(x => x.FlightNumber == clean.FlightNumber && x.Origin == clean.Origin && x.Destination == clean.Destination);
                if (route is null) { route = new() { Id = Guid.NewGuid(), AirlineId = airlineId, CreatedAt = now }; db.Routes.Add(route); existing.Add(route); created++; }
                else updated++;
                Apply(route, clean, now);
            }
            catch (IdentityException ex) { if (errors.Count < 50) errors.Add($"Line {i + 1}: {ex.Message}"); }
        }
        if (created + updated > 0) Audit(user.Id, airlineId, "schedule.imported", airlineId, $"{created} created, {updated} updated, {errors.Count} skipped", now);
        return new ScheduleImportResult(created, updated, errors.Count, errors.ToArray());
    }, ct);

    public Task<AircraftResponse[]> ListFleetAsync(ActorIdentity actor, Guid airlineId, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        await MemberAccess(user, airlineId, Access.Read, actor, now, ct);
        var fleet = await db.Fleet.Where(x => x.AirlineId == airlineId).OrderBy(x => x.TypeIcao).ThenBy(x => x.Registration).ToArrayAsync(ct);
        return fleet.Select(AircraftView).ToArray();
    }, ct);

    public Task<AircraftResponse> SaveAircraftAsync(ActorIdentity actor, Guid airlineId, Guid? aircraftId, AircraftRequest request, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        await MemberAccess(user, airlineId, Access.Operations, actor, now, ct);
        var clean = AccountRules.Aircraft(request);
        var aircraft = aircraftId is { } id
            ? await db.Fleet.SingleOrDefaultAsync(x => x.Id == id && x.AirlineId == airlineId, ct) ?? throw new IdentityException("aircraft_not_found", "Aircraft not found.", 404)
            : null;
        if (await db.Fleet.AnyAsync(x => x.AirlineId == airlineId && x.Registration == clean.Registration && x.Id != aircraftId, ct))
            throw new IdentityException("aircraft_exists", $"{clean.Registration} is already in the fleet.", 409);
        if (aircraft is null) { aircraft = new() { Id = Guid.NewGuid(), AirlineId = airlineId, CreatedAt = now }; db.Fleet.Add(aircraft); }
        aircraft.Registration = clean.Registration; aircraft.TypeIcao = clean.TypeIcao; aircraft.Name = clean.Name;
        aircraft.HomeBase = clean.HomeBase; aircraft.Status = clean.Status; aircraft.Notes = clean.Notes ?? ""; aircraft.UpdatedAt = now;
        Audit(user.Id, airlineId, aircraftId is null ? "aircraft.added" : "aircraft.updated", aircraft.Id, $"{clean.Registration} {clean.TypeIcao} {clean.Status}", now);
        return AircraftView(aircraft);
    }, ct);

    public async Task DeleteAircraftAsync(ActorIdentity actor, Guid airlineId, Guid aircraftId, CancellationToken ct = default) => await Execute(actor, async (user, now) =>
    {
        await MemberAccess(user, airlineId, Access.Operations, actor, now, ct);
        var aircraft = await db.Fleet.SingleOrDefaultAsync(x => x.Id == aircraftId && x.AirlineId == airlineId, ct) ?? throw new IdentityException("aircraft_not_found", "Aircraft not found.", 404);
        db.Fleet.Remove(aircraft);
        Audit(user.Id, airlineId, "aircraft.removed", aircraftId, aircraft.Registration, now);
        return true;
    }, ct);

    private static void Apply(AirlineRoute route, RouteRequest clean, DateTimeOffset now)
    {
        route.FlightNumber = clean.FlightNumber; route.Origin = clean.Origin; route.Destination = clean.Destination;
        route.DepartureUtc = TimeOnly.ParseExact(clean.DepartureUtc, "HH:mm"); route.BlockMinutes = clean.BlockMinutes;
        route.DaysOfWeek = clean.DaysOfWeek; route.AircraftType = clean.AircraftType; route.Notes = clean.Notes ?? "";
        route.Active = clean.Active; route.UpdatedAt = now;
    }
    private static RouteResponse RouteView(AirlineRoute r) => new(r.Id, r.FlightNumber, r.Origin, r.Destination, r.DepartureUtc.ToString("HH:mm"),
        r.BlockMinutes, r.DaysOfWeek, r.AircraftType, r.Notes, r.Active, r.UpdatedAt);
    private static AircraftResponse AircraftView(AirlineAircraft a) => new(a.Id, a.Registration, a.TypeIcao, a.Name, a.HomeBase, a.Status, a.Notes, a.UpdatedAt);

    private static AirlineRole[] Roles(VirtualAirline airline, Membership member)
    {
        var roles = member.Roles.Select(x => x.Role).ToArray();
        if (roles.Length == 0 || roles.Any(x => !Enum.IsDefined(x) || x == AirlineRole.Owner))
            throw new IdentityException("invalid_membership_roles", "The stored membership roles require administrator review.");
        return (airline.OwnerUserId == member.UserId ? roles.Append(AirlineRole.Owner) : roles).Distinct().Order().ToArray();
    }

    private static bool IsCurrent(string status, DateTimeOffset? expires, DateTimeOffset now) => SubscriptionStatuses.IsCurrent(status, expires, now);
    private Task<bool> HasCommunityAirline(Guid userId, DateTimeOffset now, CancellationToken ct, Guid? excluding = null) => db.Airlines.AnyAsync(x =>
        x.OwnerUserId == userId && x.Status == "active" && x.Id != excluding && !(x.Plan == AirlinePlan.Pro &&
        (x.SubscriptionStatus == SubscriptionStatuses.Active || x.SubscriptionStatus == SubscriptionStatuses.Complimentary) &&
        (x.SubscriptionExpiresAt == null || x.SubscriptionExpiresAt > now)), ct);
    private async Task<UserProfileRecord> EnsureProfile(UserAccount user, CancellationToken ct)
    {
        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
        if (profile is null) { profile = new() { UserId = user.Id }; db.Profiles.Add(profile); }
        return profile;
    }
    private static UserProfile ProfileView(UserProfileRecord p) => new(p.SimBriefUsername, p.Callsign, p.HomeBaseIcao, p.WeightUnit, p.AltitudeUnit,
        p.LandingDistanceUnit, p.PreferredWorkspace, p.TimeZone, p.AvatarInitials, p.LastSeenAt, p.LastSeenVersion, p.UpdatedAt);
    private static AirlinePlan EffectivePlan(VirtualAirline airline, DateTimeOffset now) => airline.Plan == AirlinePlan.Pro &&
        IsCurrent(airline.SubscriptionStatus, airline.SubscriptionExpiresAt, now) ? AirlinePlan.Pro : AirlinePlan.Community;
    private static AirlineWorkspace Workspace(VirtualAirline airline, Membership member, UserAccount user, DateTimeOffset now)
    {
        var roles = Roles(airline, member);
        var plan = EffectivePlan(airline, now);
        return new(airline.Id, airline.Slug, airline.Name, airline.Callsign, plan, airline.SubscriptionStatus, member.Status,
            roles, airline.FounderUserId == user.Id, Capabilities.ForMembership(member.Status, roles));
    }

    private static InvitationResponse InvitationView(Invitation invitation, DateTimeOffset now) => new(invitation.Id, invitation.Email,
        AccountRules.Roles(invitation.Roles.Select(x => (AirlineRole)x).ToArray()), invitation.ExpiresAt,
        invitation.Status == "pending" && invitation.ExpiresAt <= now ? "expired" : invitation.Status);
    private static IdentityException InvalidInvitation() => new("invitation_invalid", "This invitation is unavailable.", 404);
    private void Audit(Guid actor, Guid? airline, string action, Guid target, string details, DateTimeOffset now) => db.AuditEvents.Add(new()
        { Id = Guid.NewGuid(), ActorUserId = actor, AirlineId = airline, Action = action, TargetId = target, Details = details, CreatedAt = now });
}
