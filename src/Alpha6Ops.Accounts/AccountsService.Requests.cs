using Alpha6Ops.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alpha6Ops.Accounts;

// Invitation requests from people who aren't testers yet. Submitting never says whether an email is already known:
// a duplicate, an invited email or an existing account is accepted and quietly not queued.
public sealed partial class AccountsService
{
    public Task<bool> SubmitInviteRequestAsync(InviteRequestForm form, CancellationToken ct = default) => RunSystem(async now =>
    {
        var clean = InviteRequestRules.Request(form);
        var email = clean.Email!;
        if (await db.InviteRequests.AnyAsync(x => x.Email == email, ct) || await db.TesterInvites.AnyAsync(x => x.Email == email, ct)
            || await db.Users.AnyAsync(x => x.Email == email && x.Status != AccountStatus.Deleted, ct))
            return false;
        if (await db.InviteRequests.CountAsync(ct) >= InviteRequestRules.MaxPending)
            throw new IdentityException("invite_requests_full", "We have more requests than we can answer right now. Please try again in a few days.", 503);
        db.InviteRequests.Add(new() { Id = Guid.NewGuid(), Email = email, Name = clean.Name!, Note = clean.Note!, CreatedAt = now });
        return true;
    }, ct);

    public Task<InviteRequestView[]> ListInviteRequestsAsync(ActorIdentity actor, StaffAccess staff, CancellationToken ct = default) =>
        Execute(actor, async (_, now) =>
        {
            RequireStaff(actor, staff, now);
            return await db.InviteRequests.AsNoTracking().OrderBy(x => x.CreatedAt)
                .Select(x => new InviteRequestView(x.Id, x.Email, x.Name, x.Note, x.CreatedAt)).ToArrayAsync(ct);
        }, ct);

    public async Task<TesterInviteView> ApproveInviteRequestAsync(ActorIdentity actor, StaffAccess staff, Guid id, CancellationToken ct = default)
    {
        var request = await Execute(actor, async (_, now) =>
        {
            RequireStaff(actor, staff, now);
            return await db.InviteRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        }, ct);
        var detail = string.Join(" · ", new[] { request.Name, request.Note.ReplaceLineEndings(" ") }.Where(x => x.Length > 0));
        var note = ("Requested: " + detail).TrimEnd(' ', ':');
        var invite = await AddTesterInviteAsync(actor, staff, request.Email, note.Length > 200 ? note[..199] + "…" : note, ct);
        await DeclineInviteRequestAsync(actor, staff, id, ct);
        return invite;
    }

    public Task<bool> DeclineInviteRequestAsync(ActorIdentity actor, StaffAccess staff, Guid id, CancellationToken ct = default) =>
        Execute(actor, async (_, now) =>
        {
            RequireStaff(actor, staff, now);
            return await db.InviteRequests.Where(x => x.Id == id).ExecuteDeleteAsync(ct) > 0;
        }, ct);

    /// <summary>Deletes requests nobody answered within the retention period. Runs from the hourly background job.</summary>
    public Task<int> PurgeStaleInviteRequestsAsync(CancellationToken ct = default) => RunSystem(async now =>
    {
        var cutoff = now.AddDays(-InviteRequestRules.RetentionDays);
        return await db.InviteRequests.Where(x => x.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }, ct);
}
