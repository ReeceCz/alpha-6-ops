using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alpha6Ops.Accounts.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("202610040002_Feedback")]
public sealed class Feedback : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => FeedbackModel.Build(modelBuilder);

    // Problem reports from pilots, read by allowlisted staff. Attachments live apart from media_blob, whose
    // constraints admit only small images.
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE feedback (
            "Id" uuid CONSTRAINT "PK_feedback" PRIMARY KEY,
            "UserId" uuid NOT NULL CONSTRAINT "FK_feedback_user_account_UserId" REFERENCES user_account ("Id") ON DELETE RESTRICT,
            "Category" integer NOT NULL CHECK ("Category" IN (0,1,2,3)),
            "Message" varchar(4000) NOT NULL CHECK (length(btrim("Message")) >= 10),
            "FlightNumber" varchar(10) NOT NULL DEFAULT '',
            "FlownOn" date NULL,
            "AppVersion" varchar(40) NOT NULL DEFAULT '',
            "Status" integer NOT NULL CHECK ("Status" IN (0,1)),
            "CreatedAt" timestamptz NOT NULL,
            "HandledAt" timestamptz NULL,
            "HandledByUserId" uuid NULL CONSTRAINT "FK_feedback_user_account_HandledByUserId" REFERENCES user_account ("Id") ON DELETE RESTRICT,
            CONSTRAINT feedback_handled_date CHECK (("Status" = 1) = ("HandledAt" IS NOT NULL)));
        CREATE INDEX "IX_feedback_UserId_CreatedAt" ON feedback ("UserId", "CreatedAt");
        CREATE INDEX "IX_feedback_Status_CreatedAt" ON feedback ("Status", "CreatedAt");
        CREATE INDEX "IX_feedback_HandledByUserId" ON feedback ("HandledByUserId");
        CREATE TABLE feedback_attachment (
            "FeedbackId" uuid CONSTRAINT "PK_feedback_attachment" PRIMARY KEY
                CONSTRAINT "FK_feedback_attachment_feedback_FeedbackId" REFERENCES feedback ("Id") ON DELETE CASCADE,
            "FileName" varchar(120) NOT NULL,
            "ContentType" varchar(40) NOT NULL CHECK ("ContentType" IN ('image/png','image/jpeg','application/zip','application/json')),
            "Bytes" bytea NOT NULL CHECK (octet_length("Bytes") BETWEEN 1 AND 5242880),
            "Sha256" varchar(64) NOT NULL);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE feedback_attachment;
        DROP TABLE feedback;
        """);
}

internal static class FeedbackModel
{
    internal static void Build(ModelBuilder model)
    {
        AccountDeletionModel.Build(model);
        model.Entity("Alpha6Ops.Accounts.Feedback", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<string>("AppVersion").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<int>("Category").HasColumnType("integer");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("FlightNumber").IsRequired().HasMaxLength(10).HasColumnType("character varying(10)");
            b.Property<DateOnly?>("FlownOn").HasColumnType("date");
            b.Property<DateTimeOffset?>("HandledAt").HasColumnType("timestamp with time zone");
            b.Property<Guid?>("HandledByUserId").HasColumnType("uuid");
            b.Property<string>("Message").IsRequired().HasMaxLength(4000).HasColumnType("character varying(4000)");
            b.Property<int>("Status").HasColumnType("integer");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.HasKey("Id"); b.HasIndex("HandledByUserId"); b.HasIndex("Status", "CreatedAt"); b.HasIndex("UserId", "CreatedAt");
            b.ToTable("feedback");
        });
        model.Entity("Alpha6Ops.Accounts.FeedbackAttachment", b =>
        {
            b.Property<Guid>("FeedbackId").HasColumnType("uuid");
            b.Property<byte[]>("Bytes").IsRequired().HasColumnType("bytea");
            b.Property<string>("ContentType").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<string>("FileName").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)");
            b.Property<string>("Sha256").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.HasKey("FeedbackId"); b.ToTable("feedback_attachment");
        });
        model.Entity("Alpha6Ops.Accounts.Feedback", b =>
        {
            b.HasOne("Alpha6Ops.Accounts.UserAccount", null).WithMany().HasForeignKey("HandledByUserId").OnDelete(DeleteBehavior.Restrict);
            b.HasOne("Alpha6Ops.Accounts.UserAccount", null).WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
        model.Entity("Alpha6Ops.Accounts.FeedbackAttachment", b =>
            b.HasOne("Alpha6Ops.Accounts.Feedback", null).WithOne().HasForeignKey("Alpha6Ops.Accounts.FeedbackAttachment", "FeedbackId").OnDelete(DeleteBehavior.Cascade).IsRequired());
    }
}
