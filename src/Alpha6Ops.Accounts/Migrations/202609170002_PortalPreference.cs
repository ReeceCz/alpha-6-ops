using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alpha6Ops.Accounts.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("202609170002_PortalPreference")]
public sealed class PortalPreference : Migration
{
    // Schema shape is unchanged; only the workspace-preference check gains the "portal" value.
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => ProfileAndPlansModel.Build(modelBuilder);

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE user_profile DROP CONSTRAINT "user_profile_PreferredWorkspace_check";
        ALTER TABLE user_profile ADD CONSTRAINT user_profile_preferred_workspace
            CHECK ("PreferredWorkspace" IN ('last_used','personal','portal'));
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE user_profile SET "PreferredWorkspace" = 'last_used' WHERE "PreferredWorkspace" = 'portal';
        ALTER TABLE user_profile DROP CONSTRAINT user_profile_preferred_workspace;
        ALTER TABLE user_profile ADD CONSTRAINT "user_profile_PreferredWorkspace_check"
            CHECK ("PreferredWorkspace" IN ('last_used','personal'));
        """);
}
