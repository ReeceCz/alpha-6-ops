using System.Globalization;
using System.Text.RegularExpressions;
using Alpha6Ops.Identity;

namespace Alpha6Ops.Accounts;

// Reads a logbook export from another platform. Columns are matched by name using a synonym table, so the
// files vAMSYS, smartCARS/phpVMS, FSHub and spreadsheet users produce all map without a format picker.
public static partial class LogbookCsv
{
    public sealed record Row(int Line, FlightLogRequest? Flight, string? Error);
    public sealed record Parsed(string[] Columns, Row[] Rows);

    private static readonly Dictionary<string, string[]> Synonyms = new()
    {
        ["departure"] = ["departure", "departure_utc", "departure_time", "dep_time", "date", "flight_date", "departed", "off_block", "block_off", "out", "start", "started", "timestamp", "created_at", "date_utc"],
        ["arrival"] = ["arrival", "arrival_utc", "arrival_time", "arr_time", "arrived", "on_block", "block_on", "in", "end", "ended", "landed"],
        ["flight"] = ["flight", "flight_number", "flightnumber", "flight_no", "flt", "callsign", "ident", "flight_id_text"],
        ["origin"] = ["origin", "dep", "departure_airport", "dep_airport", "from", "dep_icao", "origin_icao", "departure_icao", "depicao", "dpt"],
        ["destination"] = ["destination", "arr", "arrival_airport", "arr_airport", "to", "arr_icao", "destination_icao", "arrival_icao", "arricao", "dest"],
        ["aircraft"] = ["aircraft", "aircraft_type", "type", "icao_type", "aircraft_icao", "ac_type", "equipment", "airframe"],
        ["registration"] = ["registration", "reg", "tail", "tail_number", "aircraft_registration", "aircraft_reg"],
        ["block"] = ["block", "block_time", "block_minutes", "duration", "flight_time", "flighttime", "time", "hours", "total_time", "elapsed"],
        ["airtime"] = ["air_time", "airtime", "flight_minutes", "airborne", "airborne_time"],
        ["distance"] = ["distance", "distance_nm", "dist", "nm", "route_distance"],
        ["landing"] = ["landing_rate", "landing", "landingrate", "touchdown", "touchdown_rate", "vs", "landing_vs", "fpm"],
        ["fuel"] = ["fuel", "fuel_used", "fuel_burn", "burn", "fuel_used_kg", "fuelused"],
        ["network"] = ["network", "online_network", "vatsim", "ivao"],
        ["notes"] = ["notes", "note", "comments", "comment", "remarks", "remark", "route"]
    };

    public static Parsed Parse(string csv)
    {
        var lines = (csv ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(l => l.Trim().Length > 0).ToArray();
        if (lines.Length < 2) throw new IdentityException("invalid_logbook", "Paste or upload a CSV with a header row and at least one flight.", 400);
        if (lines.Length > 5001) throw new IdentityException("invalid_logbook", "Import at most 5,000 flights at a time.", 400);
        var separator = lines[0].Count(c => c == ';') > lines[0].Count(c => c == ',') ? ';' : lines[0].Count(c => c == '\t') > lines[0].Count(c => c == ',') ? '\t' : ',';
        var header = Split(lines[0], separator).Select(Normalize).ToArray();
        var map = new Dictionary<string, int>();
        foreach (var (field, names) in Synonyms)
            foreach (var name in names)
            {
                var index = Array.IndexOf(header, name);
                if (index >= 0 && !map.ContainsKey(field) && !map.ContainsValue(index)) { map[field] = index; break; }
            }
        if (!map.ContainsKey("departure") || !map.ContainsKey("origin") || !map.ContainsKey("destination"))
            throw new IdentityException("invalid_logbook", "The header needs at least a date/departure column, an origin column and a destination column. Recognised names include date, departure, dep_time, origin, from, dep_icao, destination, to, arr_icao.", 400);
        var rows = new List<Row>();
        for (var i = 1; i < lines.Length; i++)
        {
            var cells = Split(lines[i], separator);
            string Cell(string field) => map.TryGetValue(field, out var index) && index < cells.Length ? cells[index].Trim() : "";
            try
            {
                var departure = ParseTime(Cell("departure")) ?? throw new IdentityException("invalid_logbook", $"Unrecognised departure date or time '{Cell("departure")}'.", 400);
                var arrival = ParseTime(Cell("arrival"));
                if (arrival is { } a && a <= departure) arrival = a.AddDays(1) > departure && (a.AddDays(1) - departure).TotalHours < 24 ? a.AddDays(1) : null;
                var block = ParseMinutes(Cell("block")) ?? (arrival is { } arr ? (int)Math.Round((arr - departure).TotalMinutes) : (int?)null) ?? 0;
                rows.Add(new(i + 1, new(Cell("flight"), Cell("origin"), Cell("destination"), Cell("aircraft"), Cell("registration"), departure, arrival, block,
                    ParseMinutes(Cell("airtime")), ParseInt(Cell("distance")), ParseInt(Cell("landing")), ParseInt(Cell("fuel")), Cell("network"), Cell("notes")), null));
            }
            catch (IdentityException ex) { rows.Add(new(i + 1, null, ex.Message)); }
        }
        return new(map.Keys.Order().ToArray(), rows.ToArray());
    }

    private static string Normalize(string name) => NonWord().Replace(name.Trim().Trim('"').ToLowerInvariant().Replace(' ', '_').Replace('-', '_'), "");

    private static string[] Split(string line, char separator)
    {
        var cells = new List<string>(); var current = new System.Text.StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; } else quoted = !quoted; }
            else if (c == separator && !quoted) { cells.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        cells.Add(current.ToString());
        return cells.ToArray();
    }

