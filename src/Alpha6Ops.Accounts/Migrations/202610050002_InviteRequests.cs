using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alpha6Ops.Accounts.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("202610050002_InviteRequests")]
public sealed class InviteRequests : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => InviteRequestsModel.Build(modelBuilder);

    // Pending invitation requests. A row exists only until staff invite or decline, or for 90 days.
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE invite_request (
            "Id" uuid CONSTRAINT "PK_invite_request" PRIMARY KEY,
            "Email" varchar(254) NOT NULL CHECK ("Email" = lower("Email") AND "Email" <> ''),
            "Name" varchar(100) NOT NULL DEFAULT '',
            "Note" varchar(1000) NOT NULL DEFAULT '',
            "CreatedAt" timestamptz NOT NULL);
        CREATE UNIQUE INDEX "IX_invite_request_Email" ON invite_request ("Email");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP TABLE invite_request;");
}

internal static class InviteRequestsModel
{
    internal static void Build(ModelBuilder model)
    {
        ReleaseNotesModel.Build(model);
        model.Entity("Alpha6Ops.Accounts.InviteRequest", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Email").IsRequired().HasMaxLength(254).HasColumnType("character varying(254)");
            b.Property<string>("Name").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("Note").IsRequired().HasMaxLength(1000).HasColumnType("character varying(1000)");
            b.HasKey("Id"); b.HasIndex("Email").IsUnique(); b.ToTable("invite_request");
        });
    }
}
