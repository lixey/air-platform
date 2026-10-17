using Air.Data;
using Air.Data.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace DemoApp.Data;

public class DemoDbContext : PlatformDbContext
{
  public DemoDbContext()
  {
  }

  public DemoDbContext(DbContextOptions<DemoDbContext> options) : base(options)
  {
  }

  public DemoDbContext(DbContextOptions<DemoDbContext> options, ITenantContext tenantContext) : base(options, tenantContext)
  {
  }

  public DbSet<Project> Projects => Set<Project>();
  public DbSet<TaskItem> Tasks => Set<TaskItem>();
  public DbSet<Models.AppTenant> Tenants => Set<Models.AppTenant>();
  public DbSet<Models.AppUser> Users => Set<Models.AppUser>();
  public DbSet<Models.AppAuthRole> Roles => Set<Models.AppAuthRole>();
  public DbSet<Models.AppUserGroup> UserGroups => Set<Models.AppUserGroup>();
  public DbSet<Models.AppAuthAssignment> Assignments => Set<Models.AppAuthAssignment>();
  public DbSet<Models.AppAuthGrant> Grants => Set<Models.AppAuthGrant>();
  public DbSet<Models.AppLanguage> Languages => Set<Models.AppLanguage>();
  public DbSet<Models.AppColor> Colors => Set<Models.AppColor>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<Project>(b =>
    {
      b.ToTable("Projects");
      b.Property(p => p.Title).HasMaxLength(200).IsRequired();
      b.Property(p => p.Status).HasMaxLength(50).IsRequired();
      b.Property(p => p.Budget).HasPrecision(18, 2);
    });

    modelBuilder.Entity<TaskItem>(b =>
    {
      b.ToTable("Tasks");
      b.Property(t => t.Title).HasMaxLength(200).IsRequired();
      b.Property(t => t.Priority).HasMaxLength(50).IsRequired();
      b.Property(t => t.Status).HasMaxLength(50).IsRequired();
      b.HasOne(t => t.Project)
        .WithMany(p => p.Tasks)
        .HasForeignKey(t => t.ProjectId)
        .OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<Models.AppTenant>(b =>
    {
      b.ToTable("AppTenants");
      b.Property(t => t.Handle).HasMaxLength(100).IsRequired();
      b.Property(t => t.Name).HasMaxLength(200).IsRequired();
    });

    modelBuilder.Entity<Models.AppUser>(b =>
    {
      b.ToTable("AppUsers");
      b.Property(u => u.Login).HasMaxLength(100).IsRequired();
      b.Property(u => u.Name).HasMaxLength(200).IsRequired();
      b.Property(u => u.Email).HasMaxLength(200).IsRequired();
    });

    modelBuilder.Entity<Models.AppAuthRole>(b =>
    {
      b.ToTable("AppAuthRoles");
      b.Property(r => r.Handle).HasMaxLength(100).IsRequired();
      b.Property(r => r.Name).HasMaxLength(200).IsRequired();
    });

    modelBuilder.Entity<Models.AppUserGroup>(b =>
    {
      b.ToTable("AppUserGroups");
      b.Property(g => g.Handle).HasMaxLength(100).IsRequired();
      b.Property(g => g.Name).HasMaxLength(200).IsRequired();
    });

    modelBuilder.Entity<Models.AppLanguage>(b => b.ToTable("AppLanguages"));
    modelBuilder.Entity<Models.AppColor>(b => b.ToTable("AppColors"));
  }
}
