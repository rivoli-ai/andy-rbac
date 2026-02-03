using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Andy.Rbac.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "actions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rbac_audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResourceInstanceId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PermissionCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Context = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rbac_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Email = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "resource_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SupportsInstances = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resource_types", x => x.Id);
                    table.ForeignKey(
                        name: "FK_resource_types_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    ParentRoleId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_roles_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_roles_roles_ParentRoleId",
                        column: x => x.ParentRoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ParentTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExternalProvider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_teams_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_teams_teams_ParentTeamId",
                        column: x => x.ParentTeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Scopes = table.Column<List<string>>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedIp = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_api_keys_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_api_keys_subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_permissions_actions_ActionId",
                        column: x => x.ActionId,
                        principalTable: "actions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_permissions_resource_types_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalTable: "resource_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resource_instances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OwnerSubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resource_instances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_resource_instances_resource_types_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalTable: "resource_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_resource_instances_subjects_OwnerSubjectId",
                        column: x => x.OwnerSubjectId,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "external_group_mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalGroupId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExternalGroupName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_group_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_external_group_mappings_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subject_roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceInstanceId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    GrantedById = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subject_roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subject_roles_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_subject_roles_subjects_GrantedById",
                        column: x => x.GrantedById,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_subject_roles_subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipRole = table.Column<int>(type: "integer", nullable: false),
                    AddedById = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_team_members_subjects_AddedById",
                        column: x => x.AddedById,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_team_members_subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_team_members_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceInstanceId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    GrantedById = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_team_roles_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_team_roles_subjects_GrantedById",
                        column: x => x.GrantedById,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_team_roles_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_role_permissions_permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_role_permissions_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instance_permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedById = table.Column<Guid>(type: "uuid", nullable: true),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_permissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_instance_permissions_permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_instance_permissions_resource_instances_ResourceInstanceId",
                        column: x => x.ResourceInstanceId,
                        principalTable: "resource_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_instance_permissions_subjects_GrantedById",
                        column: x => x.GrantedById,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_instance_permissions_subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_actions_Code",
                table: "actions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_ApplicationId",
                table: "api_keys",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_KeyPrefix",
                table: "api_keys",
                column: "KeyPrefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_SubjectId",
                table: "api_keys",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_applications_Code",
                table: "applications",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_group_mappings_Provider_ExternalGroupId_RoleId",
                table: "external_group_mappings",
                columns: new[] { "Provider", "ExternalGroupId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_group_mappings_RoleId",
                table: "external_group_mappings",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_instance_permissions_GrantedById",
                table: "instance_permissions",
                column: "GrantedById");

            migrationBuilder.CreateIndex(
                name: "IX_instance_permissions_PermissionId",
                table: "instance_permissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_instance_permissions_ResourceInstanceId_SubjectId_Permissio~",
                table: "instance_permissions",
                columns: new[] { "ResourceInstanceId", "SubjectId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_instance_permissions_SubjectId",
                table: "instance_permissions",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_ActionId",
                table: "permissions",
                column: "ActionId");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_ResourceTypeId_ActionId",
                table: "permissions",
                columns: new[] { "ResourceTypeId", "ActionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rbac_audit_logs_EventType",
                table: "rbac_audit_logs",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_rbac_audit_logs_SubjectId",
                table: "rbac_audit_logs",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_rbac_audit_logs_Timestamp",
                table: "rbac_audit_logs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_resource_instances_OwnerSubjectId",
                table: "resource_instances",
                column: "OwnerSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_resource_instances_ResourceTypeId_ExternalId",
                table: "resource_instances",
                columns: new[] { "ResourceTypeId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resource_types_ApplicationId_Code",
                table: "resource_types",
                columns: new[] { "ApplicationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_PermissionId",
                table: "role_permissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_roles_ApplicationId_Code",
                table: "roles",
                columns: new[] { "ApplicationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_roles_ParentRoleId",
                table: "roles",
                column: "ParentRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_roles_GrantedById",
                table: "subject_roles",
                column: "GrantedById");

            migrationBuilder.CreateIndex(
                name: "IX_subject_roles_RoleId",
                table: "subject_roles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_roles_SubjectId_RoleId_ResourceInstanceId",
                table: "subject_roles",
                columns: new[] { "SubjectId", "RoleId", "ResourceInstanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subjects_Email",
                table: "subjects",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_subjects_Provider_ExternalId",
                table: "subjects",
                columns: new[] { "Provider", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_members_AddedById",
                table: "team_members",
                column: "AddedById");

            migrationBuilder.CreateIndex(
                name: "IX_team_members_SubjectId",
                table: "team_members",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_team_members_TeamId_SubjectId",
                table: "team_members",
                columns: new[] { "TeamId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_roles_GrantedById",
                table: "team_roles",
                column: "GrantedById");

            migrationBuilder.CreateIndex(
                name: "IX_team_roles_RoleId",
                table: "team_roles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_team_roles_TeamId_RoleId_ResourceInstanceId",
                table: "team_roles",
                columns: new[] { "TeamId", "RoleId", "ResourceInstanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teams_ApplicationId",
                table: "teams",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_teams_Code",
                table: "teams",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teams_ExternalProvider_ExternalId",
                table: "teams",
                columns: new[] { "ExternalProvider", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_teams_ParentTeamId",
                table: "teams",
                column: "ParentTeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "external_group_mappings");

            migrationBuilder.DropTable(
                name: "instance_permissions");

            migrationBuilder.DropTable(
                name: "rbac_audit_logs");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "subject_roles");

            migrationBuilder.DropTable(
                name: "team_members");

            migrationBuilder.DropTable(
                name: "team_roles");

            migrationBuilder.DropTable(
                name: "resource_instances");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "subjects");

            migrationBuilder.DropTable(
                name: "actions");

            migrationBuilder.DropTable(
                name: "resource_types");

            migrationBuilder.DropTable(
                name: "applications");
        }
    }
}
