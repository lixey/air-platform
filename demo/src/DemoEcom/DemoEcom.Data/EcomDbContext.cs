using Air.Data;
using Air.Data.Tenancy;
using DemoApp.Data.Models;
using DemoEcom.Data.Models;
using DemoOrg.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.Data;

public class EcomDbContext : PlatformDbContext
{
  public EcomDbContext()
  {
  }

  public EcomDbContext(DbContextOptions<EcomDbContext> options) : base(options)
  {
  }

  public EcomDbContext(DbContextOptions<EcomDbContext> options, ITenantContext tenantContext) : base(options, tenantContext)
  {
  }

  public DbSet<EcomDoc> Docs => Set<EcomDoc>();
  public DbSet<EcomDocLine> DocLines => Set<EcomDocLine>();
  public DbSet<EcomDocType> DocTypes => Set<EcomDocType>();
  public DbSet<EcomDocStatus> DocStatuses => Set<EcomDocStatus>();
  public DbSet<EcomStockBalance> StockBalances => Set<EcomStockBalance>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<EcomDoc>(b =>
    {
      b.ToTable("EcomDocs");
      b.Property(d => d.Number).HasMaxLength(50).IsRequired();
      b.Property(d => d.Qty).HasPrecision(18, 4);
      b.Property(d => d.Amount).HasPrecision(18, 2);
      b.Property(d => d.PaidAmount).HasPrecision(18, 2);
      b.HasMany(d => d.Lines)
        .WithOne(l => l.Doc)
        .HasForeignKey(l => l.DocId)
        .OnDelete(DeleteBehavior.Cascade);
    });

    modelBuilder.Entity<EcomDocLine>(b =>
    {
      b.ToTable("EcomDocLines");
      b.Property(l => l.Article).HasMaxLength(100).IsRequired();
      b.Property(l => l.Qty).HasPrecision(18, 4);
      b.Property(l => l.QtyReserved).HasPrecision(18, 4);
      b.Property(l => l.QtyDone).HasPrecision(18, 4);
      b.Property(l => l.Price).HasPrecision(18, 2);
      b.Property(l => l.DiscountPercent).HasPrecision(18, 2);
      b.Property(l => l.Amount).HasPrecision(18, 2);
    });

    modelBuilder.Entity<EcomDocType>(b => b.ToTable("EcomDocTypes"));
    modelBuilder.Entity<EcomDocStatus>(b => b.ToTable("EcomDocStatuses"));

    modelBuilder.Entity<OrgCustomer>(b => b.ToTable("OrgCustomers"));
    modelBuilder.Entity<OrgWarehouse>(b => b.ToTable("OrgWarehouses"));
    modelBuilder.Entity<OrgOrganization>(b => b.ToTable("OrgOrganizations"));
    modelBuilder.Entity<OrgProduct>(b => b.ToTable("OrgProducts"));
    modelBuilder.Entity<AppUser>(b => b.ToTable("AppUsers"));

    modelBuilder.Entity<EcomStockBalance>(b =>
    {
      b.ToTable("EcomStockBalances");
      b.Property(s => s.Article).HasMaxLength(100).IsRequired();
      b.Property(s => s.Qty).HasPrecision(18, 4);
      b.Property(s => s.Reserved).HasPrecision(18, 4);
      b.Property(s => s.Expected).HasPrecision(18, 4);
    });
  }
}
