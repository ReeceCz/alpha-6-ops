using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Account;

// The console's front page: counts and the setup steps. Any member may read it; nothing here needs a security check.
public class AirlineModel(AccountsService accounts, ServerSettings settings) : AccountPage(accounts, settings)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    public AirlineOverview? Overview { get; private set; }
    public bool Joined { get; private set; }
    public async Task<IActionResult> OnGetAsync(bool joined, CancellationToken ct)
    {
        try { Overview = await Accounts.GetAirlineOverviewAsync(Actor, Id, ct); Joined = joined; return Page(); }
        catch (IdentityException ex) { return Failure(ex); }
    }
}
