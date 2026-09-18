using System.Text;
using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Account;

public class ScheduleModel(AccountsService accounts, ServerSettings settings) : AccountPage(accounts, settings)
{
    public static readonly string[] DayLabels = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Edit { get; set; }
    public AirlineWorkspace? Airline { get; private set; }
    public RouteResponse[] Routes { get; private set; } = [];
    public RouteResponse? Editing { get; private set; }
    public ScheduleImportResult? Import { get; private set; }
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
        Routes = await Accounts.ListRoutesAsync(Actor, Id, ct);
        Editing = Edit is { } id ? Routes.FirstOrDefault(r => r.Id == id) : null;
    }

    public async Task<IActionResult> OnPostSaveAsync(Guid? routeId, string flightNumber, string origin, string destination, string departureUtc,
        int blockMinutes, int[] days, string? aircraftType, string? notes, bool active, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(null, ct);
        var mask = days.Where(d => d is >= 0 and < 7).Aggregate(0, (m, d) => m | (1 << d));
        try
        {
            var route = await Accounts.SaveRouteAsync(Actor, Id, routeId, new(flightNumber, origin, destination, departureUtc, blockMinutes, mask, aircraftType ?? "", notes, active), ct);
            return RedirectToPage(new { Id, saved = $"{route.FlightNumber} {route.Origin}-{route.Destination} saved." });
        }
        catch (IdentityException ex) { await LoadAsync(ct); return Failure(ex); }
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid routeId, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(null, ct);
        try { await Accounts.DeleteRouteAsync(Actor, Id, routeId, ct); return RedirectToPage(new { Id, saved = "Route removed." }); }
        catch (IdentityException ex) { await LoadAsync(ct); return Failure(ex); }
    }

    public async Task<IActionResult> OnPostImportAsync(string csv, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await OnGetAsync(null, ct);
        try { Import = await Accounts.ImportRoutesAsync(Actor, Id, new(csv), ct); await LoadAsync(ct); return Page(); }
        catch (IdentityException ex) { await LoadAsync(ct); return Failure(ex); }
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken ct)
    {
        try
        {
            var airline = await Accounts.GetAirlineAsync(Actor, Id, ct);
            var routes = await Accounts.ListRoutesAsync(Actor, Id, ct);
            var text = new StringBuilder("flight,origin,destination,departure_utc,block_minutes,days,aircraft_type,notes\n");
            foreach (var r in routes)
                text.Append(r.FlightNumber).Append(',').Append(r.Origin).Append(',').Append(r.Destination).Append(',').Append(r.DepartureUtc).Append(',')
                    .Append(r.BlockMinutes).Append(',').Append(AccountRules.DaysText(r.DaysOfWeek)).Append(',').Append(r.AircraftType).Append(',')
                    .Append(r.Notes.Replace(",", " ").Replace("\n", " ")).Append('\n');
            return File(Encoding.UTF8.GetBytes(text.ToString()), "text/csv", $"{airline.Slug}-schedule.csv");
        }
        catch (IdentityException ex) { return Failure(ex); }
    }

    public static string DaysText(int mask) => string.Join(" ", Enumerable.Range(0, 7).Where(i => (mask & (1 << i)) != 0).Select(i => DayLabels[i]));
}
