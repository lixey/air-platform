namespace Platform.Agent.Tests.Internals;

[ModuleSchema("LeafTwo")]
internal sealed class TestLeafTwoDbContext(DbContextOptions<TestLeafTwoDbContext> options, ITenantContext tenant)
  : AgentTestDbContext(options, tenant)
{
  public DbSet<TestLeafTwoOrder> LeafTwoOrders => Set<TestLeafTwoOrder>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);
    modelBuilder.Entity<TestLeafTwoOrder>().HasModuleOwner("LeafTwo");
  }
}
