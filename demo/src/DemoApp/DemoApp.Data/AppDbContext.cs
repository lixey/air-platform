namespace DemoApp.Data;

[ModuleSchema(AppData.ModuleHandle)]
public class AppDbContext : PlatformDbContext
{
  public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenant = null) : base(options, tenant) { }

  protected AppDbContext(DbContextOptions options, ITenantContext? tenant) : base(options, tenant) { }

  public DbSet<AppUser> AppUser => Set<AppUser>();
  public DbSet<AppAuthSession> AppAuthSession => Set<AppAuthSession>();
  public DbSet<AppTenant> AppTenant => Set<AppTenant>();
  public DbSet<AppAuthRole> AppAuthRole => Set<AppAuthRole>();
  public DbSet<AppAuthAssignment> AppAuthAssignment => Set<AppAuthAssignment>();
  public DbSet<AppAuthGrant> AppAuthGrant => Set<AppAuthGrant>();
  public DbSet<AppColor> AppColor => Set<AppColor>();
  public DbSet<AppUserGroup> AppUserGroup => Set<AppUserGroup>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<AppUser>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<AppUser>().HasOne(row => row.UserGroup).WithMany().HasForeignKey(row => row.UserGroupId)
      .OnDelete(DeleteBehavior.SetNull);
    modelBuilder.Entity<AppUserGroup>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<AppUserGroup>().HasOne(row => row.Color).WithMany().HasForeignKey(row => row.ColorId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<AppTenant>().HasOne(row => row.Parent).WithMany().HasForeignKey(row => row.ParentId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<AppAuthRole>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<AppAuthRole>().HasIndex(row => row.Name).IsUnique().HasFilter("\"TenantId\" IS NULL");
  }
}
