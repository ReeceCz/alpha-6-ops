using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alpha6Ops.Accounts.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("202610040003_TesterInvites")]
public sealed class TesterInvites : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => TesterInvitesModel.Build(modelBuilder);

    // Testers invited from the staff page, so adding one needs no hosting configuration change.
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE tester_invite (
            "Email" varchar(254) CONSTRAINT "PK_tester_invite" PRIMARY KEY CHECK ("Email" = lower("Email") AND "Email" <> ''),
            "Note" varchar(200) NOT NULL DEFAULT '',
            "InvitedByUserId" uuid NOT NULL CONSTRAINT "FK_tester_invite_user_account_InvitedByUserId" REFERENCES user_account ("Id") ON DELETE RESTRICT,
            "CreatedAt" timestamptz NOT NULL);
        CREATE INDEX "IX_tester_invite_InvitedByUserId" ON tester_invite ("InvitedByUserId");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP TABLE tester_invite;");
}

internal static class TesterInvitesModel
{
    internal static void Build(ModelBuilder model)
    {
        FeedbackModel.Build(model);
        model.Entity("Alpha6Ops.Accounts.TesterInvite", b =>
        {
            b.Property<string>("Email").HasMaxLength(254).HasColumnType("character varying(254)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<Guid>("InvitedByUserId").HasColumnType("uuid");
            b.Property<string>("Note").IsRequired().HasMaxLength(200).HasColumnType("character varying(200)");
            b.HasKey("Email"); b.HasIndex("InvitedByUserId"); b.ToTable("tester_invite");
        });
        model.Entity("Alpha6Ops.Accounts.TesterInvite", b =>
            b.HasOne("Alpha6Ops.Accounts.UserAccount", null).WithMany().HasForeignKey("InvitedByUserId").OnDelete(DeleteBehavior.Restrict).IsRequired());
    }
}
