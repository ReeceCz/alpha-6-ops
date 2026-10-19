using System.Globalization;

namespace Alpha6Ops.Identity;

/// <summary>One flight of a recurring route on a particular UTC day.</summary>
public sealed record ScheduledLeg(RouteResponse Route, DateTimeOffset DepartureUtc, DateTimeOffset ArrivalUtc);

/// <summary>
/// Turns an airline's recurring routes into dated legs, so every client reads the weekday bitmask the same way.
/// Days are the UTC departure day: bit 0 (1) is Monday through bit 6 (64) Sunday. Arrival is departure plus block time,
/// which can fall on the next UTC day.
/// </summary>
public static class AirlineSchedule
{
    public static bool Operates(int daysOfWeek, DateOnly utcDay) => (daysOfWeek & (1 << (((int)utcDay.DayOfWeek + 6) % 7))) != 0;

    /// <summary>Active legs whose block time overlaps [from, to), ordered by departure. Includes a leg already airborne at <paramref name="from"/>.</summary>
    public static ScheduledLeg[] Between(IEnumerable<RouteResponse> routes, DateTimeOffset from, DateTimeOffset to)
    {
        from = from.ToUniversalTime(); to = to.ToUniversalTime();
        if (to <= from) return [];
        var legs = new List<ScheduledLeg>();
        // Block time is at most a day, so a leg departing two UTC days before `from` can't still be flying.
        var first = DateOnly.FromDateTime(from.UtcDateTime).AddDays(-2);
        var last = DateOnly.FromDateTime(to.UtcDateTime);
        foreach (var route in routes.Where(r => r.Active && r.BlockMinutes > 0))
        {
            if (!TimeOnly.TryParseExact(route.DepartureUtc, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) continue;
            for (var day = first; day <= last; day = day.AddDays(1))
            {
                if (!Operates(route.DaysOfWeek, day)) continue;
                var departure = new DateTimeOffset(day.ToDateTime(time), TimeSpan.Zero);
                var arrival = departure.AddMinutes(route.BlockMinutes);
                if (departure < to && arrival > from) legs.Add(new(route, departure, arrival));
            }
        }
        return [.. legs.OrderBy(x => x.DepartureUtc).ThenBy(x => x.Route.FlightNumber, StringComparer.Ordinal)];
    }
}
