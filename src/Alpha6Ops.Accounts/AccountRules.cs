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

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$")]
    private static partial Regex SlugPattern();
    [GeneratedRegex("^([A-Z0-9]{3,4})?$")]
    private static partial Regex HomeBasePattern();
    [GeneratedRegex("^[A-Z0-9]{0,3}$")]
    private static partial Regex InitialsPattern();
}
