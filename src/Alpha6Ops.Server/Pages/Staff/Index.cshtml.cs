using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Alpha6Ops.Server.Pages.Staff;

public class OverviewModel(AccountsService accounts, ServerSettings settings, StaffAccess staff, IOptions<ReleaseSettings> release) : StaffPage(accounts, settings, staff)
{
    public StaffOverview? Overview { get; private set; }
    public string? CurrentRelease => release.Value.Available ? release.Value.Version : null;

    /// <summary>True when pilots on this version should update: older than the published release.</summary>
    public bool IsBehind(string version) => CurrentRelease is { } current
        && AccountsService.ParseVersion(version) < AccountsService.ParseVersion(current);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try { Overview = await Accounts.GetStaffOverviewAsync(Actor, Staff, ct); return Page(); }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }
}
