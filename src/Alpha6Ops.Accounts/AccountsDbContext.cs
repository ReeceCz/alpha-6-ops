using Microsoft.EntityFrameworkCore;

namespace Alpha6Ops.Accounts;

public sealed class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<VirtualAirline> Airlines => Set<VirtualAirline>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<UserProfileRecord> Profiles => Set<UserProfileRecord>();
    public DbSet<AirlineRoute> Routes => Set<AirlineRoute>();
    public DbSet<AirlineAircraft> Fleet => Set<AirlineAircraft>();
    public DbSet<PilotFlight> Flights => Set<PilotFlight>();
    public DbSet<MediaBlob> Media => Set<MediaBlob>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var user = model.Entity<UserAccount>();
        user.ToTable("user_account"); user.HasKey(x => x.Id);
        user.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        user.Property(x => x.Issuer).HasMaxLength(255); user.Property(x => x.Subject).HasMaxLength(255);
        user.Property(x => x.DisplayName).HasMaxLength(150); user.Property(x => x.Email).HasMaxLength(254);
        user.Property(x => x.SubscriptionStatus).HasMaxLength(30);

        var profile = model.Entity<UserProfileRecord>();
        profile.ToTable("user_profile"); profile.HasKey(x => x.UserId);
        profile.Property(x => x.SimBriefUsername).HasMaxLength(80); profile.Property(x => x.Callsign).HasMaxLength(20);
        profile.Property(x => x.HomeBaseIcao).HasMaxLength(4); profile.Property(x => x.WeightUnit).HasMaxLength(3);
        profile.Property(x => x.AltitudeUnit).HasMaxLength(2); profile.Property(x => x.LandingDistanceUnit).HasMaxLength(2);
        profile.Property(x => x.PreferredWorkspace).HasMaxLength(20); profile.Property(x => x.TimeZone).HasMaxLength(64);
        profile.Property(x => x.AvatarInitials).HasMaxLength(3); profile.Property(x => x.LastSeenVersion).HasMaxLength(32);
        profile.HasOne<UserAccount>().WithOne().HasForeignKey<UserProfileRecord>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        var airline = model.Entity<VirtualAirline>();
        airline.ToTable("virtual_airline"); airline.HasKey(x => x.Id); airline.HasIndex(x => x.Slug).IsUnique();
        airline.Property(x => x.Slug).HasMaxLength(64); airline.Property(x => x.Name).HasMaxLength(100);
        airline.Property(x => x.Callsign).HasMaxLength(20); airline.Property(x => x.Status).HasMaxLength(30);
        airline.Property(x => x.SubscriptionStatus).HasMaxLength(30);
        airline.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.FounderUserId).OnDelete(DeleteBehavior.Restrict);
        airline.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);

        var member = model.Entity<Membership>();
        member.ToTable("membership"); member.HasKey(x => x.Id);
        member.HasAlternateKey(x => new { x.Id, x.AirlineId });
        member.HasIndex(x => new { x.AirlineId, x.UserId }).IsUnique();
        member.HasOne<VirtualAirline>().WithMany().HasForeignKey(x => x.AirlineId).OnDelete(DeleteBehavior.Restrict);
        member.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        member.HasMany(x => x.Roles).WithOne().HasForeignKey(x => new { x.MembershipId, x.AirlineId })
            .HasPrincipalKey(x => new { x.Id, x.AirlineId }).OnDelete(DeleteBehavior.Cascade);
        var role = model.Entity<MembershipRole>();
        role.ToTable("membership_role"); role.HasKey(x => new { x.MembershipId, x.AirlineId, x.Role });

        var invitation = model.Entity<Invitation>();
        invitation.ToTable("invitation"); invitation.HasKey(x => x.Id);
        invitation.HasIndex(x => x.TokenHash).IsUnique(); invitation.HasIndex(x => new { x.AirlineId, x.Email });
        invitation.Property(x => x.Email).HasMaxLength(254); invitation.Property(x => x.TokenHash).HasMaxLength(64);
        invitation.Property(x => x.Status).HasMaxLength(30);
        invitation.HasOne<VirtualAirline>().WithMany().HasForeignKey(x => x.AirlineId).OnDelete(DeleteBehavior.Restrict);
        invitation.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.IssuedByUserId).OnDelete(DeleteBehavior.Restrict);
        invitation.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.AcceptedByUserId).OnDelete(DeleteBehavior.Restrict);

        var route = model.Entity<AirlineRoute>();
        route.ToTable("airline_route"); route.HasKey(x => x.Id);
        route.HasIndex(x => new { x.AirlineId, x.FlightNumber, x.Origin, x.Destination }).IsUnique();
        route.Property(x => x.FlightNumber).HasMaxLength(10); route.Property(x => x.Origin).HasMaxLength(4);
        route.Property(x => x.Destination).HasMaxLength(4); route.Property(x => x.AircraftType).HasMaxLength(4);
        route.Property(x => x.Notes).HasMaxLength(500);
        route.HasOne<VirtualAirline>().WithMany().HasForeignKey(x => x.AirlineId).OnDelete(DeleteBehavior.Restrict);

        var aircraft = model.Entity<AirlineAircraft>();
        aircraft.ToTable("airline_aircraft"); aircraft.HasKey(x => x.Id);
        aircraft.HasIndex(x => new { x.AirlineId, x.Registration }).IsUnique();
        aircraft.Property(x => x.Registration).HasMaxLength(10); aircraft.Property(x => x.TypeIcao).HasMaxLength(4);
        aircraft.Property(x => x.Name).HasMaxLength(100); aircraft.Property(x => x.HomeBase).HasMaxLength(4);
        aircraft.Property(x => x.Status).HasMaxLength(20); aircraft.Property(x => x.Notes).HasMaxLength(500);
        aircraft.HasOne<VirtualAirline>().WithMany().HasForeignKey(x => x.AirlineId).OnDelete(DeleteBehavior.Restrict);

        var flight = model.Entity<PilotFlight>();
        flight.ToTable("pilot_flight"); flight.HasKey(x => x.Id);
        flight.HasIndex(x => new { x.UserId, x.DepartureUtc, x.FlightNumber, x.Origin, x.Destination }).IsUnique();
        flight.HasIndex(x => x.ImportBatchId);
        flight.Property(x => x.Source).HasMaxLength(20); flight.Property(x => x.FlightNumber).HasMaxLength(10);
        flight.Property(x => x.Origin).HasMaxLength(4); flight.Property(x => x.Destination).HasMaxLength(4);
        flight.Property(x => x.AircraftType).HasMaxLength(4); flight.Property(x => x.Registration).HasMaxLength(10);
        flight.Property(x => x.Network).HasMaxLength(20); flight.Property(x => x.Notes).HasMaxLength(500);
        flight.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        var media = model.Entity<MediaBlob>();
        media.ToTable("media_blob"); media.HasKey(x => x.Id);
        media.HasIndex(x => new { x.Kind, x.OwnerId }).IsUnique();
        media.Property(x => x.Kind).HasMaxLength(20); media.Property(x => x.ContentType).HasMaxLength(40);
        media.Property(x => x.Sha256).HasMaxLength(64);

        var keys = model.Entity<DataProtectionKey>();
        keys.ToTable("data_protection_key"); keys.HasKey(x => x.Id); keys.Property(x => x.FriendlyName).HasMaxLength(120);

        var audit = model.Entity<AuditEvent>(); audit.ToTable("audit_event"); audit.HasKey(x => x.Id);
        audit.Property(x => x.Action).HasMaxLength(80); audit.Property(x => x.Details).HasMaxLength(2000);
        audit.HasIndex(x => new { x.AirlineId, x.CreatedAt });
        audit.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        audit.HasOne<VirtualAirline>().WithMany().HasForeignKey(x => x.AirlineId).OnDelete(DeleteBehavior.Restrict);
    }
}
