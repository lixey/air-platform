namespace Platform.Data.Tests.Internals;

internal class ParallelAppDbContext : PlatformDbContext
{
  public ParallelAppDbContext(DbContextOptions<ParallelAppDbContext> options) : base(options)
  {
  }

  protected ParallelAppDbContext(DbContextOptions options) : base(options)
  {
  }
}
