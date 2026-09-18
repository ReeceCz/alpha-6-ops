using System.Text.Json.Serialization;

namespace Alpha6Ops.Identity;

[JsonConverter(typeof(JsonStringEnumConverter<AccountStatus>))]
public enum AccountStatus { Active, Suspended }
[JsonConverter(typeof(JsonStringEnumConverter<PersonalPlan>))]
public enum PersonalPlan { Free, Premium }
[JsonConverter(typeof(JsonStringEnumConverter<AirlinePlan>))]
public enum AirlinePlan { Community, Pro }
[JsonConverter(typeof(JsonStringEnumConverter<MembershipStatus>))]
public enum MembershipStatus { Active, Suspended, Removed }
[JsonConverter(typeof(JsonStringEnumConverter<AirlineRole>))]
public enum AirlineRole { Pilot, Dispatcher, Administrator, Owner }

public sealed record AccountProfile(Guid Id, string DisplayName, string Email, bool EmailVerified, AccountStatus Status);
public sealed record PersonalEntitlement(PersonalPlan Plan, string Status, DateTimeOffset? ExpiresAt);
public sealed record AirlineWorkspace(Guid Id, string Slug, string Name, string Callsign,
    AirlinePlan Plan, string SubscriptionStatus, MembershipStatus MembershipStatus,
    AirlineRole[] Roles, bool IsFounder, string[] Capabilities, string LogoUrl = "");
public sealed record WorkspaceSelection(Guid? AirlineId)
{
    public static WorkspaceSelection Personal => new((Guid?)null);
}
public sealed record BootstrapResponse(AccountProfile Account, PersonalEntitlement PersonalEntitlement,
    string[] PersonalCapabilities, AirlineWorkspace[] Airlines, WorkspaceSelection LastWorkspace,
    DateTimeOffset ServerTime, DateTimeOffset OfflineExpiresAt, UserProfile? Profile = null);
// Pilot preferences that follow the account between installations. Never security-relevant.
public sealed record UserProfile(string SimBriefUsername, string Callsign, string HomeBaseIcao,
    string WeightUnit, string AltitudeUnit, string LandingDistanceUnit, string PreferredWorkspace, string TimeZone,
    string AvatarInitials, DateTimeOffset? LastSeenAt, string LastSeenVersion, DateTimeOffset? UpdatedAt, string AvatarUrl = "")
{
    public static UserProfile Default { get; } = new("", "", "", "LBS", "FT", "FT", "last_used", "", "", null, "", null);
}
// Pilot logbook kept with the account. Times are UTC; block and flight time are minutes.
public sealed record FlightLogEntry(Guid Id, string Source, string FlightNumber, string Origin, string Destination, string AircraftType,
    string Registration, DateTimeOffset DepartureUtc, DateTimeOffset? ArrivalUtc, int BlockMinutes, int? FlightMinutes, int? DistanceNm,
    int? LandingRateFpm, int? FuelUsedKg, string Network, string Notes, Guid? ImportBatchId, DateTimeOffset CreatedAt);
public sealed record FlightLogRequest(string FlightNumber, string Origin, string Destination, string AircraftType, string Registration,
    DateTimeOffset DepartureUtc, DateTimeOffset? ArrivalUtc, int BlockMinutes, int? FlightMinutes, int? DistanceNm, int? LandingRateFpm,
    int? FuelUsedKg, string? Network, string? Notes);
public sealed record LogbookImportRequest(string Csv, string? FileName = null);
public sealed record LogbookImportResult(Guid BatchId, int Created, int Duplicates, int Skipped, string[] Errors, string[] Columns);
public sealed record LogbookSummary(int Flights, int BlockMinutes, int Airports, string[] TopAircraft, DateTimeOffset? FirstFlight, DateTimeOffset? LastFlight);
public sealed record LogbookPage(FlightLogEntry[] Entries, int Total, int Page, int PageSize);
public static class MediaKinds
{
    public const string Avatar = "avatar";
    public const string AirlineLogo = "airline-logo";
}
public sealed record UpdateProfileRequest(string SimBriefUsername, string Callsign, string HomeBaseIcao,
    string WeightUnit, string AltitudeUnit, string LandingDistanceUnit, string PreferredWorkspace, string TimeZone, string AvatarInitials);
