using Alpha6Ops.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alpha6Ops.Accounts;

/// <summary>Counts every member may read. Nothing here names a person or an email address.</summary>
public sealed record AirlineOverview(AirlineWorkspace Airline, int Routes, int ActiveRoutes, int Aircraft, int ActiveAircraft,
    int Crew, int PendingInvitations);

/// <summary>An invitation waiting for the signed-in pilot's verified email.</summary>
public sealed record PendingInvitation(Guid Id, string AirlineName, string Callsign, AirlineRole[] Roles, DateTimeOffset ExpiresAt, string InvitedBy);

// The airline console's overview and the invitee's side of invitations.
public sealed partial class AccountsService
{
    public Task<AirlineOverview> GetAirlineOverviewAsync(ActorIdentity actor, Guid airlineId, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        var (airline, member) = await MemberAccess(user, airlineId, Access.Read, actor, now, ct);
        var routes = db.Routes.Where(x => x.AirlineId == airlineId);
        var fleet = db.Fleet.Where(x => x.AirlineId == airlineId);
        return new AirlineOverview(
            Workspace(airline, member, user, now, await MediaVersion(MediaKinds.AirlineLogo, airline.Id, ct)),
            await routes.CountAsync(ct), await routes.CountAsync(x => x.Active, ct),
            await fleet.CountAsync(ct), await fleet.CountAsync(x => x.Status == "active", ct),
            await db.Memberships.CountAsync(x => x.AirlineId == airlineId && x.Status == MembershipStatus.Active, ct),
            await db.Invitations.CountAsync(x => x.AirlineId == airlineId && x.Status == "pending" && x.ExpiresAt > now, ct));
    }, ct);

    /// <summary>Pending invitations for the actor's email. Empty until the email is verified, because accepting needs that.</summary>
    public Task<PendingInvitation[]> ListInvitationsForMeAsync(ActorIdentity actor, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        if (!actor.EmailVerified) return Array.Empty<PendingInvitation>();
        var rows = await (from i in db.Invitations
                          join a in db.Airlines on i.AirlineId equals a.Id
                          join u in db.Users on i.IssuedByUserId equals u.Id into issuers
                          from issuer in issuers.DefaultIfEmpty()
                          where i.Email == user.Email && i.Status == "pending" && i.ExpiresAt > now && a.Status == "active"
                          orderby i.CreatedAt descending
                          select new { i.Id, a.Name, a.Callsign, i.Roles, i.ExpiresAt, Issuer = issuer == null ? null : issuer.DisplayName }).Take(20).ToArrayAsync(ct);
        return rows.Select(x => new PendingInvitation(x.Id, x.Name, x.Callsign, x.Roles.Select(r => (AirlineRole)r).ToArray(), x.ExpiresAt, x.Issuer ?? "A former member")).ToArray();
    }, ct);

    /// <summary>Accepts an invitation listed for the actor. Same rules as accepting by code; an unknown or foreign ID is just unavailable.</summary>
    public Task<AirlineWorkspace> AcceptInvitationByIdAsync(ActorIdentity actor, Guid invitationId, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        if (!actor.EmailVerified) throw new IdentityException("email_verification_required", "Verify your email before accepting an invitation.");
        var invitation = await db.Invitations.SingleOrDefaultAsync(x => x.Id == invitationId && x.Email == user.Email, ct) ?? throw InvalidInvitation();
        return await AcceptAsync(actor, user, invitation, now, ct);
    }, ct);
}
