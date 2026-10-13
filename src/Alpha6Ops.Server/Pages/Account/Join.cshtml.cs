using System.ComponentModel.DataAnnotations;
using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Account;

public class JoinModel(AccountsService accounts, ServerSettings settings) : AccountPage(accounts, settings)
{
    [BindProperty, StringLength(256)] public string Token { get; set; } = "";
    public PendingInvitation[] Waiting { get; private set; } = [];
    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        try { Waiting = await Accounts.ListInvitationsForMeAsync(Actor, ct); return Page(); }
        catch (IdentityException ex) { return Failure(ex); }
    }
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(Token)) return await Reshow(new("invitation_invalid", "Enter the invitation code.", 400), ct);
        try { var airline = await Accounts.AcceptInvitationAsync(Actor, Token.Trim(), ct); return RedirectToPage("Airline", new { id = airline.Id, joined = true }); }
        catch (IdentityException ex) { ModelState.Remove(nameof(Token)); Token = ""; return await Reshow(ex, ct); }
    }
    public async Task<IActionResult> OnPostAcceptAsync(Guid invitationId, CancellationToken ct)
    {
        try { var airline = await Accounts.AcceptInvitationByIdAsync(Actor, invitationId, ct); return RedirectToPage("Airline", new { id = airline.Id, joined = true }); }
        catch (IdentityException ex) { return await Reshow(ex, ct); }
    }
    private async Task<IActionResult> Reshow(IdentityException error, CancellationToken ct)
    {
        try { Waiting = await Accounts.ListInvitationsForMeAsync(Actor, ct); } catch (IdentityException) { }
        return Failure(error);
    }
}
