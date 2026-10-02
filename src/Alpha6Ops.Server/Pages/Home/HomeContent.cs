namespace Alpha6Ops.Server.Pages.Home;

// Landing-page content as data, so the partials stay markup and every claim lives in one reviewable place.
// Everything here describes what the product does today; the sample flight is labelled as a demonstration.
public static class HomeContent
{
    public sealed record Phase(string Code, string Time, string Event, string Detected, string? LegToNext);
    public sealed record Status(string Css, string Label, string Meaning);
    public sealed record Feature(string Code, string Title, string Text);
    public sealed record Step(string Title, string Text);
    public sealed record RoleRow(string Role, string Can);
    public sealed record BoardRow(string Flight, string From, string To, string Aircraft, string SchedOut, string ActualOut, string ActualIn, string Phase, string StatusCss, string Status);
    public sealed record LogEvent(string Time, string Code, string Text);

    // Sample flight S4601, KMKE to KORD, 2 September 2026 (the repository's fixed sample date).
    public static readonly Phase[] Phases =
    [
        new("OUT", "1802Z", "Off block", "Parking brake released and the aircraft moving on the ground.", "Taxi out 12 min"),
        new("OFF", "1814Z", "Takeoff", "The wheels leave the runway.", "Airborne 47 min"),
        new("ON", "1901Z", "Landing", "The wheels are back on the ground.", "Taxi in 8 min"),
        new("IN", "1909Z", "Block in", "Parked at the gate with the brake set. Fenix A320-family aircraft confirm it from the engine shutdown; on other aircraft you confirm it with one click.", null),
    ];

    public static readonly Status[] Statuses =
    [
        new("on-time", "On time", "Predicted gate arrival within five minutes of the schedule."),
        new("on-time", "8 min early", "Running ahead of the planned gate arrival."),
        new("delayed", "25 min late", "Behind the planned gate arrival. Thirty minutes or more shows red."),
    ];

    // The operations-board demonstration: one aircraft's rotation, so the delay on S4602 follows from S4601.
    public static readonly BoardRow[] Board =
    [
        new("S4601", "KMKE", "KORD", "A20N", "1800Z", "1802Z", "1909Z", "Block in", "on-time", "On time"),
        new("S4602", "KORD", "KDEN", "A20N", "1925Z", "1950Z", "—", "Taxi out", "delayed", "Delayed 25 min"),
        new("S4603", "KDEN", "KORD", "A20N", "2310Z", "—", "—", "At gate", "delayed", "Delayed 25 min"),
        new("S4610", "KMKE", "KMSP", "B738", "1915Z", "—", "—", "At gate", "on-time", "On time"),
    ];

    public static readonly LogEvent[] Log =
    [
        new("1802Z", "OUT", "Off block. Parking brake released, moving."),
        new("1814Z", "OFF", "Takeoff from KMKE."),
        new("1901Z", "ON", "Landing at KORD."),
        new("1909Z", "IN", "Block in. Brake set, engines off."),
        new("1909Z", "LOG", "Written to the pilot's logbook."),
    ];

    public static readonly Step[] Steps =
    [
        new("Install and sign in", "Install S4 OPS for Windows and sign in once with your account. It remembers you."),
        new("Plan the flight", "Import your SimBrief plan in Dispatch, or enter the flight yourself, then accept the flight."),
        new("Connect and fly", "Connect to Microsoft Flight Simulator 2024 at the gate and fly the way you always do. OUT, OFF and ON are recorded as they happen."),
        new("Block in and file", "Confirm block in at the gate, review the flight and submit your PIREP. Block and flight time are worked out for you."),
    ];

    public static readonly Feature[] Pilots =
    [
        new("LOG", "Logbook", "Every tracked flight in the app's pilot logbook with its four times, route and aircraft. Bring an existing logbook into your account from a CSV file."),
        new("DSP", "Dispatch", "Import a SimBrief plan, review the release and accept it. Your SimBrief username follows your account."),
        new("TRK", "Live tracking", "Phase, route progress and a predicted gate arrival as you fly, with a live map of your route."),
        new("CRZ", "Cruise and descent", "Your actual cruise level, any step climbs and the top of descent, taken from what the aircraft did rather than the flight plan."),
        new("REC", "Recovery", "If the sim or the app restarts mid-flight, S4 OPS picks the same flight back up instead of starting a new one."),
        new("WSP", "Workspaces", "Your own flying and every airline you join, under one account."),
    ];

    public static readonly Feature[] Airlines =
    [
        new("CRW", "Crew roster", "Pilots, dispatchers and administrators, with roles you control."),
        new("INV", "Invitations", "A code your invitee redeems once. Nothing leaks in email."),
        new("SKD", "Schedule and fleet", "Routes with days and block times, aircraft with their status, kept in one place."),
        new("SEC", "Security", "Every administrative change asks for a fresh security check."),
    ];

    public static readonly RoleRow[] Roles =
    [
        new("Pilot", "Sees the airline, its schedule and fleet."),
        new("Dispatcher", "Edits the schedule and the fleet."),
        new("Administrator", "Manages crew, invitations and branding, after a fresh security check."),
    ];
}
