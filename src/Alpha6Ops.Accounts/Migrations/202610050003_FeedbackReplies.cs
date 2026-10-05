using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alpha6Ops.Accounts.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("202610050003_FeedbackReplies")]
public sealed class FeedbackReplies : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => FeedbackRepliesModel.Build(modelBuilder);

    // Staff replies to problem reports, and when the pilot last read their reports (for the unread badge).
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE feedback ADD COLUMN "LastReplyAt" timestamptz NULL, ADD COLUMN "PilotReadAt" timestamptz NULL;
        CREATE TABLE feedback_reply (
            "Id" uuid CONSTRAINT "PK_feedback_reply" PRIMARY KEY,
            "FeedbackId" uuid NOT NULL CONSTRAINT "FK_feedback_reply_feedback_FeedbackId" REFERENCES feedback ("Id") ON DELETE CASCADE,
            "AuthorUserId" uuid NOT NULL CONSTRAINT "FK_feedback_reply_user_account_AuthorUserId" REFERENCES user_account ("Id") ON DELETE RESTRICT,
            "Body" varchar(2000) NOT NULL CHECK (length(btrim("Body")) > 0),
            "CreatedAt" timestamptz NOT NULL);
        CREATE INDEX "IX_feedback_reply_FeedbackId_CreatedAt" ON feedback_reply ("FeedbackId", "CreatedAt");
        CREATE INDEX "IX_feedback_reply_AuthorUserId" ON feedback_reply ("AuthorUserId");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE feedback_reply;
        ALTER TABLE feedback DROP COLUMN "LastReplyAt", DROP COLUMN "PilotReadAt";
        """);
}

internal static class FeedbackRepliesModel
{
    internal static void Build(ModelBuilder model)
    {
        InviteRequestsModel.Build(model);
        model.Entity("Alpha6Ops.Accounts.Feedback", b =>
        {
            b.Property<DateTimeOffset?>("LastReplyAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("PilotReadAt").HasColumnType("timestamp with time zone");
        });
        model.Entity("Alpha6Ops.Accounts.FeedbackReply", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<Guid>("AuthorUserId").HasColumnType("uuid");
            b.Property<string>("Body").IsRequired().HasMaxLength(2000).HasColumnType("character varying(2000)");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<Guid>("FeedbackId").HasColumnType("uuid");
            b.HasKey("Id"); b.HasIndex("AuthorUserId"); b.HasIndex("FeedbackId", "CreatedAt"); b.ToTable("feedback_reply");
        });
        model.Entity("Alpha6Ops.Accounts.FeedbackReply", b =>
        {
            b.HasOne("Alpha6Ops.Accounts.UserAccount", null).WithMany().HasForeignKey("AuthorUserId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("Alpha6Ops.Accounts.Feedback", null).WithMany().HasForeignKey("FeedbackId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });
    }
}
