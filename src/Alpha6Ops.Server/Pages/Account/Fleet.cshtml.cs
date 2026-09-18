using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Account;

public class FleetModel(AccountsService accounts, ServerSettings settings) : AccountPage(accounts, settings)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Edit { get; set; }
    public AirlineWorkspace? Airline { get; private set; }
    public AircraftResponse[] Fleet { get; private set; } = [];
    public AircraftResponse? Editing { get; private set; }
    public string? Saved { get; private set; }
    public bool CanEdit => Airline?.Capabilities.Contains(Capabilities.OperationsManage) == true;

    public async Task<IActionResult> OnGetAsync(string? saved, CancellationToken ct)
    {
        try { await LoadAsync(ct); Saved = saved; return Page(); }
        catch (IdentityException ex) { return Failure(ex); }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Airline = await Accounts.GetAirlineAsync(Actor, Id, ct);
        Fleet = await Accounts.ListFleetAsync(Actor, Id, ct);
        Editing = Edit is { } id ? Fleet.FirstOrDefault(a => a.Id == id) : null;
    }

    public async Task<IActionResult> OnPostSaveAsync(Guid? aircraftId, string registration, string typeIcao, string? name, string? homeBase, string status, string? notes, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(null, ct);
        try
        {
            var aircraft = await Accounts.SaveAircraftAsync(Actor, Id, aircraftId, new(registration, typeIcao, name ?? "", homeBase ?? "", status, notes), ct);
            return RedirectToPage(new { Id, saved = $"{aircraft.Registration} saved." });
        }
        catch (IdentityException ex) { await LoadAsync(ct); return Failure(ex); }
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid aircraftId, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(null, ct);
        try { await Accounts.DeleteAircraftAsync(Actor, Id, aircraftId, ct); return RedirectToPage(new { Id, saved = "Aircraft removed." }); }
        catch (IdentityException ex) { await LoadAsync(ct); return Failure(ex); }
    }
}
