using Alpha6Ops.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alpha6Ops.Accounts;

public sealed record GettingStarted(bool ProfileSet, bool DesktopSignedIn, bool HasFlights, bool SentReport);
public sealed record RouteCount(string Origin, string Destination, int Flights);
public sealed record MonthCount(int Year, int Month, int Flights, int BlockMinutes);
public sealed record LogbookInsights(int FlightsLast30Days, int BlockMinutesLast30Days, RouteCount[] TopRoutes,
    int? AverageLandingFpm, int? SoftestLandingFpm, FlightLogEntry? Longest, MonthCount[] Months);

// The pilot's own home: what they've set up so far, and what their logbook says. Web only; the desktop contract
// (LogbookSummary) is unchanged.
public sealed partial class AccountsService
{
    public Task<GettingStarted> GettingStartedAsync(ActorIdentity actor, CancellationToken ct = default) => Execute(actor, async (user, _) =>
    {
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user.Id, ct);
        return new GettingStarted(
            profile is not null && (profile.Callsign.Length > 0 || profile.HomeBaseIcao.Length > 0 || profile.SimBriefUsername.Length > 0),
            profile?.LastSeenAt is not null,
            await db.Flights.AnyAsync(x => x.UserId == user.Id, ct),
            await db.Feedback.AnyAsync(x => x.UserId == user.Id, ct));
    }, ct);

    public Task<LogbookInsights> LogbookInsightsAsync(ActorIdentity actor, CancellationToken ct = default) => Execute(actor, async (user, now) =>
    {
        // One pass in memory: a pilot's logbook is at most a few thousand rows.
        var flights = await db.Flights.AsNoTracking().Where(x => x.UserId == user.Id).ToListAsync(ct);
        var recent = flights.Where(x => x.DepartureUtc >= now.AddDays(-30)).ToList();
        var routes = flights.GroupBy(x => (x.Origin, x.Destination))
            .Select(g => new RouteCount(g.Key.Origin, g.Key.Destination, g.Count()))
            .OrderByDescending(x => x.Flights).ThenBy(x => x.Origin).ThenBy(x => x.Destination).Take(5).ToArray();
        // Imports store landing rates signed or unsigned; compare magnitudes.
        var landings = flights.Where(x => x.LandingRateFpm is not null).Select(x => Math.Abs(x.LandingRateFpm!.Value)).ToList();
        var longest = flights.Where(x => x.BlockMinutes > 0).OrderByDescending(x => x.BlockMinutes).ThenByDescending(x => x.DepartureUtc).FirstOrDefault();
        var thisMonth = new DateTime(now.UtcDateTime.Year, now.UtcDateTime.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var months = Enumerable.Range(0, 12).Select(i => thisMonth.AddMonths(i - 11)).Select(m =>
        {
            var inMonth = flights.Where(x => x.DepartureUtc.UtcDateTime.Year == m.Year && x.DepartureUtc.UtcDateTime.Month == m.Month).ToList();
            return new MonthCount(m.Year, m.Month, inMonth.Count, inMonth.Sum(x => x.BlockMinutes));
        }).ToArray();
        return new LogbookInsights(recent.Count, recent.Sum(x => x.BlockMinutes), routes,
            landings.Count > 0 ? (int)Math.Round(landings.Average()) : null, landings.Count > 0 ? landings.Min() : null,
            longest is null ? null : FlightView(longest), months);
    }, ct);
}
