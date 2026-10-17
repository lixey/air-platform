namespace DemoEcom.Data;

public class EcomDbContext : OrgDbContext
{
  public new string ModuleHandle => EcomData.ModuleHandle;
  public EcomDbContext(DbContextOptions<EcomDbContext> options, ITenantContext? tenant = null) : base(options, tenant) { }

  protected EcomDbContext(DbContextOptions options, ITenantContext? tenant) : base(options, tenant) { }

  public DbSet<EcomDocType> EcomDocType => Set<EcomDocType>();
  public DbSet<EcomDocStatus> EcomDocStatus => Set<EcomDocStatus>();
  public DbSet<EcomDoc> EcomDoc => Set<EcomDoc>();
  public DbSet<EcomDocLine> EcomDocLine => Set<EcomDocLine>();
  public DbSet<EcomStockBalance> EcomStockBalance => Set<EcomStockBalance>();
  public DbSet<EcomStockMove> EcomStockMove => Set<EcomStockMove>();
  public DbSet<EcomMoneyBalance> EcomMoneyBalance => Set<EcomMoneyBalance>();
  public DbSet<EcomMoneyMove> EcomMoneyMove => Set<EcomMoneyMove>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<EcomDoc>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomDocLine>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomStockBalance>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomStockMove>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomMoneyBalance>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomMoneyMove>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<EcomDoc>().HasOne(row => row.Warehouse).WithMany().HasForeignKey(row => row.WarehouseId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomDoc>().HasOne(row => row.WarehouseTo).WithMany().HasForeignKey(row => row.WarehouseToId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomDoc>().HasOne(row => row.MoneyAccount).WithMany().HasForeignKey(row => row.MoneyAccountId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomDoc>().HasOne(row => row.Organization).WithMany().HasForeignKey(row => row.OrganizationId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomDoc>().HasOne(row => row.Customer).WithMany().HasForeignKey(row => row.CustomerId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomDoc>().HasOne(row => row.Contact).WithMany().HasForeignKey(row => row.ContactId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomDoc>().HasOne(row => row.Counterparty).WithMany().HasForeignKey(row => row.CounterpartyId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomDoc>().HasOne(row => row.DeliveryType).WithMany().HasForeignKey(row => row.DeliveryTypeId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomDoc>().HasOne(row => row.PaymentMethod).WithMany().HasForeignKey(row => row.PaymentMethodId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomDoc>().HasOne(row => row.PaymentTerm).WithMany().HasForeignKey(row => row.PaymentTermId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomDoc>().HasOne(row => row.Currency).WithMany().HasForeignKey(row => row.CurrencyId)
      .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<EcomDocLine>().HasOne(row => row.Doc).WithMany().HasForeignKey(row => row.DocId)
      .OnDelete(DeleteBehavior.Cascade);
    modelBuilder.Entity<EcomDocLine>().HasOne(row => row.Product).WithMany().HasForeignKey(row => row.ProductId)
      .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<EcomStockBalance>().HasOne(row => row.Organization).WithMany().HasForeignKey(row => row.OrganizationId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomStockBalance>().HasOne(row => row.Warehouse).WithMany().HasForeignKey(row => row.WarehouseId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomStockBalance>().HasOne(row => row.Product).WithMany().HasForeignKey(row => row.ProductId)
      .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<EcomStockMove>().HasOne(row => row.Doc).WithMany().HasForeignKey(row => row.DocId)
      .OnDelete(DeleteBehavior.Cascade);
    modelBuilder.Entity<EcomStockMove>().HasOne(row => row.DocLine).WithMany().HasForeignKey(row => row.DocLineId)
      .OnDelete(DeleteBehavior.Cascade);
    modelBuilder.Entity<EcomStockMove>().HasOne(row => row.Organization).WithMany().HasForeignKey(row => row.OrganizationId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomStockMove>().HasOne(row => row.Warehouse).WithMany().HasForeignKey(row => row.WarehouseId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomStockMove>().HasOne(row => row.Product).WithMany().HasForeignKey(row => row.ProductId)
      .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<EcomMoneyBalance>().HasOne(row => row.Organization).WithMany().HasForeignKey(row => row.OrganizationId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomMoneyBalance>().HasOne(row => row.MoneyAccount).WithMany().HasForeignKey(row => row.MoneyAccountId)
      .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<EcomMoneyMove>().HasOne(row => row.Doc).WithMany().HasForeignKey(row => row.DocId)
      .OnDelete(DeleteBehavior.Cascade);
    modelBuilder.Entity<EcomMoneyMove>().HasOne(row => row.Organization).WithMany().HasForeignKey(row => row.OrganizationId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<EcomMoneyMove>().HasOne(row => row.MoneyAccount).WithMany().HasForeignKey(row => row.MoneyAccountId)
      .OnDelete(DeleteBehavior.Restrict);
  }
}