public sealed record SetPersonalPlanRequest(PersonalPlan Plan);
public sealed record SetAirlinePlanRequest(AirlinePlan Plan);
public sealed record ReleaseInfo(string Version, string DownloadUrl, string Sha256);
public sealed record ActivityEntry(Guid Id, string Action, string Details, DateTimeOffset CreatedAt, string ActorDisplayName);
public static class SubscriptionStatuses
{
    public const string Active = "active";
    // Granted without payment during early access; treated as current until billing replaces it.
    public const string Complimentary = "complimentary";
    public static bool IsCurrent(string status, DateTimeOffset? expiresAt, DateTimeOffset now) =>
        status is Active or Complimentary && (expiresAt is null || expiresAt > now);
}
// Airline operations data: schedules and fleet. Days use a bitmask, Monday = 1 through Sunday = 64.
public sealed record RouteRequest(string FlightNumber, string Origin, string Destination, string DepartureUtc, int BlockMinutes,
    int DaysOfWeek, string AircraftType, string? Notes, bool Active);
public sealed record RouteResponse(Guid Id, string FlightNumber, string Origin, string Destination, string DepartureUtc, int BlockMinutes,
    int DaysOfWeek, string AircraftType, string Notes, bool Active, DateTimeOffset UpdatedAt);
public sealed record AircraftRequest(string Registration, string TypeIcao, string Name, string HomeBase, string Status, string? Notes);
public sealed record AircraftResponse(Guid Id, string Registration, string TypeIcao, string Name, string HomeBase, string Status, string Notes, DateTimeOffset UpdatedAt);
public sealed record ScheduleImportRequest(string Csv);
public sealed record ScheduleImportResult(int Created, int Updated, int Skipped, string[] Errors);
public static class AircraftStatuses
{
    public const string Active = "active";
    public const string Maintenance = "maintenance";
    public const string Retired = "retired";
    public static readonly string[] All = [Active, Maintenance, Retired];
}
public sealed record CreateAirlineRequest(string Name, string Slug, string Callsign);
public sealed record InviteMemberRequest(string Email, AirlineRole[] Roles);
public sealed record ChangeRolesRequest(AirlineRole[] Roles);
public sealed record TransferOwnershipRequest(Guid NewOwnerUserId);
public sealed record MemberResponse(Guid Id, Guid UserId, string DisplayName, string Email,
    MembershipStatus Status, AirlineRole[] Roles);
public sealed record InvitationResponse(Guid Id, string Email, AirlineRole[] Roles, DateTimeOffset ExpiresAt,
    string Status);
// Raw tokens are returned only once to the issuing administrator, never in listings or logs.
public sealed record IssuedInvitation(InvitationResponse Invitation, string Token);
public sealed record ActorIdentity(string Issuer, string Subject, string DisplayName, string Email,
    bool EmailVerified, bool HasMfa, DateTimeOffset? AuthenticatedAt);

public static class Capabilities
{
    public const string Logbook = "personal.logbook";
    public const string Dispatch = "personal.dispatch";
    public const string Tracker = "personal.tracker";
    public const string Weather = "personal.weather";
    public const string Import = "personal.import";
    public const string AirlineRead = "airline.read";
    public const string DispatchRead = "airline.dispatch.read";
    public const string DispatchManage = "airline.dispatch.manage";
    // Schedules and fleet: everyday operations work for dispatchers and up, without a security check.
    public const string OperationsManage = "airline.operations.manage";
    public const string MembersManage = "airline.members.manage";
    public const string OwnershipManage = "airline.ownership.manage";
    public static string[] Personal => [Logbook, Dispatch, Tracker, Weather, Import];

    public static string[] ForMembership(MembershipStatus status, IEnumerable<AirlineRole> roles)
    {
        if (status != MembershipStatus.Active) return [];
        var set = roles.ToHashSet();
        var result = new List<string> { AirlineRead, DispatchRead };
        if (set.Overlaps([AirlineRole.Dispatcher, AirlineRole.Administrator, AirlineRole.Owner])) { result.Add(DispatchManage); result.Add(OperationsManage); }
        if (set.Overlaps([AirlineRole.Administrator, AirlineRole.Owner])) result.Add(MembersManage);
        if (set.Contains(AirlineRole.Owner)) result.Add(OwnershipManage);
        return result.ToArray();
    }
}

public sealed class IdentityException(string code, string message, int statusCode = 403) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
