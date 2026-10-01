using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alpha6Ops.Accounts.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("202610010001_AccountDeletion")]
public sealed class AccountDeletion : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => AccountDeletionModel.Build(modelBuilder);

    // Self-service deletion: an account waits in PendingDeletion (2) for the grace period, then becomes an
    // anonymised Deleted (3) tombstone that airline history can still point at.
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE user_account DROP CONSTRAINT "user_account_Status_check";
        ALTER TABLE user_account ADD CONSTRAINT "user_account_Status_check" CHECK ("Status" IN (0,1,2,3));
        ALTER TABLE user_account ADD COLUMN "DeletionRequestedAt" timestamptz NULL,
            ADD COLUMN "DeletedAt" timestamptz NULL, ADD COLUMN "IdentityRemovedAt" timestamptz NULL;
        ALTER TABLE user_account ADD CONSTRAINT user_account_deletion_dates
            CHECK (("Status" <> 2 OR "DeletionRequestedAt" IS NOT NULL) AND (("Status" = 3) = ("DeletedAt" IS NOT NULL)));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE user_account DROP CONSTRAINT user_account_deletion_dates;
        UPDATE user_account SET "Status" = 1 WHERE "Status" IN (2,3);
        ALTER TABLE user_account DROP COLUMN "DeletionRequestedAt", DROP COLUMN "DeletedAt", DROP COLUMN "IdentityRemovedAt";
        ALTER TABLE user_account DROP CONSTRAINT "user_account_Status_check";
        ALTER TABLE user_account ADD CONSTRAINT "user_account_Status_check" CHECK ("Status" IN (0,1));
        """);
}

internal static class AccountDeletionModel
{
    internal static void Build(ModelBuilder model)
    {
        DataProtectionKeysModel.Build(model);
        model.Entity("Alpha6Ops.Accounts.UserAccount", b =>
        {
            b.Property<DateTimeOffset?>("DeletionRequestedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("DeletedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("IdentityRemovedAt").HasColumnType("timestamp with time zone");
        });
    }
}
