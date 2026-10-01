using Alpha6Ops.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alpha6Ops.Accounts;

public sealed record OwnedAirline(Guid Id, string Name);
public sealed record DeletionStatus(AccountStatus Status, DateTimeOffset? RequestedAt, DateTimeOffset? EraseAfter, OwnedAirline[] OwnedAirlines);
public sealed record PendingIdentityRemoval(Guid UserId, string Issuer, string Subject);

public sealed record ExportedAccount(Guid Id, string DisplayName, string Email, bool EmailVerified, string Status, string Plan,
    DateTimeOffset CreatedAt, DateTimeOffset? DeletionRequestedAt);
public sealed record ExportedProfile(string Callsign, string HomeBaseIcao, string SimBriefUsername, string WeightUnit, string AltitudeUnit,
    string LandingDistanceUnit, string PreferredWorkspace, string TimeZone, string AvatarInitials, DateTimeOffset? LastSeenAt, string LastSeenVersion);
public sealed record ExportedMembership(Guid AirlineId, string Name, string Callsign, string Status, string[] Roles, DateTimeOffset JoinedAt);
public sealed record ExportedActivity(string Action, Guid? AirlineId, Guid TargetId, string Details, DateTimeOffset At);
public sealed record AccountExport(DateTimeOffset ExportedAt, ExportedAccount Account, ExportedProfile? Profile, ExportedMembership[] Airlines,
    FlightLogEntry[] Logbook, ExportedActivity[] Activity);

// Self-service export and deletion. Deletion locks the account at once, then after the grace period erases the
// person's data and leaves an anonymised tombstone, because airlines, invitations and audit history reference it.
public sealed partial class AccountsService
{
    public const string DeletedDisplayName = "Deleted pilot";

