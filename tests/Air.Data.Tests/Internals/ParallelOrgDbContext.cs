namespace Platform.Data.Tests.Internals;

internal class ParallelOrgDbContext : ParallelAppDbContext
{
  public ParallelOrgDbContext(DbContextOptions<ParallelOrgDbContext> options) : base(options)
  {
  }

  protected ParallelOrgDbContext(DbContextOptions options) : base(options)
  {
  }
}
