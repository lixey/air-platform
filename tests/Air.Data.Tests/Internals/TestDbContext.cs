namespace Platform.Data.Tests.Internals;

[ModuleSchema("Test")]
internal sealed class TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext? tenant = null)
  : PlatformDbContext(options, tenant)
{
  public DbSet<TestOrder> Orders => Set<TestOrder>();
  public DbSet<TestOrderLine> OrderLines => Set<TestOrderLine>();
  public DbSet<TestOrderNote> OrderNotes => Set<TestOrderNote>();
  public DbSet<TestRole> Roles => Set<TestRole>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<TestOrder>(b => b.HasQueryFilter("ArchivedFilter", e => !e.IsArchived));

    modelBuilder.Entity<TestOrderLine>(b => b.HasOne(x => x.Order)
        .WithMany()
        .HasForeignKey(x => x.OrderId)
        .OnDelete(DeleteBehavior.Cascade));

    modelBuilder.Entity<TestOrderNote>(b => b.HasOne(x => x.Order)
        .WithMany()
        .HasForeignKey(x => x.OrderId)
        .OnDelete(DeleteBehavior.SetNull));

    base.OnModelCreating(modelBuilder);
  }
}
