namespace Platform.Agent.Tests.Internals;

internal sealed class AgentTestDbContext(DbContextOptions<AgentTestDbContext> options, ITenantContext tenant)
  : PlatformDbContext(options, tenant)
{
  public DbSet<AgentTestCategory> Categories => Set<AgentTestCategory>();
  public DbSet<AgentTestReceipt> Receipts => Set<AgentTestReceipt>();
  public DbSet<AgentTestReceiptLine> ReceiptLines => Set<AgentTestReceiptLine>();
  public DbSet<AgentTestSecret> Secrets => Set<AgentTestSecret>();
}
