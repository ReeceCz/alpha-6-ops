using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Account;

[RequestSizeLimit(1_400_000), RequestFormLimits(MultipartBodyLengthLimit = 1_400_000)]
public class AirlineModel(AccountsService accounts, ServerSettings settings) : AccountPage(accounts, settings)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public AirlineWorkspace? Airline { get; private set; }
    public MemberResponse[] Members { get; private set; } = [];
    public InvitationResponse[] Invitations { get; private set; } = [];
    public ActivityEntry[] Activity { get; private set; } = [];
    public IssuedInvitation? Issued { get; private set; }
    public bool PlanSaved { get; private set; }
    public async Task<IActionResult> OnGetAsync(bool planSaved, CancellationToken ct)
    {
        try { await LoadAsync(ct); PlanSaved = planSaved; return Page(); }
        catch (IdentityException ex) { return Failure(ex); }
    }
    private async Task LoadAsync(CancellationToken ct)
    {
        Airline = await Accounts.GetAirlineAsync(Actor, Id, ct);
        Members = await Accounts.ListMembersAsync(Actor, Id, ct);
        Invitations = await Accounts.ListInvitationsAsync(Actor, Id, ct);
        Activity = await Accounts.ListActivityAsync(Actor, Id, ct);
    }
    public async Task<IActionResult> OnPostPlanAsync(AirlinePlan plan, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(false, ct);
        try { await Accounts.SetAirlinePlanAsync(Actor, Id, new(plan), ct); return RedirectToPage(new { Id, planSaved = true }); }
        catch (IdentityException ex) { return Failure(ex); }
    }
    public async Task<IActionResult> OnPostLogoAsync(IFormFile? logo, CancellationToken ct)
    {
        try
        {
            if (logo is null || logo.Length == 0) throw new IdentityException("invalid_image", "Choose an image file.", 400);
            if (logo.Length > ImageRules.MaxBytes) throw new IdentityException("invalid_image", "Images must be 1 MB or smaller.", 400);
            using var stream = new MemoryStream();
            await logo.CopyToAsync(stream, ct);
            await Accounts.SetAirlineLogoAsync(Actor, Id, stream.ToArray(), ct);
            return RedirectToPage(new { Id, planSaved = false });
        }
        catch (IdentityException ex) { try { await LoadAsync(ct); } catch (IdentityException) { } return Failure(ex); }
    }
    public async Task<IActionResult> OnPostRemoveLogoAsync(CancellationToken ct)
    {
        try { await Accounts.RemoveAirlineLogoAsync(Actor, Id, ct); return RedirectToPage(new { Id }); }
        catch (IdentityException ex) { try { await LoadAsync(ct); } catch (IdentityException) { } return Failure(ex); }
    }
    public async Task<IActionResult> OnPostInviteAsync(string email, AirlineRole[] roles, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(false, ct);
        try
        {
            // Load before issuing so an unrelated read failure cannot discard the only token copy.
            await LoadAsync(ct);
            Issued = await Accounts.InviteAsync(Actor, Id, new(email, roles), ct);
            return Page();
        }
        catch (IdentityException ex) { return Failure(ex); }
    }
    public async Task<IActionResult> OnPostRevokeAsync(Guid invitationId, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(false, ct);
        try { await Accounts.RevokeInvitationAsync(Actor, Id, invitationId, ct); return RedirectToPage(new { Id }); }
        catch (IdentityException ex) { return Failure(ex); }
    }
    public async Task<IActionResult> OnPostRolesAsync(Guid membershipId, AirlineRole[] roles, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(false, ct);
        try { await Accounts.ChangeRolesAsync(Actor, Id, membershipId, new(roles), ct); return RedirectToPage(new { Id }); }
        catch (IdentityException ex) { return Failure(ex); }
    }
    public async Task<IActionResult> OnPostTransferAsync(Guid newOwnerUserId, bool confirmTransfer, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(false, ct);
        if (!confirmTransfer) return Failure(new("confirmation_required", "Confirm that you want to transfer ownership.", 400));
        try { await Accounts.TransferOwnershipAsync(Actor, Id, new(newOwnerUserId), ct); return RedirectToPage("Index"); }
        catch (IdentityException ex) { return Failure(ex); }
    }
}
