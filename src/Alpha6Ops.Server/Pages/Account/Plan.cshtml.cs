using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Account;

public class PlanModel(AccountsService accounts, ServerSettings settings) : AccountPage(accounts, settings)
{
    public BootstrapResponse? Account { get; private set; }
    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        try { Account = await Accounts.BootstrapAsync(Actor, ct); return Page(); }
        catch (IdentityException ex) { return Failure(ex); }
    }
    public async Task<IActionResult> OnPostAsync(PersonalPlan plan, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(ct);
        try { await Accounts.SetPersonalPlanAsync(Actor, new(plan), ct); return RedirectToPage("Index"); }
        catch (IdentityException ex) { Account = await Accounts.BootstrapAsync(Actor, ct); return Failure(ex); }
    }
}
