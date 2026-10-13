using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Account;

public class IndexModel(AccountsService accounts, ServerSettings settings, StaffAccess staff) : AccountPage(accounts, settings)
{
    public BootstrapResponse? Account { get; private set; }
    public bool IsStaff { get; private set; }
    public GettingStarted? Start { get; private set; }
    public int UnreadReplies { get; private set; }
    public int WaitingInvitations { get; private set; }
    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        try { Account = await Accounts.BootstrapAsync(Actor, ct); Start = await Accounts.GettingStartedAsync(Actor, ct); UnreadReplies = await Accounts.UnreadFeedbackRepliesAsync(Actor, ct); WaitingInvitations = (await Accounts.ListInvitationsForMeAsync(Actor, ct)).Length; IsStaff = staff.IsStaff(Actor); return Page(); }
        catch (IdentityException ex) { return Failure(ex); }
    }
    public async Task<IActionResult> OnPostWorkspaceAsync(Guid? airlineId, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(ct);
        try { await Accounts.SetWorkspaceAsync(Actor, new(airlineId), ct); return RedirectToPage(); }
        catch (IdentityException ex) { return Failure(ex); }
    }
    public IActionResult OnPostLogout() => SignOut(new AuthenticationProperties { RedirectUri = "/SignedOut" }, CookieAuthenticationDefaults.AuthenticationScheme, "Auth0");
}
