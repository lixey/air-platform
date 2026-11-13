namespace Platform.Agent.Tests.Internals;

[ModuleSchema("Test")]
internal class AgentTestDbContext : PlatformDbContext
{
  public AgentTestDbContext(DbContextOptions<AgentTestDbContext> options, ITenantContext tenant)
    : base(options, tenant)
  {
  }

  protected AgentTestDbContext(DbContextOptions options, ITenantContext tenant)
    : base(options, tenant)
  {
  }

  public DbSet<AgentTestCategory> Categories => Set<AgentTestCategory>();
  public DbSet<AgentTestReceipt> Receipts => Set<AgentTestReceipt>();
  public DbSet<AgentTestReceiptLine> ReceiptLines => Set<AgentTestReceiptLine>();
  public DbSet<AgentTestInspection> Inspections => Set<AgentTestInspection>();
  public DbSet<AgentTestCheckpoint> Checkpoints => Set<AgentTestCheckpoint>();
  public DbSet<AgentTestMeasurement> Measurements => Set<AgentTestMeasurement>();
  public DbSet<AgentTestAuditNote> AuditNotes => Set<AgentTestAuditNote>();
  public DbSet<AgentTestSecret> Secrets => Set<AgentTestSecret>();
  public DbSet<AgentTestOrderStatus> OrderStatuses => Set<AgentTestOrderStatus>();
  public DbSet<AgentTestOrder> Orders => Set<AgentTestOrder>();
  public DbSet<AgentTestOrderLine> OrderLines => Set<AgentTestOrderLine>();
}
