using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Staff;

public class FeedbackInboxModel(AccountsService accounts, ServerSettings settings, StaffAccess staff) : StaffPage(accounts, settings, staff)
{
    public StaffFeedbackView[] Reports { get; private set; } = [];
    public string Show { get; private set; } = "new";

    public async Task<IActionResult> OnGetAsync(string? show, CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        Show = show is "handled" or "all" ? show : "new";
        FeedbackStatus? filter = Show switch { "handled" => FeedbackStatus.Handled, "all" => null, _ => FeedbackStatus.New };
        try { Reports = await Accounts.ListFeedbackAsync(Actor, Staff, filter, ct); return Page(); }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }
}
