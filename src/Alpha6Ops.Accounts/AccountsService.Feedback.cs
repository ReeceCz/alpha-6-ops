using Alpha6Ops.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alpha6Ops.Accounts;

public sealed record FeedbackView(Guid Id, FeedbackCategory Category, string Message, string FlightNumber, DateOnly? FlownOn,
    string AppVersion, FeedbackStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? HandledAt, string? AttachmentName);
public sealed record StaffFeedbackView(FeedbackView Report, Guid UserId, string ReporterName, string ReporterEmail, string ReporterCallsign, int? AttachmentBytes);

// Problem reports. Pilots write and read their own; allowlisted staff with a fresh MFA read all of them.
public sealed partial class AccountsService
{
    public Task<FeedbackView> SubmitFeedbackAsync(ActorIdentity actor, FeedbackRequest request, FeedbackFile? file, CancellationToken ct = default) =>
        Execute(actor, async (user, now) =>
        {
            var clean = FeedbackRules.Request(request, DateOnly.FromDateTime(now.UtcDateTime));
            var attachment = file is null ? null : FeedbackRules.Attachment(file);
            var since = now.AddHours(-1);
            if (await db.Feedback.CountAsync(x => x.UserId == user.Id && x.CreatedAt > since, ct) >= FeedbackRules.PerHour)
                throw new IdentityException("feedback_rate_limited", "You've sent several reports in the last hour. Try again a little later.", 429);
            if (attachment is not null && await db.FeedbackAttachments.SumAsync(x => (long)x.Bytes.Length, ct) + attachment.Bytes.Length > FeedbackRules.StorageCapBytes)
                throw new IdentityException("feedback_storage_full", "Attachments are full for now. Send the report without the file and we'll ask for it.", 507);
            var version = await db.Profiles.Where(x => x.UserId == user.Id).Select(x => x.LastSeenVersion).FirstOrDefaultAsync(ct) ?? "";
            var row = new Feedback { Id = Guid.NewGuid(), UserId = user.Id, Category = clean.Category, Message = clean.Message!,
                FlightNumber = clean.FlightNumber!, FlownOn = clean.FlownOn, AppVersion = version, Status = FeedbackStatus.New, CreatedAt = now };
            db.Feedback.Add(row);
            if (attachment is not null)
                db.FeedbackAttachments.Add(new() { FeedbackId = row.Id, FileName = attachment.FileName, ContentType = attachment.ContentType, Bytes = attachment.Bytes, Sha256 = attachment.Sha256 });
            Audit(user.Id, null, "feedback.submitted", row.Id, clean.Category.ToString(), now);
            return View(row, attachment?.FileName);
        }, ct);

    public Task<FeedbackView[]> ListMyFeedbackAsync(ActorIdentity actor, CancellationToken ct = default) => Execute(actor, async (user, _) =>
        (await Reports(db.Feedback.Where(x => x.UserId == user.Id), ct)).Select(x => x.Report).ToArray(), ct);

    public Task<StaffFeedbackView[]> ListFeedbackAsync(ActorIdentity actor, StaffAccess staff, FeedbackStatus? status, CancellationToken ct = default) =>
        Execute(actor, async (_, now) =>
        {
            RequireStaff(actor, staff, now);
            return await Reports(status is { } s ? db.Feedback.Where(x => x.Status == s) : db.Feedback, ct);
        }, ct);

    public Task<StaffFeedbackView> GetFeedbackAsync(ActorIdentity actor, StaffAccess staff, Guid id, CancellationToken ct = default) =>
        Execute(actor, async (_, now) => { RequireStaff(actor, staff, now); return await Report(id, ct); }, ct);

    public Task<CleanAttachment> GetFeedbackAttachmentAsync(ActorIdentity actor, StaffAccess staff, Guid id, CancellationToken ct = default) =>
        Execute(actor, async (_, now) =>
        {
            RequireStaff(actor, staff, now);
            var a = await db.FeedbackAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.FeedbackId == id, ct) ?? throw NotFound();
            return new CleanAttachment(a.FileName, a.ContentType, a.Bytes, a.Sha256);
        }, ct);

    public Task<StaffFeedbackView> SetFeedbackStatusAsync(ActorIdentity actor, StaffAccess staff, Guid id, FeedbackStatus status, CancellationToken ct = default) =>
        Execute(actor, async (user, now) =>
        {
            RequireStaff(actor, staff, now);
            if (!Enum.IsDefined(status)) throw new IdentityException("invalid_feedback", "Choose New or Handled.", 400);
            var row = await db.Feedback.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
            if (row.Status != status)
            {
                row.Status = status;
                row.HandledAt = status == FeedbackStatus.Handled ? now : null;
                row.HandledByUserId = status == FeedbackStatus.Handled ? user.Id : null;
                Audit(user.Id, null, status == FeedbackStatus.Handled ? "feedback.handled" : "feedback.reopened", row.Id, "", now);
                await db.SaveChangesAsync(ct);
            }
            return await Report(id, ct);
        }, ct);

    // Staff pages answer "not found" to everyone else, so their existence isn't revealed.
    private static void RequireStaff(ActorIdentity actor, StaffAccess staff, DateTimeOffset now)
    {
        if (!staff.IsStaff(actor)) throw NotFound();
        AccountRules.RequireRecentMfa(actor, now);
    }
    private static IdentityException NotFound() => new("not_found", "That page doesn't exist.", 404);

    private async Task<StaffFeedbackView> Report(Guid id, CancellationToken ct) =>
        (await Reports(db.Feedback.Where(x => x.Id == id), ct)).SingleOrDefault() ?? throw NotFound();

    private async Task<StaffFeedbackView[]> Reports(IQueryable<Feedback> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(500)
            .Select(x => new
            {
                Row = x,
                Attachment = db.FeedbackAttachments.Where(a => a.FeedbackId == x.Id).Select(a => new { a.FileName, Size = a.Bytes.Length }).FirstOrDefault(),
                User = db.Users.Where(u => u.Id == x.UserId).Select(u => new { u.DisplayName, u.Email }).First(),
                Callsign = db.Profiles.Where(p => p.UserId == x.UserId).Select(p => p.Callsign).FirstOrDefault()
            }).ToListAsync(ct);
        return rows.Select(x => new StaffFeedbackView(View(x.Row, x.Attachment?.FileName), x.Row.UserId, x.User.DisplayName, x.User.Email,
            x.Callsign ?? "", x.Attachment?.Size)).ToArray();
    }

    private static FeedbackView View(Feedback x, string? attachmentName) =>
        new(x.Id, x.Category, x.Message, x.FlightNumber, x.FlownOn, x.AppVersion, x.Status, x.CreatedAt, x.HandledAt, attachmentName);
}
