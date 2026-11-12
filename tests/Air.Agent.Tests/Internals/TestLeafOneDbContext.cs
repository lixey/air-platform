namespace Platform.Agent.Tests.Internals;

[ModuleSchema("LeafOne")]
internal sealed class TestLeafOneDbContext(DbContextOptions<TestLeafOneDbContext> options, ITenantContext tenant)
  : AgentTestDbContext(options, tenant)
{
  public DbSet<TestLeafOneItem> LeafOneItems => Set<TestLeafOneItem>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);
    modelBuilder.Entity<TestLeafOneItem>().HasModuleOwner("LeafOne");
  }
}
