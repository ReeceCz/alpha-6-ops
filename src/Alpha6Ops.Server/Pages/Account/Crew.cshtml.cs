using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Alpha6Ops.Server.Pages.Account;

public class CrewModel(AccountsService accounts, ServerSettings settings, Mailer mailer, IOptions<EmailSettings> email) : AccountPage(accounts, settings)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public AirlineWorkspace? Airline { get; private set; }
    public MemberResponse[] Members { get; private set; } = [];
    public InvitationResponse[] Invitations { get; private set; } = [];
    public IssuedInvitation? Issued { get; private set; }
    public bool EmailOn => email.Value.Enabled;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        try { await LoadAsync(ct); return Page(); }
        catch (IdentityException ex) { return Failure(ex); }
    }
    private async Task LoadAsync(CancellationToken ct)
    {
        Airline = await Accounts.GetAirlineAsync(Actor, Id, ct);
        Members = await Accounts.ListMembersAsync(Actor, Id, ct);
        Invitations = await Accounts.ListInvitationsAsync(Actor, Id, ct);
    }
    public async Task<IActionResult> OnPostInviteAsync(string email, AirlineRole[] roles, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(ct);
        try
        {
            // Load before issuing so an unrelated read failure cannot discard the only token copy.
            await LoadAsync(ct);
            Issued = await Accounts.InviteAsync(Actor, Id, new(email, roles), ct);
            Invitations = await Accounts.ListInvitationsAsync(Actor, Id, ct);
            mailer.AirlineInvitation(Issued.Invitation.Email, Airline!.Name, Actor.DisplayName);
            return Page();
        }
        catch (IdentityException ex) { return Failure(ex); }
    }
    public async Task<IActionResult> OnPostRevokeAsync(Guid invitationId, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(ct);
        try { await Accounts.RevokeInvitationAsync(Actor, Id, invitationId, ct); return RedirectToPage(new { Id }); }
        catch (IdentityException ex) { return Failure(ex); }
    }
    public async Task<IActionResult> OnPostRolesAsync(Guid membershipId, AirlineRole[] roles, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(ct);
        try { await Accounts.ChangeRolesAsync(Actor, Id, membershipId, new(roles), ct); return RedirectToPage(new { Id }); }
        catch (IdentityException ex) { return Failure(ex); }
    }
}
