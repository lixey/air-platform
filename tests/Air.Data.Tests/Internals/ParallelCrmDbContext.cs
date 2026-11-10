namespace Platform.Data.Tests.Internals;

internal sealed class ParallelCrmDbContext(DbContextOptions<ParallelCrmDbContext> options) : ParallelOrgDbContext(options);
