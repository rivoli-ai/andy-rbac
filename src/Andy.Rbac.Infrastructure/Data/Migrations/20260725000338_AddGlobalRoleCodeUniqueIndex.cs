using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Rbac.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Constrains global role codes (issue #116).
    ///
    /// The existing unique index is (ApplicationId, Code); Postgres treats
    /// NULLs as distinct, so it never constrained roles with
    /// ApplicationId IS NULL. Duplicates make RoleResolver report the code
    /// ambiguous forever, leaving it permanently unassignable.
    ///
    /// This migration FAILS on a database that already contains duplicate
    /// global role codes. That is deliberate — resolving them means deciding
    /// which role's assignments survive, and deleting a role cascades to its
    /// SubjectRoles, TeamRoles and RolePermissions. Find them with:
    ///
    ///   SELECT "Code", count(*) FROM roles
    ///   WHERE "ApplicationId" IS NULL GROUP BY "Code" HAVING count(*) &gt; 1;
    /// </summary>
    public partial class AddGlobalRoleCodeUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_roles_Code_global_unique",
                table: "roles",
                column: "Code",
                unique: true,
                filter: "\"ApplicationId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_roles_Code_global_unique",
                table: "roles");
        }
    }
}
