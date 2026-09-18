using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alpha6Ops.Accounts.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("202609180002_LogbookAndMedia")]
public sealed class LogbookAndMedia : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => LogbookAndMediaModel.Build(modelBuilder);

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE pilot_flight (
            "Id" uuid CONSTRAINT "PK_pilot_flight" PRIMARY KEY,
            "UserId" uuid NOT NULL CONSTRAINT "FK_pilot_flight_user_account_UserId" REFERENCES user_account ("Id") ON DELETE RESTRICT,
            "Source" varchar(20) NOT NULL CHECK ("Source" IN ('import','desktop','manual')),
            "ImportBatchId" uuid,
            "FlightNumber" varchar(10) NOT NULL CHECK ("FlightNumber" ~ '^([A-Z0-9]{2,10})?$'),
            "Origin" varchar(4) NOT NULL CHECK ("Origin" ~ '^[A-Z0-9]{3,4}$'),
            "Destination" varchar(4) NOT NULL CHECK ("Destination" ~ '^[A-Z0-9]{3,4}$'),
            "AircraftType" varchar(4) NOT NULL CHECK ("AircraftType" ~ '^([A-Z0-9]{2,4})?$'),
            "Registration" varchar(10) NOT NULL,
            "DepartureUtc" timestamptz NOT NULL, "ArrivalUtc" timestamptz,
            "BlockMinutes" integer NOT NULL CHECK ("BlockMinutes" BETWEEN 0 AND 2880),
            "FlightMinutes" integer CHECK ("FlightMinutes" BETWEEN 0 AND 2880),
            "DistanceNm" integer CHECK ("DistanceNm" BETWEEN 0 AND 20000),
            "LandingRateFpm" integer CHECK ("LandingRateFpm" BETWEEN -5000 AND 5000),
            "FuelUsedKg" integer CHECK ("FuelUsedKg" BETWEEN 0 AND 400000),
            "Network" varchar(20) NOT NULL, "Notes" varchar(500) NOT NULL,
            "CreatedAt" timestamptz NOT NULL);
        CREATE UNIQUE INDEX "IX_pilot_flight_UserId_DepartureUtc_FlightNumber_Origin_Destination" ON pilot_flight ("UserId", "DepartureUtc", "FlightNumber", "Origin", "Destination");
        CREATE INDEX "IX_pilot_flight_ImportBatchId" ON pilot_flight ("ImportBatchId");

        CREATE TABLE media_blob (
            "Id" uuid CONSTRAINT "PK_media_blob" PRIMARY KEY,
            "Kind" varchar(20) NOT NULL CHECK ("Kind" IN ('avatar','airline-logo')),
            "OwnerId" uuid NOT NULL,
            "ContentType" varchar(40) NOT NULL CHECK ("ContentType" IN ('image/png','image/jpeg','image/webp')),
            "Bytes" bytea NOT NULL CHECK (octet_length("Bytes") BETWEEN 1 AND 1048576),
            "Sha256" varchar(64) NOT NULL,
            "Width" integer NOT NULL, "Height" integer NOT NULL,
            "UpdatedAt" timestamptz NOT NULL);
        CREATE UNIQUE INDEX "IX_media_blob_Kind_OwnerId" ON media_blob ("Kind", "OwnerId");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE media_blob;
        DROP TABLE pilot_flight;
        """);
}

internal static class LogbookAndMediaModel
{
    internal static void Build(ModelBuilder model)
    {
        ScheduleAndFleetModel.Build(model);
        model.Entity("Alpha6Ops.Accounts.PilotFlight", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<string>("AircraftType").IsRequired().HasMaxLength(4).HasColumnType("character varying(4)");
            b.Property<DateTimeOffset?>("ArrivalUtc").HasColumnType("timestamp with time zone");
            b.Property<int>("BlockMinutes").HasColumnType("integer");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("DepartureUtc").HasColumnType("timestamp with time zone");
            b.Property<string>("Destination").IsRequired().HasMaxLength(4).HasColumnType("character varying(4)");
            b.Property<int?>("DistanceNm").HasColumnType("integer");
            b.Property<int?>("FlightMinutes").HasColumnType("integer");
            b.Property<string>("FlightNumber").IsRequired().HasMaxLength(10).HasColumnType("character varying(10)");
            b.Property<int?>("FuelUsedKg").HasColumnType("integer");
            b.Property<Guid?>("ImportBatchId").HasColumnType("uuid");
            b.Property<int?>("LandingRateFpm").HasColumnType("integer");
            b.Property<string>("Network").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("Notes").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<string>("Origin").IsRequired().HasMaxLength(4).HasColumnType("character varying(4)");
            b.Property<string>("Registration").IsRequired().HasMaxLength(10).HasColumnType("character varying(10)");
            b.Property<string>("Source").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.HasKey("Id"); b.HasIndex("ImportBatchId"); b.HasIndex("UserId", "DepartureUtc", "FlightNumber", "Origin", "Destination").IsUnique();
            b.ToTable("pilot_flight");
        });
        model.Entity("Alpha6Ops.Accounts.MediaBlob", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<byte[]>("Bytes").IsRequired().HasColumnType("bytea");
            b.Property<string>("ContentType").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<int>("Height").HasColumnType("integer");
            b.Property<string>("Kind").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<Guid>("OwnerId").HasColumnType("uuid");
            b.Property<string>("Sha256").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.Property<int>("Width").HasColumnType("integer");
            b.HasKey("Id"); b.HasIndex("Kind", "OwnerId").IsUnique(); b.ToTable("media_blob");
        });
        model.Entity("Alpha6Ops.Accounts.PilotFlight", b =>
            b.HasOne("Alpha6Ops.Accounts.UserAccount", null).WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Restrict).IsRequired());
    }
}
