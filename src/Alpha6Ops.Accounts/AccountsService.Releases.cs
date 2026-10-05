using Alpha6Ops.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alpha6Ops.Accounts;

// Release notes. Staff write them; the public pages read them without a signed-in actor.
public sealed partial class AccountsService
{
    public Task<ReleaseNoteView> SaveReleaseNoteAsync(ActorIdentity actor, StaffAccess staff, ReleaseNoteRequest request, CancellationToken ct = default) =>
        Execute(actor, async (user, now) =>
        {
            RequireStaff(actor, staff, now);
            var clean = ReleaseRules.Note(request);
            var row = await db.ReleaseNotes.SingleOrDefaultAsync(x => x.Version == clean.Version, ct);
            if (row is null) { row = new() { Version = clean.Version!, PublishedAt = now }; db.ReleaseNotes.Add(row); }
            row.Title = clean.Title!; row.Notes = clean.Notes!; row.AuthorUserId = user.Id; row.UpdatedAt = now;
            Audit(user.Id, null, "release.notes_saved", user.Id, clean.Version!, now);
            await db.SaveChangesAsync(ct);
            return (await ReleaseNotes(clean.Version, ct)).Single();
        }, ct);

    public Task<bool> DeleteReleaseNoteAsync(ActorIdentity actor, StaffAccess staff, string version, CancellationToken ct = default) =>
        Execute(actor, async (user, now) =>
        {
            RequireStaff(actor, staff, now);
            var removed = await db.ReleaseNotes.Where(x => x.Version == version).ExecuteDeleteAsync(ct) > 0;
            if (removed) Audit(user.Id, null, "release.notes_deleted", user.Id, version, now);
            return removed;
        }, ct);

    public Task<ReleaseNoteView[]> ListReleaseNotesAsync(CancellationToken ct = default) => ReleaseNotes(null, ct);

    public async Task<ReleaseNoteView?> GetReleaseNoteAsync(string version, CancellationToken ct = default) =>
        (await ReleaseNotes(version, ct)).SingleOrDefault();

    private async Task<ReleaseNoteView[]> ReleaseNotes(string? version, CancellationToken ct)
    {
        var rows = await db.ReleaseNotes.AsNoTracking().Where(x => version == null || x.Version == version)
            .Select(x => new ReleaseNoteView(x.Version, x.Title, x.Notes, x.PublishedAt, x.UpdatedAt,
                db.Users.Where(u => u.Id == x.AuthorUserId).Select(u => u.DisplayName).First()))
            .ToListAsync(ct);
        return rows.OrderByDescending(x => ParseVersion(x.Version)).ToArray();
    }
}
