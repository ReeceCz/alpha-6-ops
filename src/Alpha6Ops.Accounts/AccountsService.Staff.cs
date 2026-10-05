using Alpha6Ops.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alpha6Ops.Accounts;

public sealed record VersionInUse(string Version, int Pilots);
public sealed record StaffOverview(int Invited, int Accounts, int DesktopSignedIn, int WithFlights, int Reported,
    int OpenReports, int NewAccountsThisWeek, VersionInUse[] Versions);

// The staff overview: counts only, from what the account store already holds.
public sealed partial class AccountsService
{
    public Task<StaffOverview> GetStaffOverviewAsync(ActorIdentity actor, StaffAccess staff, CancellationToken ct = default) =>
        Execute(actor, async (_, now) =>
        {
            RequireStaff(actor, staff, now);
            var active = db.Users.AsNoTracking().Where(x => x.Status == AccountStatus.Active);
            var weekAgo = now.AddDays(-7);
            var monthAgo = now.AddDays(-30);
            var versions = await db.Profiles.AsNoTracking()
                .Where(p => p.LastSeenAt >= monthAgo && active.Any(u => u.Id == p.UserId))
                .GroupBy(p => p.LastSeenVersion).Select(g => new VersionInUse(g.Key, g.Count())).ToListAsync(ct);
            return new StaffOverview(
                await db.TesterInvites.CountAsync(ct),
                await active.CountAsync(ct),
                await active.CountAsync(u => db.Profiles.Any(p => p.UserId == u.Id && p.LastSeenAt != null), ct),
                await active.CountAsync(u => db.Flights.Any(f => f.UserId == u.Id), ct),
                await active.CountAsync(u => db.Feedback.Any(f => f.UserId == u.Id), ct),
                await db.Feedback.CountAsync(x => x.Status == FeedbackStatus.New, ct),
                await active.CountAsync(u => u.CreatedAt >= weekAgo, ct),
                versions.OrderByDescending(v => ParseVersion(v.Version)).ToArray());
        }, ct);

    /// <summary>Throws not_found for non-staff and mfa_required without a recent MFA; for staff pages that read public data.</summary>
    public Task<bool> EnsureStaffAsync(ActorIdentity actor, StaffAccess staff, CancellationToken ct = default) =>
        Execute(actor, (_, now) => { RequireStaff(actor, staff, now); return Task.FromResult(true); }, ct);

    /// <summary>Orders x.y.z versions numerically; anything unparseable (including "") sorts last.</summary>
    public static Version ParseVersion(string value) => Version.TryParse(value, out var parsed) ? parsed : new Version(0, 0);
}
