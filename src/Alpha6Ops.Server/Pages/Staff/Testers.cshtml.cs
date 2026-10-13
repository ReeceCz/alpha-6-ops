using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Staff;

public class TestersModel(AccountsService accounts, ServerSettings settings, StaffAccess staff, Mailer mailer, Microsoft.Extensions.Options.IOptions<EmailSettings> email) : StaffPage(accounts, settings, staff)
{
    public TesterInviteView[] Invites { get; private set; } = [];
    public InviteRequestView[] Requests { get; private set; } = [];
    public bool EmailOn => email.Value.Enabled;
    // Also bound on GET, so an invitation refused for a stale security check comes back filled in after the check.
    [BindProperty(SupportsGet = true)] public string? Email { get; set; }
    [BindProperty(SupportsGet = true)] public string? Note { get; set; }
    /// <summary>When the current security check stops covering staff actions (UTC).</summary>
    public DateTimeOffset? CheckValidUntil => Actor.AuthenticatedAt?.AddMinutes(5);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try { Invites = await Accounts.ListTesterInvitesAsync(Actor, Staff, ct); Requests = await Accounts.ListInviteRequestsAsync(Actor, Staff, ct); return Page(); }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try
        {
            var address = AccountRules.Email(Email ?? "");
            var existed = (await Accounts.ListTesterInvitesAsync(Actor, Staff, ct)).Any(x => x.Email == address);
            var invite = await Accounts.AddTesterInviteAsync(Actor, Staff, address, Note, ct);
            if (!existed && !invite.HasAccount) mailer.TesterInvited(invite.Email);
            return RedirectToPage();
        }
        catch (IdentityException ex)
        {
            if (ex.Code == "mfa_required")
                StepUpReturn = $"/Staff/Testers?email={Uri.EscapeDataString(Email ?? "")}&note={Uri.EscapeDataString(Note ?? "")}#invite";
            return await Reload(ex, ct);
        }
    }

    public async Task<IActionResult> OnPostRemoveAsync(CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try { await Accounts.RemoveTesterInviteAsync(Actor, Staff, Email ?? "", ct); return RedirectToPage(); }
        catch (IdentityException ex) { return await Reload(ex, ct); }
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid id, CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try
        {
            var invite = await Accounts.ApproveInviteRequestAsync(Actor, Staff, id, ct);
            if (!invite.HasAccount) mailer.TesterInvited(invite.Email);
            return RedirectToPage();
        }
        catch (IdentityException ex) { return await Reload(ex, ct); }
    }

    public async Task<IActionResult> OnPostDeclineAsync(Guid id, CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try { await Accounts.DeclineInviteRequestAsync(Actor, Staff, id, ct); return RedirectToPage(); }
        catch (IdentityException ex) { return await Reload(ex, ct); }
    }

    // Show the error with the lists still on the page.
    private async Task<IActionResult> Reload(IdentityException error, CancellationToken ct)
    {
        try { Invites = await Accounts.ListTesterInvitesAsync(Actor, Staff, ct); Requests = await Accounts.ListInviteRequestsAsync(Actor, Staff, ct); } catch (IdentityException) { }
        return StaffFailure(error);
    }
}
