namespace Platform.Data.Tests.Internals;

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options, ITenantContext tenant)
  : PlatformDbContext(options, tenant)
{
  public DbSet<TestOrder> Orders => Set<TestOrder>();
  public DbSet<TestOrderLine> OrderLines => Set<TestOrderLine>();
  public DbSet<TestOrderNote> OrderNotes => Set<TestOrderNote>();

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
