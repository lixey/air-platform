using Air.Data;
using Air.Data.Tenancy;
using DemoOrg.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DemoOrg.Data;

public class OrgDbContext : PlatformDbContext
{
  public OrgDbContext()
  {
  }

  public OrgDbContext(DbContextOptions<OrgDbContext> options) : base(options)
  {
  }

  public OrgDbContext(DbContextOptions<OrgDbContext> options, ITenantContext tenantContext) : base(options, tenantContext)
  {
  }

  public DbSet<OrgOrganization> Organizations => Set<OrgOrganization>();
  public DbSet<OrgWarehouse> Warehouses => Set<OrgWarehouse>();
  public DbSet<OrgProduct> Products => Set<OrgProduct>();
  public DbSet<OrgCustomer> Customers => Set<OrgCustomer>();
  public DbSet<OrgBrand> Brands => Set<OrgBrand>();
  public DbSet<OrgProductCategory> ProductCategories => Set<OrgProductCategory>();
  public DbSet<OrgUnit> Units => Set<OrgUnit>();
  public DbSet<OrgCurrency> Currencies => Set<OrgCurrency>();
  public DbSet<OrgDeliveryType> DeliveryTypes => Set<OrgDeliveryType>();
  public DbSet<OrgPaymentMethod> PaymentMethods => Set<OrgPaymentMethod>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<OrgOrganization>(b => b.ToTable("OrgOrganizations"));
    modelBuilder.Entity<OrgWarehouse>(b => b.ToTable("OrgWarehouses"));
    modelBuilder.Entity<OrgProduct>(b =>
    {
      b.ToTable("OrgProducts");
      b.Property(p => p.Price).HasPrecision(18, 2);
      b.Property(p => p.PurchasePrice).HasPrecision(18, 2);
    });
    modelBuilder.Entity<OrgCustomer>(b => b.ToTable("OrgCustomers"));
    modelBuilder.Entity<OrgBrand>(b => b.ToTable("OrgBrands"));
    modelBuilder.Entity<OrgProductCategory>(b => b.ToTable("OrgProductCategories"));
    modelBuilder.Entity<OrgUnit>(b => b.ToTable("OrgUnits"));
    modelBuilder.Entity<OrgCurrency>(b => b.ToTable("OrgCurrencies"));
    modelBuilder.Entity<OrgDeliveryType>(b => b.ToTable("OrgDeliveryTypes"));
    modelBuilder.Entity<OrgPaymentMethod>(b => b.ToTable("OrgPaymentMethods"));
  }
}
