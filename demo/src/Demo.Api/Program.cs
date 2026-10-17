using Air.App.Host;
using Air.Data.Postgres;
using Air.Data.Tenancy;
using DemoApp;
using DemoApp.Data;
using DemoApp.Data.Models;
using DemoEcom;
using DemoEcom.Data;
using DemoEcom.Data.Models;
using DemoOrg;
using DemoOrg.Data;
using DemoOrg.Data.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure databases: PostgreSQL if configured, otherwise SQLite
var postgresConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(postgresConnection))
{
  builder.AddPlatformPostgres<AppDbContext>();
  builder.AddPlatformPostgres<OrgDbContext>();
  builder.AddPlatformPostgres<EcomDbContext>();
}
else
{
  builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=demo.db"));
  builder.Services.AddDbContext<OrgDbContext>(options => options.UseSqlite("Data Source=demo.db"));
  builder.Services.AddDbContext<EcomDbContext>(options => options.UseSqlite("Data Source=demo.db"));
}

builder.AddPlatformModule<AppModule>();
builder.AddPlatformModule<OrgModule>();
builder.AddPlatformModule<EcomModule>();

// CORS for Angular / Vite dev server
builder.Services.AddCors(options =>
{
  options.AddDefaultPolicy(policy =>
  {
    policy.SetIsOriginAllowed(origin => new Uri(origin).IsLoopback)
      .AllowAnyHeader()
      .AllowAnyMethod()
      .AllowCredentials();
  });
});

var app = builder.Build();

app.UseCors();

