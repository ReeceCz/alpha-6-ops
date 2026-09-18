using Alpha6Ops.Identity;

namespace Alpha6Ops.Accounts;

public sealed class UserAccount
{
    public Guid Id { get; set; }
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool EmailVerified { get; set; }
    public AccountStatus Status { get; set; }
    public PersonalPlan Plan { get; set; }
    public string SubscriptionStatus { get; set; } = "active";
    public DateTimeOffset? SubscriptionExpiresAt { get; set; }
    public Guid? LastAirlineId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class UserProfileRecord
{
    public Guid UserId { get; set; }
    public string SimBriefUsername { get; set; } = "";
    public string Callsign { get; set; } = "";
    public string HomeBaseIcao { get; set; } = "";
    public string WeightUnit { get; set; } = "LBS";
    public string AltitudeUnit { get; set; } = "FT";
    public string LandingDistanceUnit { get; set; } = "FT";
    public string PreferredWorkspace { get; set; } = "last_used";
    public string TimeZone { get; set; } = "";
    public string AvatarInitials { get; set; } = "";
    public DateTimeOffset? LastSeenAt { get; set; }
    public string LastSeenVersion { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class VirtualAirline
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Callsign { get; set; } = "";
    public string Status { get; set; } = "active";
    public Guid FounderUserId { get; set; }
    public Guid OwnerUserId { get; set; }
    public AirlinePlan Plan { get; set; }
    public string SubscriptionStatus { get; set; } = "active";
    public DateTimeOffset? SubscriptionExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Membership
{
    public Guid Id { get; set; }
    public Guid AirlineId { get; set; }
    public Guid UserId { get; set; }
    public MembershipStatus Status { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public List<MembershipRole> Roles { get; set; } = [];
}

// Owner is derived exclusively from VirtualAirline.OwnerUserId, avoiding two competing owners.
public sealed class MembershipRole
{
    public Guid AirlineId { get; set; }
    public Guid MembershipId { get; set; }
    public AirlineRole Role { get; set; }
}

public sealed class Invitation
{
    public Guid Id { get; set; }
    public Guid AirlineId { get; set; }
    public Guid IssuedByUserId { get; set; }
    public string Email { get; set; } = "";
    public int[] Roles { get; set; } = [];
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "pending";
    public Guid? AcceptedByUserId { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AirlineRoute
{
    public Guid Id { get; set; }
    public Guid AirlineId { get; set; }
    public string FlightNumber { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
    public TimeOnly DepartureUtc { get; set; }
    public int BlockMinutes { get; set; }
    public int DaysOfWeek { get; set; }
    public string AircraftType { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AirlineAircraft
{
    public Guid Id { get; set; }
    public Guid AirlineId { get; set; }
    public string Registration { get; set; } = "";
    public string TypeIcao { get; set; } = "";
    public string Name { get; set; } = "";
    public string HomeBase { get; set; } = "";
    public string Status { get; set; } = "active";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PilotFlight
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Source { get; set; } = "import";
    public Guid? ImportBatchId { get; set; }
    public string FlightNumber { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
    public string AircraftType { get; set; } = "";
    public string Registration { get; set; } = "";
    public DateTimeOffset DepartureUtc { get; set; }
    public DateTimeOffset? ArrivalUtc { get; set; }
    public int BlockMinutes { get; set; }
    public int? FlightMinutes { get; set; }
    public int? DistanceNm { get; set; }
    public int? LandingRateFpm { get; set; }
    public int? FuelUsedKg { get; set; }
    public string Network { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

// One image per owner and kind (a pilot's avatar, an airline's logo). Validated before storage; served by hash.
public sealed class MediaBlob
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public Guid OwnerId { get; set; }
    public string ContentType { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
    public string Sha256 { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AuditEvent
{
    public Guid Id { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? AirlineId { get; set; }
    public string Action { get; set; } = "";
    public Guid TargetId { get; set; }
    public string Details { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
