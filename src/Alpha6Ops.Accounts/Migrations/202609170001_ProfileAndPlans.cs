using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alpha6Ops.Accounts.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("202609170001_ProfileAndPlans")]
public sealed class ProfileAndPlans : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ProfileAndPlansModel.Build(modelBuilder);

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE user_profile (
            "UserId" uuid CONSTRAINT "PK_user_profile" PRIMARY KEY CONSTRAINT "FK_user_profile_user_account_UserId" REFERENCES user_account ("Id") ON DELETE RESTRICT,
            "SimBriefUsername" varchar(80) NOT NULL, "Callsign" varchar(20) NOT NULL,
            "HomeBaseIcao" varchar(4) NOT NULL CHECK ("HomeBaseIcao" ~ '^([A-Z0-9]{3,4})?$'),
            "WeightUnit" varchar(3) NOT NULL CHECK ("WeightUnit" IN ('LBS','KG')),
            "AltitudeUnit" varchar(2) NOT NULL CHECK ("AltitudeUnit" IN ('FT','M')),
            "LandingDistanceUnit" varchar(2) NOT NULL CHECK ("LandingDistanceUnit" IN ('FT','M')),
            "PreferredWorkspace" varchar(20) NOT NULL CHECK ("PreferredWorkspace" IN ('last_used','personal')),
            "TimeZone" varchar(64) NOT NULL,
            "AvatarInitials" varchar(3) NOT NULL CHECK ("AvatarInitials" ~ '^[A-Z0-9]{0,3}$'),
            "LastSeenAt" timestamptz, "LastSeenVersion" varchar(32) NOT NULL, "UpdatedAt" timestamptz);

        -- Complimentary grants count as current subscriptions until billing replaces them.
        ALTER TABLE user_account ADD CONSTRAINT user_account_subscription_status
            CHECK ("SubscriptionStatus" IN ('active','complimentary','canceled','expired'));
        ALTER TABLE virtual_airline ADD CONSTRAINT virtual_airline_subscription_status
            CHECK ("SubscriptionStatus" IN ('active','complimentary','canceled','expired'));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE virtual_airline DROP CONSTRAINT virtual_airline_subscription_status;
        ALTER TABLE user_account DROP CONSTRAINT user_account_subscription_status;
        DROP TABLE user_profile;
        """);
}

// Composes the frozen initial model with the profile entity. Later migrations layer on this the same way.
internal static class ProfileAndPlansModel
{
    internal static void Build(ModelBuilder model)
    {
        InitialAccountsModel.Build(model);
        model.Entity("Alpha6Ops.Accounts.UserProfileRecord", b =>
        {
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<string>("AltitudeUnit").IsRequired().HasMaxLength(2).HasColumnType("character varying(2)");
            b.Property<string>("AvatarInitials").IsRequired().HasMaxLength(3).HasColumnType("character varying(3)");
            b.Property<string>("Callsign").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("HomeBaseIcao").IsRequired().HasMaxLength(4).HasColumnType("character varying(4)");
            b.Property<string>("LandingDistanceUnit").IsRequired().HasMaxLength(2).HasColumnType("character varying(2)");
            b.Property<DateTimeOffset?>("LastSeenAt").HasColumnType("timestamp with time zone");
            b.Property<string>("LastSeenVersion").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
            b.Property<string>("PreferredWorkspace").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("SimBriefUsername").IsRequired().HasMaxLength(80).HasColumnType("character varying(80)");
            b.Property<string>("TimeZone").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<DateTimeOffset?>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("WeightUnit").IsRequired().HasMaxLength(3).HasColumnType("character varying(3)");
            b.HasKey("UserId"); b.ToTable("user_profile");
        });
        model.Entity("Alpha6Ops.Accounts.UserProfileRecord", b =>
            b.HasOne("Alpha6Ops.Accounts.UserAccount", null).WithOne().HasForeignKey("Alpha6Ops.Accounts.UserProfileRecord", "UserId")
                .OnDelete(DeleteBehavior.Restrict).IsRequired());
    }
}
