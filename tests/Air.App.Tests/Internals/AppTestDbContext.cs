namespace Platform.App.Tests.Internals;

[ModuleSchema("Test")]
internal sealed class AppTestDbContext(DbContextOptions<AppTestDbContext> options, ITenantContext tenant)
  : PlatformDbContext(options, tenant)
{
  public DbSet<TestInvoice> Invoices => Set<TestInvoice>();
  public DbSet<TestInvoiceLine> InvoiceLines => Set<TestInvoiceLine>();
  public DbSet<TestTag> Tags => Set<TestTag>();
  public DbSet<TestInvoiceTag> InvoiceTags => Set<TestInvoiceTag>();
  public DbSet<TestAppFile> AppFiles => Set<TestAppFile>();
}
