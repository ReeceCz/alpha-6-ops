using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Alpha6Ops.Server.Pages.Staff;

public class ReleasesModel(AccountsService accounts, ServerSettings settings, StaffAccess staff, IOptions<ReleaseSettings> release) : StaffPage(accounts, settings, staff)
{
    public ReleaseNoteView[] Published { get; private set; } = [];
    [BindProperty] public string? Version { get; set; }
    [BindProperty] public string? Title { get; set; }
    [BindProperty(Name = "Notes")] public string? NotesText { get; set; }
    public bool Editing { get; private set; }
    public string? CurrentRelease => release.Value.Available ? release.Value.Version : null;

    public async Task<IActionResult> OnGetAsync(string? version, CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try
        {
            await Accounts.EnsureStaffAsync(Actor, Staff, ct);
            Published = await Accounts.ListReleaseNotesAsync(ct);
            if (Published.FirstOrDefault(x => x.Version == version) is { } existing)
            { Version = existing.Version; Title = existing.Title; NotesText = existing.Notes; Editing = true; }
            else Version = CurrentRelease;
            return Page();
        }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try { await Accounts.SaveReleaseNoteAsync(Actor, Staff, new(Version, Title, NotesText), ct); return RedirectToPage(); }
        catch (IdentityException ex)
        {
            Published = await Accounts.ListReleaseNotesAsync(ct);
            return StaffFailure(ex);
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try { await Accounts.DeleteReleaseNoteAsync(Actor, Staff, Version ?? "", ct); return RedirectToPage(); }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }
}
