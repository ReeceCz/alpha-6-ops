using System.Text;
using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Account;

[RequestSizeLimit(3_200_000), RequestFormLimits(MultipartBodyLengthLimit = 3_200_000, ValueLengthLimit = 3_000_000)]
public class LogbookModel(AccountsService accounts, ServerSettings settings) : AccountPage(accounts, settings)
{
    public const int PageSize = 50;
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;
    public LogbookSummary? Summary { get; private set; }
    public LogbookPage? Flights { get; private set; }
    public LogbookImportResult? Import { get; private set; }
    public string? Saved { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? saved, CancellationToken ct)
    {
        try { await LoadAsync(ct); Saved = saved; return Page(); }
        catch (IdentityException ex) { return Failure(ex); }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Summary = await Accounts.LogbookSummaryAsync(Actor, ct);
        Flights = await Accounts.ListFlightsAsync(Actor, Math.Max(1, P), PageSize, ct);
    }

    public async Task<IActionResult> OnPostImportAsync(IFormFile? file, string? csv, CancellationToken ct)
    {
        try
        {
            var text = csv ?? "";
            if (file is { Length: > 0 })
            {
                if (file.Length > 3_000_000) throw new IdentityException("invalid_logbook", "Files must be 3 MB or smaller.", 400);
                using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, true);
                text = await reader.ReadToEndAsync(ct);
            }
            Import = await Accounts.ImportLogbookAsync(Actor, new(text, file?.FileName), ct);
            await LoadAsync(ct);
            return Page();
        }
        catch (IdentityException ex) { try { await LoadAsync(ct); } catch (IdentityException) { } return Failure(ex); }
    }

    public async Task<IActionResult> OnPostUndoAsync(Guid batchId, CancellationToken ct)
    {
        try { var removed = await Accounts.UndoImportAsync(Actor, batchId, ct); return RedirectToPage(new { saved = $"Import undone: {removed} flights removed." }); }
        catch (IdentityException ex) { await LoadAsync(ct); return Failure(ex); }
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid flightId, CancellationToken ct)
    {
        try { await Accounts.DeleteFlightAsync(Actor, flightId, ct); return RedirectToPage(new { saved = "Flight removed.", p = P }); }
        catch (IdentityException ex) { await LoadAsync(ct); return Failure(ex); }
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken ct)
    {
        try
        {
            var text = new StringBuilder("departure_utc,arrival_utc,flight,origin,destination,aircraft_type,registration,block_minutes,air_minutes,distance_nm,landing_rate,fuel_used_kg,network,notes,source\n");
            for (var page = 1; ; page++)
            {
                var batch = await Accounts.ListFlightsAsync(Actor, page, 200, ct);
                foreach (var f in batch.Entries)
                    text.Append(f.DepartureUtc.ToString("yyyy-MM-dd HH:mm")).Append(',').Append(f.ArrivalUtc?.ToString("yyyy-MM-dd HH:mm")).Append(',').Append(f.FlightNumber).Append(',')
                        .Append(f.Origin).Append(',').Append(f.Destination).Append(',').Append(f.AircraftType).Append(',').Append(f.Registration).Append(',')
                        .Append(f.BlockMinutes).Append(',').Append(f.FlightMinutes).Append(',').Append(f.DistanceNm).Append(',').Append(f.LandingRateFpm).Append(',')
                        .Append(f.FuelUsedKg).Append(',').Append(f.Network).Append(',').Append('"').Append(f.Notes.Replace("\"", "\"\"").Replace("\n", " ")).Append('"').Append(',').Append(f.Source).Append('\n');
                if (batch.Entries.Length < 200) break;
            }
            return File(Encoding.UTF8.GetBytes(text.ToString()), "text/csv", "alpha6-logbook.csv");
        }
        catch (IdentityException ex) { return Failure(ex); }
    }

    public static string Hours(int minutes) => $"{minutes / 60:n0}h {minutes % 60:00}m";
}
