using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Alpha6Ops.Identity;

namespace Alpha6Ops.Accounts;

public static partial class AccountRules
{
    public static string Email(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length > 254 || !MailAddress.TryCreate(value, out var parsed) || parsed.Address != value)
            throw new IdentityException("invalid_email", "Enter a valid email address.", 400);
        return value.ToUpperInvariant();
    }

    public static AirlineRole[] Roles(AirlineRole[]? roles)
    {
        if (roles is null || roles.Length == 0 || roles.Length > 3 ||
            roles.Any(x => !Enum.IsDefined(x) || x == AirlineRole.Owner))
            throw new IdentityException("invalid_roles", "Choose Pilot, Dispatcher, or Administrator. Ownership requires a transfer.", 400);
        return roles.Distinct().Order().ToArray();
    }

    public static void RequireRecentMfa(ActorIdentity actor, DateTimeOffset now)
    {
        if (!actor.EmailVerified) throw new IdentityException("email_verification_required", "Verify your email first.");
        if (!actor.HasMfa || actor.AuthenticatedAt is not { } at || at > now.AddSeconds(30) || at < now.AddMinutes(-5))
            throw new IdentityException("mfa_required", "Confirm your identity with a passkey or MFA to continue.");
    }

    public static string Slug(string value)
    {
        value = (value ?? "").Trim().ToLowerInvariant();
        if (!SlugPattern().IsMatch(value)) throw new IdentityException("invalid_slug", "Use 3 to 64 lowercase letters, digits, and internal hyphens.", 400);
        return value;
    }

    public static string Text(string value, int limit, string field)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0 || value.Length > limit || value.Any(char.IsControl))
            throw new IdentityException("invalid_input", $"{field} must contain 1 to {limit} characters without control characters.", 400);
        return value;
    }

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static readonly string[] WeightUnits = ["LBS", "KG"];
    public static readonly string[] LengthUnits = ["FT", "M"];
    public static readonly string[] WorkspacePreferences = ["last_used", "personal", "portal"];

    public static UpdateProfileRequest Profile(UpdateProfileRequest request)
    {
        static IdentityException Invalid(string message) => new("invalid_profile", message, 400);
        var simBrief = (request.SimBriefUsername ?? "").Trim();
        if (simBrief.Length is 1 or > 80 || simBrief.Any(char.IsControl) || simBrief.Any(char.IsWhiteSpace))
            throw Invalid("SimBrief username must be 2 to 80 characters without spaces.");
        var callsign = (request.Callsign ?? "").Trim().ToUpperInvariant();
        if (callsign.Length > 20 || callsign.Any(char.IsControl)) throw Invalid("Callsign must be 20 characters or fewer.");
        var homeBase = (request.HomeBaseIcao ?? "").Trim().ToUpperInvariant();
        if (!HomeBasePattern().IsMatch(homeBase)) throw Invalid("Home base must be a 3 or 4 character airport code.");
        var weight = (request.WeightUnit ?? "").Trim().ToUpperInvariant();
        var altitude = (request.AltitudeUnit ?? "").Trim().ToUpperInvariant();
        var landing = (request.LandingDistanceUnit ?? "").Trim().ToUpperInvariant();
        if (!WeightUnits.Contains(weight) || !LengthUnits.Contains(altitude) || !LengthUnits.Contains(landing))
            throw Invalid("Choose LBS or KG for weight and FT or M for altitude and landing distance.");
        var workspace = (request.PreferredWorkspace ?? "").Trim().ToLowerInvariant();
        if (!WorkspacePreferences.Contains(workspace)) throw Invalid("Preferred workspace must be last_used, personal or portal.");
        var timeZone = (request.TimeZone ?? "").Trim();
        if (timeZone.Length > 64 || (timeZone.Length > 0 && !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _)))
            throw Invalid("Choose a known time zone or leave it blank.");
        var initials = (request.AvatarInitials ?? "").Trim().ToUpperInvariant();
        if (!InitialsPattern().IsMatch(initials)) throw Invalid("Initials must be up to 3 letters or digits.");
        return new(simBrief, callsign, homeBase, weight, altitude, landing, workspace, timeZone, initials);
    }

    public static string ClientVersion(string? userAgent)
    {
        var value = (userAgent ?? "").Trim();
        var marker = value.IndexOf("Alpha6OPS/", StringComparison.Ordinal);
        if (marker >= 0) value = value[(marker + "Alpha6OPS/".Length)..].Split(' ')[0];
        value = new string(value.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+').ToArray());
        return value.Length > 32 ? value[..32] : value;
    }

    public const int AllDays = 127;

    public static RouteRequest Route(RouteRequest request)
    {
        static IdentityException Invalid(string message) => new("invalid_route", message, 400);
        var flight = (request.FlightNumber ?? "").Trim().ToUpperInvariant().Replace(" ", "");
        if (!FlightNumberPattern().IsMatch(flight)) throw Invalid("Flight number must be 2 to 10 letters or digits, such as A6101.");
        var origin = (request.Origin ?? "").Trim().ToUpperInvariant();
        var destination = (request.Destination ?? "").Trim().ToUpperInvariant();
        if (!AirportPattern().IsMatch(origin) || !AirportPattern().IsMatch(destination)) throw Invalid("Origin and destination must be 3 or 4 character airport codes.");
        if (origin == destination) throw Invalid("Origin and destination must differ.");
        if (!TimeOnly.TryParseExact((request.DepartureUtc ?? "").Trim(), "HH:mm", out var departure)) throw Invalid("Departure must be a UTC time such as 14:35.");
        if (request.BlockMinutes is < 5 or > 1440) throw Invalid("Block time must be between 5 minutes and 24 hours.");
        if (request.DaysOfWeek is < 1 or > AllDays) throw Invalid("Choose at least one day of the week.");
        var type = (request.AircraftType ?? "").Trim().ToUpperInvariant();
        if (!TypePattern().IsMatch(type)) throw Invalid("Aircraft type must be a 2 to 4 character ICAO designator such as A20N, or blank.");
        var notes = Notes(request.Notes, Invalid);
        return new(flight, origin, destination, departure.ToString("HH:mm"), request.BlockMinutes, request.DaysOfWeek, type, notes, request.Active);
    }

    public static AircraftRequest Aircraft(AircraftRequest request)
    {
        static IdentityException Invalid(string message) => new("invalid_aircraft", message, 400);
        var registration = (request.Registration ?? "").Trim().ToUpperInvariant();
        if (!RegistrationPattern().IsMatch(registration)) throw Invalid("Registration must be 2 to 10 letters, digits or hyphens, such as N123A6.");
        var type = (request.TypeIcao ?? "").Trim().ToUpperInvariant();
        if (type.Length == 0 || !TypePattern().IsMatch(type)) throw Invalid("Type must be a 2 to 4 character ICAO designator such as B738.");
        var name = (request.Name ?? "").Trim();
        if (name.Length > 100 || name.Any(char.IsControl)) throw Invalid("Name must be 100 characters or fewer.");
        var homeBase = (request.HomeBase ?? "").Trim().ToUpperInvariant();
        if (!HomeBasePattern().IsMatch(homeBase)) throw Invalid("Home base must be a 3 or 4 character airport code, or blank.");
        var status = (request.Status ?? "").Trim().ToLowerInvariant();
        if (!AircraftStatuses.All.Contains(status)) throw Invalid("Status must be active, maintenance or retired.");
        return new(registration, type, name, homeBase, status, Notes(request.Notes, Invalid));
    }

    // Days in CSV are digits 1 (Monday) to 7 (Sunday), in any order, e.g. "12345" or "67".
    public static int Days(string value)
    {
        var mask = 0;
        foreach (var c in (value ?? "").Trim())
        {
            if (c is < '1' or > '7') throw new IdentityException("invalid_route", "Days must use the digits 1 (Monday) to 7 (Sunday).", 400);
            mask |= 1 << (c - '1');
        }
        return mask;
    }

    public static string DaysText(int mask) => string.Concat(Enumerable.Range(0, 7).Where(i => (mask & (1 << i)) != 0).Select(i => (char)('1' + i)));

    private static string Notes(string? value, Func<string, IdentityException> invalid)
    {
        var notes = (value ?? "").Trim();
        if (notes.Length > 500 || notes.Any(c => char.IsControl(c) && c != '\n')) throw invalid("Notes must be 500 characters or fewer.");
        return notes;
    }

    [GeneratedRegex("^[A-Z0-9]{2,10}$")]
    private static partial Regex FlightNumberPattern();
    [GeneratedRegex("^[A-Z0-9]{3,4}$")]
    private static partial Regex AirportPattern();
    [GeneratedRegex("^([A-Z0-9]{2,4})?$")]
    private static partial Regex TypePattern();
    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,8}[A-Z0-9]$")]
    private static partial Regex RegistrationPattern();
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$")]
    private static partial Regex SlugPattern();
    [GeneratedRegex("^([A-Z0-9]{3,4})?$")]
    private static partial Regex HomeBasePattern();
    [GeneratedRegex("^[A-Z0-9]{0,3}$")]
    private static partial Regex InitialsPattern();
}
