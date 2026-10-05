using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alpha6Ops.Accounts.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("202610040001_LowercaseEmails")]
public sealed class LowercaseEmails : Migration
{
    // Data only: AccountRules.Email now stores lower case instead of upper case. The schema is unchanged.
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => AccountDeletionModel.Build(modelBuilder);

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE user_account SET "Email" = lower("Email") WHERE "Email" <> lower("Email");
        UPDATE invitation SET "Email" = lower("Email") WHERE "Email" <> lower("Email");
        """);

    // Invitation acceptance compares emails exactly, so the older code needs its upper case back.
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE user_account SET "Email" = upper("Email") WHERE "Email" <> upper("Email");
        UPDATE invitation SET "Email" = upper("Email") WHERE "Email" <> upper("Email");
        """);
}