    public Task<(AccountExport Data, MediaBlob? Avatar)> ExportAsync(ActorIdentity actor, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
        var memberships = await db.Memberships.AsNoTracking().Include(x => x.Roles).Where(x => x.UserId == user.Id)
            .Join(db.Airlines.AsNoTracking(), m => m.AirlineId, a => a.Id, (m, a) => new { m, a }).ToListAsync(ct);
        var flights = await db.Flights.AsNoTracking().Where(x => x.UserId == user.Id).OrderBy(x => x.DepartureUtc).ToListAsync(ct);
        var activity = await db.AuditEvents.AsNoTracking().Where(x => x.ActorUserId == user.Id).OrderBy(x => x.CreatedAt).ToListAsync(ct);
        var avatar = await db.Media.AsNoTracking().SingleOrDefaultAsync(x => x.Kind == MediaKinds.Avatar && x.OwnerId == user.Id, ct);
        var data = new AccountExport(now,
            new(user.Id, user.DisplayName, user.Email, user.EmailVerified, user.Status.ToString(), user.Plan.ToString(), user.CreatedAt, user.DeletionRequestedAt),
            profile is null ? null : new(profile.Callsign, profile.HomeBaseIcao, profile.SimBriefUsername, profile.WeightUnit, profile.AltitudeUnit,
                profile.LandingDistanceUnit, profile.PreferredWorkspace, profile.TimeZone, profile.AvatarInitials, profile.LastSeenAt, profile.LastSeenVersion),
            memberships.Select(x => new ExportedMembership(x.a.Id, x.a.Name, x.a.Callsign, x.m.Status.ToString(),
                x.m.Roles.Select(r => r.Role.ToString()).Concat(x.a.OwnerUserId == user.Id ? [nameof(AirlineRole.Owner)] : Array.Empty<string>()).ToArray(), x.m.JoinedAt)).ToArray(),
            flights.Select(FlightView).ToArray(),
            activity.Select(x => new ExportedActivity(x.Action, x.AirlineId, x.TargetId, x.Details, x.CreatedAt)).ToArray());
        return (data, avatar);
    }, allowPendingDeletion: true, ct);

    public Task<DeletionStatus> GetDeletionStatusAsync(ActorIdentity actor, CancellationToken ct = default) =>
        Execute(actor, async (user, _) => await Status(user, ct), allowPendingDeletion: true, ct);

    public Task<DeletionStatus> RequestDeletionAsync(ActorIdentity actor, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        if (user.Status == AccountStatus.PendingDeletion) return await Status(user, ct);
        AccountRules.RequireRecentAuthentication(actor, now);
        var owned = await OwnedAirlines(user.Id, ct);
        if (owned.Length > 0)
            throw new IdentityException("ownership_transfer_required",
                $"Transfer ownership of {string.Join(", ", owned.Select(x => x.Name))} to another crew member before deleting your account.", 409);
        user.Status = AccountStatus.PendingDeletion;
        user.DeletionRequestedAt = now;
        Audit(user.Id, null, "account.deletion_requested", user.Id, "", now);
        return await Status(user, ct);
    }, allowPendingDeletion: true, ct);

    public Task<DeletionStatus> CancelDeletionAsync(ActorIdentity actor, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        if (user.Status == AccountStatus.PendingDeletion)
        {
            user.Status = AccountStatus.Active;
            user.DeletionRequestedAt = null;
            Audit(user.Id, null, "account.deletion_cancelled", user.Id, "", now);
        }
        return await Status(user, ct);
    }, allowPendingDeletion: true, ct);

    /// <summary>Erases every account whose grace period has ended. Runs without an actor, from the background job.</summary>
    public Task<int> PurgeDueDeletionsAsync(CancellationToken ct = default) => RunSystem(async now =>
    {
        var cutoff = now.AddDays(-AccountRules.DeletionGraceDays);
        var due = await db.Users.Where(x => x.Status == AccountStatus.PendingDeletion && x.DeletionRequestedAt <= cutoff).ToListAsync(ct);
        foreach (var user in due) await Erase(user, now, ct);
        return due.Count;
    }, ct);

    /// <summary>Deleted accounts whose sign-in identity still exists at the identity provider.</summary>
    public Task<PendingIdentityRemoval[]> PendingIdentityRemovalsAsync(CancellationToken ct = default) =>
        db.Users.AsNoTracking().Where(x => x.Status == AccountStatus.Deleted && x.IdentityRemovedAt == null)
            .OrderBy(x => x.DeletedAt).Select(x => new PendingIdentityRemoval(x.Id, x.Issuer, x.Subject)).ToArrayAsync(ct);

    /// <summary>Records that the identity provider no longer has this sign-in, and drops the last identifier.</summary>
    public Task MarkIdentityRemovedAsync(Guid userId, CancellationToken ct = default) => RunSystem(async now =>
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.Status == AccountStatus.Deleted && x.IdentityRemovedAt == null, ct);
        if (user is null) return 0;
        user.IdentityRemovedAt = now;
        user.Subject = $"deleted:{user.Id:N}";
        return 1;
    }, ct);

    private async Task Erase(UserAccount user, DateTimeOffset now, CancellationToken ct)
    {
        var id = user.Id;
        var email = user.Email;
        await db.Flights.Where(x => x.UserId == id).ExecuteDeleteAsync(ct);
        await db.Profiles.Where(x => x.UserId == id).ExecuteDeleteAsync(ct);
        await db.Media.Where(x => x.Kind == MediaKinds.Avatar && x.OwnerId == id).ExecuteDeleteAsync(ct);
        // Personal history goes; airline history stays and now points at the anonymous tombstone.
        await db.AuditEvents.Where(x => x.ActorUserId == id && x.AirlineId == null).ExecuteDeleteAsync(ct);
        // Requests block owners, so these are ordinary memberships; roles cascade in the database.
        await db.Memberships.Where(x => x.UserId == id && !db.Airlines.Any(a => a.Id == x.AirlineId && a.OwnerUserId == id)).ExecuteDeleteAsync(ct);
        await db.Invitations.Where(x => x.AcceptedByUserId == id).ExecuteUpdateAsync(x => x.SetProperty(i => i.Email, ""), ct);
        if (email.Length > 0)
            await db.Invitations.Where(x => x.Status == "pending" && x.Email == email)
                .ExecuteUpdateAsync(x => x.SetProperty(i => i.Email, "").SetProperty(i => i.Status, "revoked"), ct);
        user.DisplayName = DeletedDisplayName; user.Email = ""; user.EmailVerified = false;
        user.Plan = PersonalPlan.Free; user.LastAirlineId = null;
        user.Status = AccountStatus.Deleted; user.DeletedAt = now;
        Audit(id, null, "account.deleted", id, "", now);
    }

    private async Task<DeletionStatus> Status(UserAccount user, CancellationToken ct) => new(user.Status, user.DeletionRequestedAt,
        user.DeletionRequestedAt?.AddDays(AccountRules.DeletionGraceDays), user.Status == AccountStatus.Active ? await OwnedAirlines(user.Id, ct) : []);

    private Task<OwnedAirline[]> OwnedAirlines(Guid userId, CancellationToken ct) => db.Airlines.AsNoTracking()
        .Where(x => x.OwnerUserId == userId && x.Status == "active").OrderBy(x => x.Name).Select(x => new OwnedAirline(x.Id, x.Name)).ToArrayAsync(ct);

    // The same transaction and lock as Execute, for work that has no signed-in actor.
    private async Task<T> RunSystem<T>(Func<DateTimeOffset, Task<T>> action, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LockId})", ct);
        var result = await action(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }
}
