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

    // The hero's split-flap board: the headline, one sentence per row on wide screens and two rows each on phones.
    // Accent rows are set in taxiway-sign yellow ("we" is S4).
    public const int FlapSteps = 5, FlapDelays = 40;
    public const int BoardWideColumns = 22, BoardNarrowColumns = 13; // every position is a tile; spaces land blank
    public static readonly (string Text, bool Accent)[] BoardWide = [("YOU FLY THE AIRPLANE.", false), ("WE'LL RUN THE AIRLINE.", true)];
    public static readonly (string Text, bool Accent)[] BoardNarrow = [("YOU FLY THE", false), ("AIRPLANE.", false), ("WE'LL RUN THE", true), ("AIRLINE.", true)];

    // Sample flight S4601, KMKE to KORD, 2 September 2026 (the repository's fixed sample date).
    public static readonly Phase[] Phases =
    [
        new("OUT", "1802Z", "Off block", "Parking brake released and the aircraft moving on the ground.", "Taxi out 12 min"),
        new("OFF", "1814Z", "Takeoff", "The wheels leave the runway.", "Airborne 47 min"),
        new("ON", "1901Z", "Landing", "The wheels are back on the ground.", "Taxi in 8 min"),
        new("IN", "1909Z", "Block in", "Parked with the brake set and the engines off.", null),
    ];

    public static readonly Status[] Statuses =
    [
        new("on-time", "On time", "Off block within the schedule window."),
        new("delayed", "Delayed 25 min", "Late off block, carried down the rotation."),
        new("diverted", "Diverted KCOS", "Landed somewhere other than planned."),
        new("cancelled", "Cancelled", "Taken off the board by a dispatcher."),
    ];

    // The operations-board demonstration: one aircraft's rotation, so the delay on S4602 follows from S4601.
    public static readonly BoardRow[] Board =
    [
        new("S4601", "KMKE", "KORD", "A20N", "1800Z", "1802Z", "1909Z", "Block in", "on-time", "On time"),
        new("S4602", "KORD", "KDEN", "A20N", "1925Z", "1950Z", "—", "Taxi out", "delayed", "Delayed 25 min"),
        new("S4603", "KDEN", "KORD", "A20N", "2310Z", "—", "—", "At gate", "delayed", "Delayed 25 min"),
        new("S4610", "KMKE", "KMSP", "B738", "1915Z", "—", "—", "At gate", "cancelled", "Cancelled"),
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
        new("Sign in on the desktop app", "Install S4 OPS for Windows and sign in once with your account. It remembers you."),
        new("Fly normally", "Load Microsoft Flight Simulator 2024 and fly the way you always do. There is nothing to start or stop."),
        new("Phases are detected", "The app reads the aircraft state from the sim and records OUT, OFF, ON and IN as they happen."),
        new("The flight is logged", "Times, route and aircraft go into your logbook, with block and flight time worked out for you."),
    ];

    public static readonly Feature[] Pilots =
    [
        new("LOG", "Logbook", "Every flight recorded with times, route and aircraft, from a replay or the live sim."),
        new("DSP", "Dispatch", "Import a SimBrief plan with your username saved to your account."),
        new("TRK", "Tracking", "See the flight's phase and progress as you fly."),
        new("WX", "Weather", "Briefings for departure and destination."),
        new("WSP", "Workspaces", "Your own flying and every airline you join, under one account."),
    ];

    public static readonly Feature[] Airlines =
    [
        new("CRW", "Crew roster", "Pilots, dispatchers and administrators, with roles you control."),
        new("INV", "Invitations", "A code your invitee redeems once. Nothing leaks in email."),
        new("DSP", "Shared dispatch", "The same board for everyone on the schedule."),
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