    private static readonly string[] Formats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd", "dd/MM/yyyy HH:mm", "dd/MM/yyyy",
        "MM/dd/yyyy HH:mm", "MM/dd/yyyy", "dd.MM.yyyy HH:mm", "dd.MM.yyyy", "yyyy/MM/dd HH:mm", "yyyy/MM/dd", "dd-MM-yyyy HH:mm", "dd-MM-yyyy", "yyyyMMddHHmm", "yyyyMMdd"];

    // Times without an offset are taken as UTC, which is what every simulator platform exports.
    public static DateTimeOffset? ParseTime(string value)
    {
        value = (value ?? "").Trim().Trim('"');
        if (value.Length == 0) return null;
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var unix) && value.Length >= 9)
            return unix > 30_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unix) : DateTimeOffset.FromUnixTimeSeconds(unix);
        if (DateTimeOffset.TryParseExact(value, Formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exact)) return exact;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var loose)) return loose;
        return null;
    }

    // Accepts minutes ("95"), hours:minutes ("1:35", "01:35:20"), decimal hours ("1.6h", "1.58"), and "1h 35m".
    public static int? ParseMinutes(string value)
    {
        value = (value ?? "").Trim().ToLowerInvariant();
        if (value.Length == 0) return null;
        var hm = HoursMinutes().Match(value);
        if (hm.Success) return int.Parse(hm.Groups[1].Value) * 60 + int.Parse(hm.Groups[2].Value);
        var words = HoursWords().Match(value);
        if (words.Success) return int.Parse(words.Groups[1].Value) * 60 + (words.Groups[2].Success ? int.Parse(words.Groups[2].Value) : 0);
        if (value.EndsWith('h') && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours)) return (int)Math.Round(hours * 60);
        if (value.EndsWith('m') && int.TryParse(value[..^1], out var minutes)) return minutes;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return number < 30 && number != Math.Floor(number) ? (int)Math.Round(number * 60) : (int)Math.Round(number);
        return null;
    }

    public static int? ParseInt(string value)
    {
        value = (value ?? "").Trim().Replace(",", "");
        if (value.Length == 0) return null;
        var match = Number().Match(value);
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? (int)Math.Round(n) : null;
    }

    [GeneratedRegex("[^a-z0-9_]")] private static partial Regex NonWord();
    [GeneratedRegex(@"^(\d{1,3}):(\d{2})(?::\d{2})?$")] private static partial Regex HoursMinutes();
    [GeneratedRegex(@"^(\d{1,3})\s*h(?:ours?)?\s*(?:(\d{1,2})\s*m)?")] private static partial Regex HoursWords();
    [GeneratedRegex(@"-?\d+(\.\d+)?")] private static partial Regex Number();
}
