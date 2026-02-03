using Andy.Rbac.Models;
using Microsoft.EntityFrameworkCore;
using Action = Andy.Rbac.Models.Action;

namespace Andy.Rbac.Infrastructure.Data;

public class RbacDbContext : DbContext
{
    public RbacDbContext(DbContextOptions<RbacDbContext> options) : base(options)
    {
    }

    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ResourceType> ResourceTypes => Set<ResourceType>();
    public DbSet<Action> Actions => Set<Action>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<SubjectRole> SubjectRoles => Set<SubjectRole>();
    public DbSet<ResourceInstance> ResourceInstances => Set<ResourceInstance>();
    public DbSet<InstancePermission> InstancePermissions => Set<InstancePermission>();
    public DbSet<ExternalGroupMapping> ExternalGroupMappings => Set<ExternalGroupMapping>();
    public DbSet<RbacAuditLog> AuditLogs => Set<RbacAuditLog>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<TeamRole> TeamRoles => Set<TeamRole>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Application
        modelBuilder.Entity<Application>(entity =>
        {
            entity.ToTable("applications");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => e.Code).IsUnique();
        });

        // ResourceType
        modelBuilder.Entity<ResourceType>(entity =>
        {
            entity.ToTable("resource_types");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => new { e.ApplicationId, e.Code }).IsUnique();
            entity.HasOne(e => e.Application)
                .WithMany(a => a.ResourceTypes)
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Action
        modelBuilder.Entity<Action>(entity =>
        {
            entity.ToTable("actions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => e.Code).IsUnique();
        });

        // Permission
        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("permissions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ResourceTypeId, e.ActionId }).IsUnique();
            entity.Ignore(e => e.Code); // Computed property
            entity.HasOne(e => e.ResourceType)
                .WithMany(r => r.Permissions)
                .HasForeignKey(e => e.ResourceTypeId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Action)
                .WithMany(a => a.Permissions)
                .HasForeignKey(e => e.ActionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Role
        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => new { e.ApplicationId, e.Code }).IsUnique();
            entity.HasOne(e => e.Application)
                .WithMany(a => a.Roles)
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.ParentRole)
                .WithMany(r => r.ChildRoles)
                .HasForeignKey(e => e.ParentRoleId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // RolePermission (join table)
        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(e => new { e.RoleId, e.PermissionId });
            entity.HasOne(e => e.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(e => e.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Subject
        modelBuilder.Entity<Subject>(entity =>
        {
            entity.ToTable("subjects");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExternalId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Provider).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(500);
            entity.Property(e => e.DisplayName).HasMaxLength(500);
            entity.Property(e => e.Metadata).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.Provider, e.ExternalId }).IsUnique();
            entity.HasIndex(e => e.Email);
        });

        // SubjectRole
        modelBuilder.Entity<SubjectRole>(entity =>
        {
            entity.ToTable("subject_roles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ResourceInstanceId).HasMaxLength(500);
            entity.HasIndex(e => new { e.SubjectId, e.RoleId, e.ResourceInstanceId }).IsUnique();
            entity.HasOne(e => e.Subject)
                .WithMany(s => s.SubjectRoles)
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Role)
                .WithMany(r => r.SubjectRoles)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.GrantedBy)
                .WithMany()
                .HasForeignKey(e => e.GrantedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ResourceInstance
        modelBuilder.Entity<ResourceInstance>(entity =>
        {
            entity.ToTable("resource_instances");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExternalId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(500);
            entity.Property(e => e.Metadata).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.ResourceTypeId, e.ExternalId }).IsUnique();
            entity.HasOne(e => e.ResourceType)
                .WithMany(r => r.Instances)
                .HasForeignKey(e => e.ResourceTypeId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Owner)
                .WithMany(s => s.OwnedResources)
                .HasForeignKey(e => e.OwnerSubjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // InstancePermission
        modelBuilder.Entity<InstancePermission>(entity =>
        {
            entity.ToTable("instance_permissions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ResourceInstanceId, e.SubjectId, e.PermissionId }).IsUnique();
            entity.HasOne(e => e.ResourceInstance)
                .WithMany(r => r.InstancePermissions)
                .HasForeignKey(e => e.ResourceInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject)
                .WithMany(s => s.InstancePermissions)
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Permission)
                .WithMany(p => p.InstancePermissions)
                .HasForeignKey(e => e.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.GrantedBy)
                .WithMany()
                .HasForeignKey(e => e.GrantedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ExternalGroupMapping
        modelBuilder.Entity<ExternalGroupMapping>(entity =>
        {
            entity.ToTable("external_group_mappings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ExternalGroupId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ExternalGroupName).HasMaxLength(500);
            entity.HasIndex(e => new { e.Provider, e.ExternalGroupId, e.RoleId }).IsUnique();
            entity.HasOne(e => e.Role)
                .WithMany(r => r.ExternalGroupMappings)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // RbacAuditLog
        modelBuilder.Entity<RbacAuditLog>(entity =>
        {
            entity.ToTable("rbac_audit_logs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ResourceType).HasMaxLength(100);
            entity.Property(e => e.ResourceInstanceId).HasMaxLength(500);
            entity.Property(e => e.PermissionCode).HasMaxLength(200);
            entity.Property(e => e.Result).HasMaxLength(20);
            entity.Property(e => e.Context).HasColumnType("jsonb");
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.SubjectId);
            entity.HasIndex(e => e.EventType);
        });

        // Team
        modelBuilder.Entity<Team>(entity =>
        {
            entity.ToTable("teams");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Code).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(500);
            entity.Property(e => e.ExternalProvider).HasMaxLength(100);
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => new { e.ExternalProvider, e.ExternalId });
            entity.HasOne(e => e.ParentTeam)
                .WithMany(t => t.ChildTeams)
                .HasForeignKey(e => e.ParentTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Application)
                .WithMany()
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // TeamMember
        modelBuilder.Entity<TeamMember>(entity =>
        {
            entity.ToTable("team_members");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TeamId, e.SubjectId }).IsUnique();
            entity.HasOne(e => e.Team)
                .WithMany(t => t.Members)
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject)
                .WithMany()
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AddedBy)
                .WithMany()
                .HasForeignKey(e => e.AddedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // TeamRole
        modelBuilder.Entity<TeamRole>(entity =>
        {
            entity.ToTable("team_roles");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ResourceInstanceId).HasMaxLength(500);
            entity.HasIndex(e => new { e.TeamId, e.RoleId, e.ResourceInstanceId }).IsUnique();
            entity.HasOne(e => e.Team)
                .WithMany(t => t.TeamRoles)
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Role)
                .WithMany()
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.GrantedBy)
                .WithMany()
                .HasForeignKey(e => e.GrantedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ApiKey
        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.ToTable("api_keys");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.KeyHash).HasMaxLength(500).IsRequired();
            entity.Property(e => e.KeyPrefix).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Scopes).HasColumnType("jsonb");
            entity.Property(e => e.LastUsedIp).HasMaxLength(50);
            entity.HasIndex(e => e.KeyPrefix).IsUnique();
            entity.HasOne(e => e.Subject)
                .WithMany()
                .HasForeignKey(e => e.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Application)
                .WithMany()
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
