using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Staff;

// Private desktop builds for staff. Downloads are a GET link, not a form, because the portal's
// form-action CSP would block a form post that redirects to the bucket.
public class BuildsModel(AccountsService accounts, ServerSettings settings, StaffAccess staff, IPreviewBuildStore store) : StaffPage(accounts, settings, staff)
{
    public bool Enabled => store.Enabled;
    public PreviewBuild[] Builds { get; private set; } = [];
    public string? StoreError { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try
        {
            await Accounts.EnsureStaffAsync(Actor, Staff, ct);
            if (Enabled) await LoadAsync(ct);
            return Page();
        }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }

    public async Task<IActionResult> OnGetDownloadAsync(string? key, CancellationToken ct)
    {
        if (!Staff.IsConfigured || !Enabled) return NotFound();
        try
        {
            await Accounts.EnsureStaffAsync(Actor, Staff, ct);
            // Only a key the store just listed is signed, so the link can't be pointed at anything else in the bucket.
            if (!await LoadAsync(ct)) return Page();
            if (Builds.FirstOrDefault(b => b.Key == key) is not { } build) return NotFound();
            Response.Headers.CacheControl = "no-store";
            return Redirect(store.DownloadUrl(build).AbsoluteUri);
        }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        try { Builds = await store.ListAsync(ct); return true; }
        catch (PreviewBuildStoreException ex) { StoreError = ex.Message; Response.StatusCode = StatusCodes.Status502BadGateway; return false; }
    }
}
