namespace Platform.App.Tests.Internals;

internal sealed class AppTestDbContext(DbContextOptions<AppTestDbContext> options, ITenantContext tenant)
  : PlatformDbContext(options, tenant)
{
  public DbSet<TestInvoice> Invoices => Set<TestInvoice>();
  public DbSet<TestInvoiceLine> InvoiceLines => Set<TestInvoiceLine>();
  public DbSet<TestTag> Tags => Set<TestTag>();
  public DbSet<TestInvoiceTag> InvoiceTags => Set<TestInvoiceTag>();
}
