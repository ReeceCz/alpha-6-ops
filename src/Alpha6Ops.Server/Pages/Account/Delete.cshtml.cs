using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Account;

public class DeleteModel(AccountsService accounts, ServerSettings settings) : AccountPage(accounts, settings)
{
    [BindProperty] public bool Confirm { get; set; }
    public DeletionStatus? Status { get; private set; }
    public bool Cancelled { get; private set; }

    public async Task<IActionResult> OnGetAsync(bool cancelled, CancellationToken ct)
    {
        try { Status = await Accounts.GetDeletionStatusAsync(Actor, ct); Cancelled = cancelled; return Page(); }
        catch (IdentityException ex) { return Failure(ex); }
    }

    public async Task<IActionResult> OnPostRequestAsync(CancellationToken ct)
    {
        try
        {
            if (!Confirm) throw new IdentityException("confirmation_required", "Tick the box to confirm you want to delete your account.", 400);
            await Accounts.RequestDeletionAsync(Actor, ct);
            return RedirectToPage();
        }
        catch (IdentityException ex)
        {
            try { Status = await Accounts.GetDeletionStatusAsync(Actor, ct); } catch (IdentityException) { }
            return Failure(ex);
        }
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken ct)
    {
        try { await Accounts.CancelDeletionAsync(Actor, ct); return RedirectToPage(new { cancelled = true }); }
        catch (IdentityException ex) { return Failure(ex); }
    }
}
