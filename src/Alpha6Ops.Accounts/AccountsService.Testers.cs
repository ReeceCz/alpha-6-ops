using Alpha6Ops.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alpha6Ops.Accounts;

public sealed record TesterInviteView(string Email, string Note, string InvitedBy, DateTimeOffset CreatedAt, bool HasAccount);

// Testers invited from the staff page. AccountAdmission still decides with the configured list first; Execute
// falls back to this table when that list refuses an email.
public sealed partial class AccountsService
{
    public Task<TesterInviteView[]> ListTesterInvitesAsync(ActorIdentity actor, StaffAccess staff, CancellationToken ct = default) =>
        Execute(actor, async (_, now) => { RequireStaff(actor, staff, now); return await TesterInvites(null, ct); }, ct);

    public Task<TesterInviteView> AddTesterInviteAsync(ActorIdentity actor, StaffAccess staff, string email, string? note, CancellationToken ct = default) =>
        Execute(actor, async (user, now) =>
        {
            RequireStaff(actor, staff, now);
            var clean = AccountRules.Email(email);
            var text = (note ?? "").Trim();
            if (text.Length > 200 || text.Any(char.IsControl)) throw new IdentityException("invalid_invite", "Keep the note to one line of up to 200 characters.", 400);
            var row = await db.TesterInvites.SingleOrDefaultAsync(x => x.Email == clean, ct);
            if (row is null) { row = new() { Email = clean, InvitedByUserId = user.Id, CreatedAt = now }; db.TesterInvites.Add(row); }
            row.Note = text;
            Audit(user.Id, null, "tester.invited", user.Id, clean, now);
            await db.SaveChangesAsync(ct);
            return (await TesterInvites(clean, ct)).Single();
        }, ct);

    public Task<bool> RemoveTesterInviteAsync(ActorIdentity actor, StaffAccess staff, string email, CancellationToken ct = default) =>
        Execute(actor, async (user, now) =>
        {
            RequireStaff(actor, staff, now);
            var clean = AccountRules.Email(email);
            var removed = await db.TesterInvites.Where(x => x.Email == clean).ExecuteDeleteAsync(ct) > 0;
            if (removed) Audit(user.Id, null, "tester.uninvited", user.Id, clean, now);
            return removed;
        }, ct);

    private async Task<TesterInviteView[]> TesterInvites(string? email, CancellationToken ct) =>
        await db.TesterInvites.AsNoTracking().Where(x => email == null || x.Email == email).OrderByDescending(x => x.CreatedAt)
            .Select(x => new TesterInviteView(x.Email, x.Note,
                db.Users.Where(u => u.Id == x.InvitedByUserId).Select(u => u.DisplayName).First(), x.CreatedAt,
                db.Users.Any(u => u.Email == x.Email && u.Status != AccountStatus.Deleted)))
            .ToArrayAsync(ct);

    private Task<bool> IsInvitedTester(string email, CancellationToken ct) => db.TesterInvites.AnyAsync(x => x.Email == email, ct);
}
