namespace Platform.Data.Tests.Internals;

internal sealed class ParallelEcomDbContext(DbContextOptions<ParallelEcomDbContext> options) : ParallelOrgDbContext(options);
