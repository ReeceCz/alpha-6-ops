using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Staff;

public class ReportModel(AccountsService accounts, ServerSettings settings, StaffAccess staff) : StaffPage(accounts, settings, staff)
{
    public StaffFeedbackView? Report { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try { Report = await Accounts.GetFeedbackAsync(Actor, Staff, id, ct); return Page(); }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }

    public async Task<IActionResult> OnGetAttachmentAsync(Guid id, CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try
        {
            var file = await Accounts.GetFeedbackAttachmentAsync(Actor, Staff, id, ct);
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(file.Bytes, file.ContentType, file.FileName); // FileContentResult with a name => Content-Disposition: attachment
        }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }

    public async Task<IActionResult> OnPostReplyAsync(Guid id, string? body, bool markHandled, CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try { await Accounts.ReplyToFeedbackAsync(Actor, Staff, id, body, markHandled, ct); return Redirect($"/Staff/Feedback/{id}"); }
        catch (IdentityException ex)
        {
            if (ex.Code == "invalid_reply") Report = await Accounts.GetFeedbackAsync(Actor, Staff, id, ct);
            return StaffFailure(ex);
        }
    }

    public async Task<IActionResult> OnPostStatusAsync(Guid id, FeedbackStatus status, CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try { await Accounts.SetFeedbackStatusAsync(Actor, Staff, id, status, ct); return Redirect("/Staff/Feedback"); }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }
}