// Ensure database is created and seed demo data
using (var scope = app.Services.CreateScope())
{
  var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
  var orgDb = scope.ServiceProvider.GetRequiredService<OrgDbContext>();
  var ecomDb = scope.ServiceProvider.GetRequiredService<EcomDbContext>();

  await ecomDb.Database.EnsureDeletedAsync();
  await ecomDb.Database.EnsureCreatedAsync();

  using (AsyncLocalTenantContext.Use(1))
  {
    // 1. Seed Tenants
    if (!await appDb.AppTenant.AnyAsync())
    {
      appDb.AppTenant.AddRange(
        new AppTenant { Id = 1, Handle = "demo", Name = "Demo Enterprise", TimeZone = "UTC", IsActive = true },
        new AppTenant { Id = 2, Handle = "retail", Name = "Retail Branch", TimeZone = "UTC", ParentId = 1, IsActive = true }
      );
      await appDb.SaveChangesAsync();
    }

    // 2. Seed Colors
    if (!await appDb.AppColor.AnyAsync())
    {
      appDb.AppColor.AddRange(
        new AppColor { Id = 1, Handle = "sky", Name = "Sky Blue", IsActive = true },
        new AppColor { Id = 2, Handle = "indigo", Name = "Indigo", IsActive = true },
        new AppColor { Id = 3, Handle = "purple", Name = "Purple", IsActive = true },
        new AppColor { Id = 4, Handle = "teal", Name = "Teal", IsActive = true },
        new AppColor { Id = 5, Handle = "emerald", Name = "Emerald", IsActive = true },
        new AppColor { Id = 6, Handle = "amber", Name = "Amber", IsActive = true },
        new AppColor { Id = 7, Handle = "rose", Name = "Rose", IsActive = true }
      );
      await appDb.SaveChangesAsync();
    }

    // 3. Seed User Groups
    if (!await appDb.AppUserGroup.AnyAsync())
    {
      appDb.AppUserGroup.AddRange(
        new AppUserGroup { Id = 1, TenantId = 1, Name = "Administrators", ColorId = 3, IsActive = true },
        new AppUserGroup { Id = 2, TenantId = 1, Name = "Sales Managers", ColorId = 2, IsActive = true },
        new AppUserGroup { Id = 3, TenantId = 1, Name = "Support Engineers", ColorId = 4, IsActive = true }
      );
      await appDb.SaveChangesAsync();
    }

    // 4. Seed Roles
    if (!await appDb.AppAuthRole.AnyAsync())
    {
      appDb.AppAuthRole.AddRange(
        new AppAuthRole { Id = 1, TenantId = 1, Name = "Administrator", Description = "Full system administration", IsActive = true },
        new AppAuthRole { Id = 2, TenantId = 1, Name = "Store Manager", Description = "Inventory and orders management", IsActive = true },
        new AppAuthRole { Id = 3, TenantId = 1, Name = "Read-Only Auditor", Description = "Reports and audit view only", IsActive = true }
      );
      await appDb.SaveChangesAsync();
    }

    // 5. Seed Users
    if (!await appDb.AppUser.AnyAsync())
    {
      appDb.AppUser.AddRange(
        new AppUser { Id = 1, TenantId = 1, Login = "admin", Name = "Demo Administrator", Email = "admin@air.platform", UserGroupId = 1, IsActive = true },
        new AppUser { Id = 2, TenantId = 1, Login = "olena.koval", Name = "Olena Koval", Email = "olena.koval@air.platform", UserGroupId = 2, IsActive = true },
        new AppUser { Id = 3, TenantId = 1, Login = "dmytro.bond", Name = "Dmytro Bondarenko", Email = "dmytro.bond@air.platform", UserGroupId = 2, IsActive = true },
        new AppUser { Id = 4, TenantId = 1, Login = "iryna.melnyk", Name = "Iryna Melnyk", Email = "iryna.melnyk@air.platform", UserGroupId = 3, IsActive = true }
      );
      await appDb.SaveChangesAsync();
    }

    // 6. Seed Currencies, Units & Payment Terms
    if (!await orgDb.OrgCurrency.AnyAsync())
    {
      orgDb.OrgCurrency.AddRange(
        new OrgCurrency { Id = 1, Handle = "uah", Name = "Ukrainian Hryvnia", Symbol = "₴", IsActive = true },
        new OrgCurrency { Id = 2, Handle = "usd", Name = "US Dollar", Symbol = "$", IsActive = true },
        new OrgCurrency { Id = 3, Handle = "eur", Name = "Euro", Symbol = "€", IsActive = true }
      );
      orgDb.OrgUnit.AddRange(
        new OrgUnit { Id = 1, Handle = "pcs", Name = "Pieces", IsActive = true },
        new OrgUnit { Id = 2, Handle = "kg", Name = "Kilograms", IsActive = true },
        new OrgUnit { Id = 3, Handle = "box", Name = "Boxes", IsActive = true }
      );
      orgDb.OrgPaymentTerm.AddRange(
        new OrgPaymentTerm { Id = 1, Handle = "immediate", Name = "Immediate Payment", IsActive = true },
        new OrgPaymentTerm { Id = 2, Handle = "net14", Name = "Net 14 Days", IsActive = true },
        new OrgPaymentTerm { Id = 3, Handle = "net30", Name = "Net 30 Days", IsActive = true }
      );
      await orgDb.SaveChangesAsync();
    }

    // 7. Seed Organizations
    if (!await orgDb.OrgOrganization.AnyAsync())
    {
      orgDb.OrgOrganization.AddRange(
        new OrgOrganization { Id = 1, TenantId = 1, Code = "ACME-UA", Name = "Acme Trading Ukraine", ColorId = 1, CurrencyId = 1, IsActive = true },
        new OrgOrganization { Id = 2, TenantId = 1, Code = "ACME-EU", Name = "Acme Logistics Europe", ColorId = 2, CurrencyId = 3, IsActive = true }
      );
      await orgDb.SaveChangesAsync();
    }

    // 8. Seed Brands, Categories, Delivery Types, Payment Methods
    if (!await orgDb.OrgBrand.AnyAsync())
    {
      orgDb.OrgBrand.AddRange(
        new OrgBrand { Id = 1, TenantId = 1, Name = "Apple", Country = "USA", IsActive = true },
        new OrgBrand { Id = 2, TenantId = 1, Name = "Dell", Country = "USA", IsActive = true },
        new OrgBrand { Id = 3, TenantId = 1, Name = "LG", Country = "South Korea", IsActive = true },
        new OrgBrand { Id = 4, TenantId = 1, Name = "Logitech", Country = "Switzerland", IsActive = true },
        new OrgBrand { Id = 5, TenantId = 1, Name = "Keychron", Country = "China", IsActive = true },
        new OrgBrand { Id = 6, TenantId = 1, Name = "Sony", Country = "Japan", IsActive = true }
      );
      orgDb.OrgProductCategory.AddRange(
        new OrgProductCategory { Id = 1, TenantId = 1, Name = "Laptops & Computers", ColorId = 1, IsActive = true },
        new OrgProductCategory { Id = 2, TenantId = 1, Name = "Monitors & Displays", ColorId = 2, IsActive = true },
        new OrgProductCategory { Id = 3, TenantId = 1, Name = "Peripherals & Accessories", ColorId = 3, IsActive = true },
        new OrgProductCategory { Id = 4, TenantId = 1, Name = "Audio & Headphones", ColorId = 4, IsActive = true }
      );
      orgDb.OrgDeliveryType.AddRange(
        new OrgDeliveryType { Id = 1, TenantId = 1, Name = "Nova Poshta Express", IsActive = true },
        new OrgDeliveryType { Id = 2, TenantId = 1, Name = "Doorstep Courier", IsActive = true },
        new OrgDeliveryType { Id = 3, TenantId = 1, Name = "Warehouse Pickup", IsPickup = true, IsActive = true }
      );
      orgDb.OrgPaymentMethod.AddRange(
        new OrgPaymentMethod { Id = 1, TenantId = 1, Name = "Bank Wire Transfer (IBAN)", IsActive = true },
        new OrgPaymentMethod { Id = 2, TenantId = 1, Name = "Credit / Debit Card", IsActive = true },
        new OrgPaymentMethod { Id = 3, TenantId = 1, Name = "Cash on Delivery", IsCashOnDelivery = true, IsActive = true }
      );
      await orgDb.SaveChangesAsync();
    }

    // 9. Seed Warehouses
    if (!await orgDb.OrgWarehouse.AnyAsync())
    {
      orgDb.OrgWarehouse.AddRange(
        new OrgWarehouse { Id = 1, TenantId = 1, OrganizationId = 1, Name = "Central Logistics Hub", Address = "Kyiv, Stepana Bandery Ave 15", IsActive = true },
        new OrgWarehouse { Id = 2, TenantId = 1, OrganizationId = 1, Name = "Western Fulfillment Center", Address = "Lviv, Horodotska St 242", IsActive = true },
        new OrgWarehouse { Id = 3, TenantId = 1, OrganizationId = 2, Name = "Southern Transit Terminal", Address = "Odesa, Balkivska St 120", IsActive = true }
      );
      await orgDb.SaveChangesAsync();
    }

    // 10. Seed Customers & Counterparties
    if (!await orgDb.OrgCustomer.AnyAsync())
    {
      orgDb.OrgCustomer.AddRange(
        new OrgCustomer { Id = 1, TenantId = 1, Name = "NovaTech Solutions LLC", Email = "orders@novatech.ua", Phone = "+380 44 123 4567", City = "Kyiv", ManagerId = 2, DeliveryTypeId = 1, PaymentMethodId = 1, PaymentTermId = 2, IsActive = true },
        new OrgCustomer { Id = 2, TenantId = 1, Name = "AgroTrade Global", Email = "contact@agrotrade.com", Phone = "+380 32 987 6543", City = "Lviv", ManagerId = 2, DeliveryTypeId = 1, PaymentMethodId = 1, PaymentTermId = 2, IsActive = true },
        new OrgCustomer { Id = 3, TenantId = 1, Name = "Dnipro Industrial Group", Email = "sales@dnipro-ind.ua", Phone = "+380 56 333 4455", City = "Dnipro", ManagerId = 3, DeliveryTypeId = 2, PaymentMethodId = 2, PaymentTermId = 1, IsActive = true }
      );
      orgDb.OrgCounterparty.AddRange(
        new OrgCounterparty { Id = 1, TenantId = 1, Name = "Global Tech Supplies Ltd", TaxId = "38124567", IsSupplier = true, IsActive = true },
        new OrgCounterparty { Id = 2, TenantId = 1, Name = "EuroLogistics Partner GmbH", TaxId = "DE987654321", IsSupplier = true, IsActive = true }
      );
      await orgDb.SaveChangesAsync();
    }

    // 11. Seed Products
    if (!await orgDb.OrgProduct.AnyAsync())
    {
      orgDb.OrgProduct.AddRange(
        new OrgProduct { Id = 1, TenantId = 1, Article = "PRD-M3-MAX", Name = "Apple MacBook Pro 16\" M3 Max 64GB", Price = 159999m, PurchasePrice = 135000m, BrandId = 1, CategoryId = 1, UnitId = 1, SupplierId = 1, IsActive = true },
        new OrgProduct { Id = 2, TenantId = 1, Article = "PRD-DELL-XPS", Name = "Dell XPS 15 9530 Core i9 32GB", Price = 98500m, PurchasePrice = 82000m, BrandId = 2, CategoryId = 1, UnitId = 1, SupplierId = 1, IsActive = true },
        new OrgProduct { Id = 3, TenantId = 1, Article = "PRD-LG-ULTRA", Name = "LG UltraFine 32\" 4K Nano IPS Monitor", Price = 34999m, PurchasePrice = 28000m, BrandId = 3, CategoryId = 2, UnitId = 1, SupplierId = 2, IsActive = true },
        new OrgProduct { Id = 4, TenantId = 1, Article = "PRD-MX-MASTER", Name = "Logitech MX Master 3S Wireless Mouse", Price = 4999m, PurchasePrice = 3800m, BrandId = 4, CategoryId = 3, UnitId = 1, SupplierId = 2, IsActive = true },
        new OrgProduct { Id = 5, TenantId = 1, Article = "PRD-KEYCHRON", Name = "Keychron Q3 Pro Wireless Mechanical Keyboard", Price = 8499m, PurchasePrice = 6900m, BrandId = 5, CategoryId = 3, UnitId = 1, SupplierId = 2, IsActive = true },
        new OrgProduct { Id = 6, TenantId = 1, Article = "PRD-SONY-WH", Name = "Sony WH-1000XM5 Noise Canceling Headphones", Price = 16999m, PurchasePrice = 13800m, BrandId = 6, CategoryId = 4, UnitId = 1, SupplierId = 2, IsActive = true }
      );
      await orgDb.SaveChangesAsync();
    }

    // 12. Seed Ecom DocTypes and DocStatuses
    if (!await ecomDb.EcomDocType.AnyAsync())
    {
      ecomDb.EcomDocType.AddRange(
        new EcomDocType { Id = EcomData.DocType.CustomerOrderId, Handle = "CustomerOrder", Name = "Customer Order", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.PurchaseOrderId, Handle = "PurchaseOrder", Name = "Purchase Order", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.SalesInvoiceId, Handle = "SalesInvoice", Name = "Sales Invoice", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.SalesReturnId, Handle = "SalesReturn", Name = "Sales Return", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.SupplierInvoiceId, Handle = "SupplierInvoice", Name = "Supplier Invoice", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.SupplierReturnId, Handle = "SupplierReturn", Name = "Supplier Return", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.StockReceiptId, Handle = "StockReceipt", Name = "Stock Receipt", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.StockIssueId, Handle = "StockIssue", Name = "Stock Issue", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.StockTransferId, Handle = "StockTransfer", Name = "Stock Transfer", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.IncomingPaymentId, Handle = "IncomingPayment", Name = "Incoming Payment", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.OutgoingPaymentId, Handle = "OutgoingPayment", Name = "Outgoing Payment", IsActive = true }
      );
      ecomDb.EcomDocStatus.AddRange(
        new EcomDocStatus { Id = EcomData.DocStatus.CustomerOrderNewId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "New", Name = "New", ColorId = 1, IsActive = true },
        new EcomDocStatus { Id = EcomData.DocStatus.CustomerOrderInProgressId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "InProgress", Name = "In Progress", ColorId = 2, IsActive = true },
        new EcomDocStatus { Id = EcomData.DocStatus.CustomerOrderConfirmedId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "Confirmed", Name = "Confirmed", ColorId = 5, IsActive = true },
        new EcomDocStatus { Id = EcomData.DocStatus.CustomerOrderCompletedId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "Completed", Name = "Completed", ColorId = 4, IsActive = true },
        new EcomDocStatus { Id = EcomData.DocStatus.CustomerOrderCanceledId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "Canceled", Name = "Canceled", ColorId = 7, IsActive = true }
      );
      await ecomDb.SaveChangesAsync();
    }

    // 13. Seed Ecom Stock Balances
    if (!await ecomDb.EcomStockBalance.AnyAsync())
    {
      ecomDb.EcomStockBalance.AddRange(
        new EcomStockBalance { Id = 1, TenantId = 1, OrganizationId = 1, WarehouseId = 1, ProductId = 1, Qty = 24, Reserved = 2, Expected = 10 },
        new EcomStockBalance { Id = 2, TenantId = 1, OrganizationId = 1, WarehouseId = 1, ProductId = 2, Qty = 45, Reserved = 5, Expected = 15 },
        new EcomStockBalance { Id = 3, TenantId = 1, OrganizationId = 1, WarehouseId = 1, ProductId = 3, Qty = 80, Reserved = 10, Expected = 20 },
        new EcomStockBalance { Id = 4, TenantId = 1, OrganizationId = 1, WarehouseId = 1, ProductId = 4, Qty = 150, Reserved = 4, Expected = 50 },
        new EcomStockBalance { Id = 5, TenantId = 1, OrganizationId = 1, WarehouseId = 1, ProductId = 5, Qty = 60, Reserved = 5, Expected = 25 },
        new EcomStockBalance { Id = 6, TenantId = 1, OrganizationId = 1, WarehouseId = 1, ProductId = 6, Qty = 75, Reserved = 0, Expected = 30 }
      );
      await ecomDb.SaveChangesAsync();
    }

    // 14. Seed Customer Orders with Lines
    if (!await ecomDb.EcomDoc.AnyAsync())
    {
      var order1 = new EcomDoc
      {
        Id = 1,
        TenantId = 1,
        OrganizationId = 1,
        CurrencyId = 1,
        DocTypeId = EcomData.DocType.CustomerOrderId,
        DocStatusId = EcomData.DocStatus.CustomerOrderConfirmedId,
        Number = "ORD-2026-001",
        Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
        CustomerId = 1,
        WarehouseId = 1,
        ManagerId = 2,
        Qty = 4,
        Amount = 329996m,
        PaidAmount = 329996m,
        DeliveryAddress = "Kyiv, Khreshchatyk 1, Office 404",
        Note = "Priority shipment requested via Nova Poshta Express."
      };

      var order2 = new EcomDoc
      {
        Id = 2,
        TenantId = 1,
        OrganizationId = 1,
        CurrencyId = 1,
        DocTypeId = EcomData.DocType.CustomerOrderId,
        DocStatusId = EcomData.DocStatus.CustomerOrderInProgressId,
        Number = "ORD-2026-002",
        Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
        CustomerId = 2,
        WarehouseId = 1,
        ManagerId = 2,
        Qty = 10,
        Amount = 534995m,
        PaidAmount = 200000m,
        DeliveryAddress = "Lviv, Stryiska 45, AgroTrade Terminal",
        Note = "Partial advance payment received. Awaiting dispatch."
      };

      var order3 = new EcomDoc
      {
        Id = 3,
        TenantId = 1,
        OrganizationId = 1,
        CurrencyId = 1,
        DocTypeId = EcomData.DocType.CustomerOrderId,
        DocStatusId = EcomData.DocStatus.CustomerOrderNewId,
        Number = "ORD-2026-003",
        Date = DateOnly.FromDateTime(DateTime.UtcNow),
        CustomerId = 3,
        WarehouseId = 1,
        ManagerId = 3,
        Qty = 10,
        Amount = 349990m,
        PaidAmount = 0m,
        DeliveryAddress = "Dnipro, Yavornitskoho 20",
        Note = "New bulk request for design engineering dept."
      };

      ecomDb.EcomDoc.AddRange(order1, order2, order3);
      await ecomDb.SaveChangesAsync();

      ecomDb.EcomDocLine.AddRange(
        new EcomDocLine { Id = 1, DocId = 1, TenantId = 1, ProductId = 1, SortOrder = 1, Qty = 2, QtyReserved = 2, QtyDone = 0, Price = 159999m, Amount = 319998m },
        new EcomDocLine { Id = 2, DocId = 1, TenantId = 1, ProductId = 4, SortOrder = 2, Qty = 2, QtyReserved = 2, QtyDone = 0, Price = 4999m, Amount = 9998m },
        new EcomDocLine { Id = 3, DocId = 2, TenantId = 1, ProductId = 2, SortOrder = 1, Qty = 5, QtyReserved = 5, QtyDone = 0, Price = 98500m, Amount = 492500m },
        new EcomDocLine { Id = 4, DocId = 2, TenantId = 1, ProductId = 5, SortOrder = 2, Qty = 5, QtyReserved = 5, QtyDone = 0, Price = 8499m, Amount = 42495m },
        new EcomDocLine { Id = 5, DocId = 3, TenantId = 1, ProductId = 3, SortOrder = 1, Qty = 10, QtyReserved = 10, QtyDone = 0, Price = 34999m, Amount = 349990m }
      );
      await ecomDb.SaveChangesAsync();
    }
  }
}

app.MapPlatform();

app.Run();
