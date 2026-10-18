namespace DemoOrg.Data;

public class OrgDbContext : AppDbContext
{
  public new string ModuleHandle => OrgData.ModuleHandle;
  public OrgDbContext(DbContextOptions<OrgDbContext> options, ITenantContext? tenant = null) : base(options, tenant) { }

  protected OrgDbContext(DbContextOptions options, ITenantContext? tenant) : base(options, tenant) { }

  public DbSet<OrgCurrency> OrgCurrency => Set<OrgCurrency>();
  public DbSet<OrgUnit> OrgUnit => Set<OrgUnit>();
  public DbSet<OrgOrganization> OrgOrganization => Set<OrgOrganization>();
  public DbSet<OrgWarehouse> OrgWarehouse => Set<OrgWarehouse>();
  public DbSet<OrgMoneyAccount> OrgMoneyAccount => Set<OrgMoneyAccount>();
  public DbSet<OrgPaymentTerm> OrgPaymentTerm => Set<OrgPaymentTerm>();
  public DbSet<OrgDeliveryType> OrgDeliveryType => Set<OrgDeliveryType>();
  public DbSet<OrgPaymentMethod> OrgPaymentMethod => Set<OrgPaymentMethod>();
  public DbSet<OrgCustomer> OrgCustomer => Set<OrgCustomer>();
  public DbSet<OrgCustomerContact> OrgCustomerContact => Set<OrgCustomerContact>();
  public DbSet<OrgCounterparty> OrgCounterparty => Set<OrgCounterparty>();
  public DbSet<OrgBrand> OrgBrand => Set<OrgBrand>();
  public DbSet<OrgProductCategory> OrgProductCategory => Set<OrgProductCategory>();
  public DbSet<OrgProduct> OrgProduct => Set<OrgProduct>();
  public DbSet<OrgProductFile> OrgProductFile => Set<OrgProductFile>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<OrgOrganization>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgWarehouse>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgMoneyAccount>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgDeliveryType>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgPaymentMethod>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgCustomer>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgCustomerContact>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgCounterparty>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgBrand>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgProductCategory>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgProduct>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<OrgOrganization>().HasOne(row => row.Color).WithMany().HasForeignKey(row => row.ColorId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgOrganization>().HasOne(row => row.Currency).WithMany().HasForeignKey(row => row.CurrencyId)
      .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<OrgWarehouse>().HasOne(row => row.Organization).WithMany().HasForeignKey(row => row.OrganizationId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgMoneyAccount>().HasOne(row => row.Organization).WithMany().HasForeignKey(row => row.OrganizationId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgMoneyAccount>().HasOne(row => row.Currency).WithMany().HasForeignKey(row => row.CurrencyId)
      .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<OrgCustomerContact>().HasOne(row => row.Customer).WithMany().HasForeignKey(row => row.CustomerId)
      .OnDelete(DeleteBehavior.Cascade);
    modelBuilder.Entity<OrgCustomer>().HasOne(row => row.Manager).WithMany().HasForeignKey(row => row.ManagerId)
      .OnDelete(DeleteBehavior.SetNull);
    modelBuilder.Entity<OrgCustomer>().HasOne(row => row.DeliveryType).WithMany().HasForeignKey(row => row.DeliveryTypeId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgCustomer>().HasOne(row => row.PaymentMethod).WithMany().HasForeignKey(row => row.PaymentMethodId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgCustomer>().HasOne(row => row.PaymentTerm).WithMany().HasForeignKey(row => row.PaymentTermId)
      .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<OrgCounterparty>().HasOne(row => row.Customer).WithMany().HasForeignKey(row => row.CustomerId)
      .OnDelete(DeleteBehavior.SetNull);

    modelBuilder.Entity<OrgProduct>().HasOne(row => row.Supplier).WithMany().HasForeignKey(row => row.SupplierId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgProduct>().HasOne(row => row.Brand).WithMany().HasForeignKey(row => row.BrandId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgProduct>().HasOne(row => row.Category).WithMany().HasForeignKey(row => row.CategoryId)
      .OnDelete(DeleteBehavior.SetNull);
    modelBuilder.Entity<OrgProduct>().HasOne(row => row.Unit).WithMany().HasForeignKey(row => row.UnitId)
      .OnDelete(DeleteBehavior.Restrict);

    modelBuilder.Entity<OrgProductFile>().HasOne(row => row.Tenant).WithMany().HasForeignKey(row => row.TenantId)
      .OnDelete(DeleteBehavior.Restrict);
    modelBuilder.Entity<OrgProductFile>().HasOne(row => row.Product).WithMany().HasForeignKey(row => row.ProductId)
      .OnDelete(DeleteBehavior.Cascade);
  }
}
