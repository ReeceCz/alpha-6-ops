using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alpha6Ops.Accounts.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("202609180001_ScheduleAndFleet")]
public sealed class ScheduleAndFleet : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ScheduleAndFleetModel.Build(modelBuilder);

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE airline_route (
            "Id" uuid CONSTRAINT "PK_airline_route" PRIMARY KEY,
            "AirlineId" uuid NOT NULL CONSTRAINT "FK_airline_route_virtual_airline_AirlineId" REFERENCES virtual_airline ("Id") ON DELETE RESTRICT,
            "FlightNumber" varchar(10) NOT NULL CHECK ("FlightNumber" ~ '^[A-Z0-9]{2,10}$'),
            "Origin" varchar(4) NOT NULL CHECK ("Origin" ~ '^[A-Z0-9]{3,4}$'),
            "Destination" varchar(4) NOT NULL CHECK ("Destination" ~ '^[A-Z0-9]{3,4}$' AND "Destination" <> "Origin"),
            "DepartureUtc" time without time zone NOT NULL,
            "BlockMinutes" integer NOT NULL CHECK ("BlockMinutes" BETWEEN 5 AND 1440),
            "DaysOfWeek" integer NOT NULL CHECK ("DaysOfWeek" BETWEEN 1 AND 127),
            "AircraftType" varchar(4) NOT NULL CHECK ("AircraftType" ~ '^([A-Z0-9]{2,4})?$'),
            "Notes" varchar(500) NOT NULL,
            "Active" boolean NOT NULL,
            "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL);
        CREATE UNIQUE INDEX "IX_airline_route_AirlineId_FlightNumber_Origin_Destination" ON airline_route ("AirlineId", "FlightNumber", "Origin", "Destination");

        CREATE TABLE airline_aircraft (
            "Id" uuid CONSTRAINT "PK_airline_aircraft" PRIMARY KEY,
            "AirlineId" uuid NOT NULL CONSTRAINT "FK_airline_aircraft_virtual_airline_AirlineId" REFERENCES virtual_airline ("Id") ON DELETE RESTRICT,
            "Registration" varchar(10) NOT NULL CHECK ("Registration" ~ '^[A-Z0-9][A-Z0-9-]{0,8}[A-Z0-9]$'),
            "TypeIcao" varchar(4) NOT NULL CHECK ("TypeIcao" ~ '^[A-Z0-9]{2,4}$'),
            "Name" varchar(100) NOT NULL,
            "HomeBase" varchar(4) NOT NULL CHECK ("HomeBase" ~ '^([A-Z0-9]{3,4})?$'),
            "Status" varchar(20) NOT NULL CHECK ("Status" IN ('active','maintenance','retired')),
            "Notes" varchar(500) NOT NULL,
            "CreatedAt" timestamptz NOT NULL, "UpdatedAt" timestamptz NOT NULL);
        CREATE UNIQUE INDEX "IX_airline_aircraft_AirlineId_Registration" ON airline_aircraft ("AirlineId", "Registration");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE airline_aircraft;
        DROP TABLE airline_route;
        """);
}

// Composes the profile model with the schedule and fleet entities.
internal static class ScheduleAndFleetModel
{
    internal static void Build(ModelBuilder model)
    {
        ProfileAndPlansModel.Build(model);
        model.Entity("Alpha6Ops.Accounts.AirlineRoute", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<bool>("Active").HasColumnType("boolean");
            b.Property<string>("AircraftType").IsRequired().HasMaxLength(4).HasColumnType("character varying(4)");
            b.Property<Guid>("AirlineId").HasColumnType("uuid");
            b.Property<int>("BlockMinutes").HasColumnType("integer");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<int>("DaysOfWeek").HasColumnType("integer");
            b.Property<TimeOnly>("DepartureUtc").HasColumnType("time without time zone");
            b.Property<string>("Destination").IsRequired().HasMaxLength(4).HasColumnType("character varying(4)");
            b.Property<string>("FlightNumber").IsRequired().HasMaxLength(10).HasColumnType("character varying(10)");
            b.Property<string>("Notes").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<string>("Origin").IsRequired().HasMaxLength(4).HasColumnType("character varying(4)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id"); b.HasIndex("AirlineId", "FlightNumber", "Origin", "Destination").IsUnique(); b.ToTable("airline_route");
        });
        model.Entity("Alpha6Ops.Accounts.AirlineAircraft", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<Guid>("AirlineId").HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("HomeBase").IsRequired().HasMaxLength(4).HasColumnType("character varying(4)");
            b.Property<string>("Name").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("Notes").IsRequired().HasMaxLength(500).HasColumnType("character varying(500)");
            b.Property<string>("Registration").IsRequired().HasMaxLength(10).HasColumnType("character varying(10)");
            b.Property<string>("Status").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("TypeIcao").IsRequired().HasMaxLength(4).HasColumnType("character varying(4)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id"); b.HasIndex("AirlineId", "Registration").IsUnique(); b.ToTable("airline_aircraft");
        });
        model.Entity("Alpha6Ops.Accounts.AirlineRoute", b =>
            b.HasOne("Alpha6Ops.Accounts.VirtualAirline", null).WithMany().HasForeignKey("AirlineId").OnDelete(DeleteBehavior.Restrict).IsRequired());
        model.Entity("Alpha6Ops.Accounts.AirlineAircraft", b =>
            b.HasOne("Alpha6Ops.Accounts.VirtualAirline", null).WithMany().HasForeignKey("AirlineId").OnDelete(DeleteBehavior.Restrict).IsRequired());
    }
}
