using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alpha6Ops.Accounts.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("202610050001_ReleaseNotes")]
public sealed class ReleaseNotes : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ReleaseNotesModel.Build(modelBuilder);

    // "What's new" per desktop version, written by staff and shown on Download and Releases.
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE release_note (
            "Version" varchar(32) CONSTRAINT "PK_release_note" PRIMARY KEY CHECK ("Version" ~ '^[0-9]+\.[0-9]+\.[0-9]+$'),
            "Title" varchar(120) NOT NULL CHECK (length(btrim("Title")) > 0),
            "Notes" varchar(8000) NOT NULL CHECK (length(btrim("Notes")) > 0),
            "AuthorUserId" uuid NOT NULL CONSTRAINT "FK_release_note_user_account_AuthorUserId" REFERENCES user_account ("Id") ON DELETE RESTRICT,
            "PublishedAt" timestamptz NOT NULL,
            "UpdatedAt" timestamptz NOT NULL);
        CREATE INDEX "IX_release_note_AuthorUserId" ON release_note ("AuthorUserId");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP TABLE release_note;");
}

internal static class ReleaseNotesModel
{
    internal static void Build(ModelBuilder model)
    {
        TesterInvitesModel.Build(model);
        model.Entity("Alpha6Ops.Accounts.ReleaseNote", b =>
        {
            b.Property<string>("Version").HasMaxLength(32).HasColumnType("character varying(32)");
            b.Property<Guid>("AuthorUserId").HasColumnType("uuid");
            b.Property<string>("Notes").IsRequired().HasMaxLength(8000).HasColumnType("character varying(8000)");
            b.Property<DateTimeOffset>("PublishedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("Title").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Version"); b.HasIndex("AuthorUserId"); b.ToTable("release_note");
        });
        model.Entity("Alpha6Ops.Accounts.ReleaseNote", b =>
            b.HasOne("Alpha6Ops.Accounts.UserAccount", null).WithMany().HasForeignKey("AuthorUserId").OnDelete(DeleteBehavior.Restrict).IsRequired());
    }
}
