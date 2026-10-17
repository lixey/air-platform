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
  builder.AddPlatformPostgres<DemoDbContext>();
  builder.AddPlatformPostgres<OrgDbContext>();
  builder.AddPlatformPostgres<EcomDbContext>();
}
else
{
  builder.Services.AddDbContext<DemoDbContext>(options => options.UseSqlite("Data Source=demo.db"));
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
  var appDb = scope.ServiceProvider.GetRequiredService<DemoDbContext>();
  var orgDb = scope.ServiceProvider.GetRequiredService<OrgDbContext>();
  var ecomDb = scope.ServiceProvider.GetRequiredService<EcomDbContext>();

  if (appDb.Database.IsNpgsql())
  {
    await appDb.Database.ExecuteSqlRawAsync("""
      DROP TABLE IF EXISTS
        "AppTenants", "AppUserGroups", "AppAuthRoles", "AppUsers", "AppLanguages", "AppColors",
        "OrgOrganizations", "OrgProducts", "OrgCustomers", "OrgWarehouses", "OrgBrands", "OrgProductCategories", "OrgUnits", "OrgCurrencies", "OrgDeliveryTypes", "OrgPaymentMethods",
        "EcomDocTypes", "EcomDocStatuses", "EcomDocs", "EcomDocLines", "EcomStockBalances"
      CASCADE;

      CREATE TABLE IF NOT EXISTS "AppTenants" (
        "Id" bigint PRIMARY KEY,
        "Handle" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" text,
        "Comment" text,
        "TimeZone" character varying(50),
        "ParentId" bigint,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "AppUserGroups" (
        "Id" bigint PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "Handle" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" text,
        "Comment" text,
        "Color" character varying(50),
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "AppAuthRoles" (
        "Id" bigint PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "Handle" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" text,
        "Comment" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "AppLanguages" (
        "Id" bigint PRIMARY KEY,
        "Handle" character varying(50) NOT NULL,
        "Name" character varying(100) NOT NULL,
        "Description" text,
        "Comment" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "AppColors" (
        "Id" bigint PRIMARY KEY,
        "Handle" character varying(50) NOT NULL,
        "Name" character varying(100) NOT NULL,
        "Description" text,
        "Comment" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "AppUsers" (
        "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "Login" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Email" character varying(200) NOT NULL,
        "PasswordHash" text,
        "TimeZone" character varying(50),
        "IsActive" boolean NOT NULL DEFAULT true,
        "MustChangePassword" boolean NOT NULL DEFAULT false,
        "UserGroupId" bigint,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "OrgOrganizations" (
        "Id" bigint PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "Handle" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Code" character varying(50),
        "Color" character varying(50),
        "Description" text,
        "Comment" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "OrgBrands" (
        "Id" bigint PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "Handle" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" text,
        "Comment" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "OrgProductCategories" (
        "Id" bigint PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "Handle" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" text,
        "Comment" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "OrgUnits" (
        "Id" bigint PRIMARY KEY,
        "Handle" character varying(50) NOT NULL,
        "Code" character varying(50) NOT NULL,
        "Name" character varying(100) NOT NULL,
        "Description" text,
        "Comment" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "OrgCurrencies" (
        "Id" bigint PRIMARY KEY,
        "Handle" character varying(50) NOT NULL,
        "Code" character varying(50) NOT NULL,
        "Name" character varying(100) NOT NULL,
        "Symbol" character varying(10) NOT NULL,
        "Description" text,
        "Comment" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "OrgDeliveryTypes" (
        "Id" bigint PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "Handle" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" text,
        "Comment" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "OrgPaymentMethods" (
        "Id" bigint PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "Handle" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" text,
        "Comment" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "OrgProducts" (
        "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "Handle" character varying(100) NOT NULL DEFAULT '',
        "Article" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" text,
        "Comment" text,
        "Price" numeric(18,2) NOT NULL DEFAULT 0,
        "PurchasePrice" numeric(18,2) NOT NULL DEFAULT 0,
        "StockQuantity" integer NOT NULL DEFAULT 0,
        "BrandId" bigint,
        "CategoryId" bigint,
        "UnitId" bigint,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "OrgCustomers" (
        "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "Handle" character varying(100) NOT NULL DEFAULT '',
        "Code" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" text,
        "Comment" text,
        "Email" character varying(200),
        "Phone" character varying(100),
        "Address" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "OrgWarehouses" (
        "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "OrganizationId" bigint,
        "Handle" character varying(100) NOT NULL DEFAULT '',
        "Code" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" text,
        "Comment" text,
        "Address" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "EcomDocTypes" (
        "Id" bigint PRIMARY KEY,
        "Handle" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Description" text,
        "Comment" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "EcomDocStatuses" (
        "Id" bigint PRIMARY KEY,
        "DocTypeId" bigint NOT NULL,
        "Handle" character varying(100) NOT NULL,
        "Name" character varying(200) NOT NULL,
        "Color" character varying(50),
        "Description" text,
        "Comment" text,
        "SortOrder" integer NOT NULL DEFAULT 0,
        "IsActive" boolean NOT NULL DEFAULT true,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "EcomDocs" (
        "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "OrganizationId" bigint NOT NULL DEFAULT 1,
        "DocTypeId" bigint NOT NULL,
        "DocStatusId" bigint NOT NULL DEFAULT 101,
        "Number" character varying(50) NOT NULL,
        "Date" date NOT NULL,
        "CustomerId" bigint,
        "WarehouseId" bigint,
        "ManagerId" bigint,
        "Qty" numeric(18,4) NOT NULL DEFAULT 0,
        "Amount" numeric(18,2) NOT NULL DEFAULT 0,
        "PaidAmount" numeric(18,2) NOT NULL DEFAULT 0,
        "DeliveryAddress" text,
        "Note" text,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "EcomDocLines" (
        "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "DocId" bigint NOT NULL,
        "ProductId" bigint NOT NULL,
        "Article" character varying(100) NOT NULL,
        "SortOrder" integer NOT NULL DEFAULT 1,
        "Qty" numeric(18,4) NOT NULL DEFAULT 1,
        "QtyReserved" numeric(18,4) NOT NULL DEFAULT 0,
        "QtyDone" numeric(18,4) NOT NULL DEFAULT 0,
        "Price" numeric(18,2) NOT NULL DEFAULT 0,
        "DiscountPercent" numeric(18,2) NOT NULL DEFAULT 0,
        "Amount" numeric(18,2) NOT NULL DEFAULT 0,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );

      CREATE TABLE IF NOT EXISTS "EcomStockBalances" (
        "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
        "TenantId" bigint NOT NULL DEFAULT 1,
        "OrganizationId" bigint NOT NULL DEFAULT 1,
        "WarehouseId" bigint NOT NULL,
        "ProductId" bigint NOT NULL,
        "Article" character varying(100) NOT NULL,
        "Qty" numeric(18,4) NOT NULL DEFAULT 0,
        "Reserved" numeric(18,4) NOT NULL DEFAULT 0,
        "Expected" numeric(18,4) NOT NULL DEFAULT 0,
        "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
        "UpdatedAt" timestamp with time zone,
        "Ver" integer NOT NULL DEFAULT 1
      );
    """);
  }
  else
  {
    await appDb.Database.EnsureCreatedAsync();
    await orgDb.Database.EnsureCreatedAsync();
    await ecomDb.Database.EnsureCreatedAsync();
  }

  using (AsyncLocalTenantContext.Use(1))
  {
    // 1. Seed Tenants
    if (!await appDb.Tenants.AnyAsync())
    {
      appDb.Tenants.AddRange(
        new AppTenant { Id = 1, Handle = "demo", Name = "Demo Enterprise", TimeZone = "UTC", IsActive = true },
        new AppTenant { Id = 2, Handle = "retail", Name = "Retail Branch", TimeZone = "UTC", ParentId = 1, IsActive = true }
      );
      await appDb.SaveChangesAsync();
    }

    // 2. Seed Languages
    if (!await appDb.Languages.AnyAsync())
    {
      appDb.Languages.AddRange(
        new AppLanguage { Id = 1, Handle = "en", Name = "English", IsActive = true },
        new AppLanguage { Id = 2, Handle = "uk", Name = "Ukrainian", IsActive = true },
        new AppLanguage { Id = 3, Handle = "pl", Name = "Polish", IsActive = true }
      );
      await appDb.SaveChangesAsync();
    }

    // 3. Seed Colors
    if (!await appDb.Colors.AnyAsync())
    {
      appDb.Colors.AddRange(
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

    // 4. Seed User Groups
    if (!await appDb.UserGroups.AnyAsync())
    {
      appDb.UserGroups.AddRange(
        new AppUserGroup { Id = 1, TenantId = 1, Handle = "admins", Name = "Administrators", Color = "purple", Description = "System administrators", IsActive = true },
        new AppUserGroup { Id = 2, TenantId = 1, Handle = "managers", Name = "Sales Managers", Color = "indigo", Description = "Regional sales managers", IsActive = true },
        new AppUserGroup { Id = 3, TenantId = 1, Handle = "support", Name = "Support Engineers", Color = "teal", Description = "Customer support team", IsActive = true }
      );
      await appDb.SaveChangesAsync();
    }

    // 5. Seed Roles
    if (!await appDb.Roles.AnyAsync())
    {
      appDb.Roles.AddRange(
        new AppAuthRole { Id = 1, TenantId = 1, Handle = "admin", Name = "Administrator", Description = "Full system administration", IsActive = true },
        new AppAuthRole { Id = 2, TenantId = 1, Handle = "manager", Name = "Store Manager", Description = "Inventory and orders management", IsActive = true },
        new AppAuthRole { Id = 3, TenantId = 1, Handle = "viewer", Name = "Read-Only Auditor", Description = "Reports and audit view only", IsActive = true }
      );
      await appDb.SaveChangesAsync();
    }

    // 6. Seed Users
    if (!await appDb.Users.AnyAsync())
    {
      appDb.Users.AddRange(
        new AppUser { TenantId = 1, Login = "admin", Name = "Demo Administrator", Email = "admin@air.platform", UserGroupId = 1, IsActive = true },
        new AppUser { TenantId = 1, Login = "olena.koval", Name = "Olena Koval", Email = "olena.koval@air.platform", UserGroupId = 2, IsActive = true },
        new AppUser { TenantId = 1, Login = "dmytro.bond", Name = "Dmytro Bondarenko", Email = "dmytro.bond@air.platform", UserGroupId = 2, IsActive = true },
        new AppUser { TenantId = 1, Login = "iryna.melnyk", Name = "Iryna Melnyk", Email = "iryna.melnyk@air.platform", UserGroupId = 3, IsActive = true }
      );
      await appDb.SaveChangesAsync();
    }

    // 7. Seed Org Organizations
    if (!await orgDb.Organizations.AnyAsync())
    {
      orgDb.Organizations.AddRange(
        new OrgOrganization { Id = 1, TenantId = 1, Handle = "acme-ua", Name = "Acme Trading Ukraine", Code = "ACME-UA", Color = "sky", IsActive = true },
        new OrgOrganization { Id = 2, TenantId = 1, Handle = "acme-eu", Name = "Acme Logistics Europe", Code = "ACME-EU", Color = "indigo", IsActive = true }
      );
      await orgDb.SaveChangesAsync();
    }

    // 8. Seed Org Currencies & Units
    if (!await orgDb.Currencies.AnyAsync())
    {
      orgDb.Currencies.AddRange(
        new OrgCurrency { Id = 1, Handle = "uah", Code = "UAH", Name = "Ukrainian Hryvnia", Symbol = "₴", IsActive = true },
        new OrgCurrency { Id = 2, Handle = "usd", Code = "USD", Name = "US Dollar", Symbol = "$", IsActive = true },
        new OrgCurrency { Id = 3, Handle = "eur", Code = "EUR", Name = "Euro", Symbol = "€", IsActive = true }
      );
      orgDb.Units.AddRange(
        new OrgUnit { Id = 1, Handle = "pcs", Code = "PCS", Name = "Pieces", IsActive = true },
        new OrgUnit { Id = 2, Handle = "kg", Code = "KG", Name = "Kilograms", IsActive = true },
        new OrgUnit { Id = 3, Handle = "box", Code = "BOX", Name = "Boxes", IsActive = true }
      );
      orgDb.Brands.AddRange(
        new OrgBrand { Id = 1, TenantId = 1, Handle = "apple", Name = "Apple", IsActive = true },
        new OrgBrand { Id = 2, TenantId = 1, Handle = "dell", Name = "Dell", IsActive = true },
        new OrgBrand { Id = 3, TenantId = 1, Handle = "lg", Name = "LG", IsActive = true },
        new OrgBrand { Id = 4, TenantId = 1, Handle = "logitech", Name = "Logitech", IsActive = true },
        new OrgBrand { Id = 5, TenantId = 1, Handle = "keychron", Name = "Keychron", IsActive = true },
        new OrgBrand { Id = 6, TenantId = 1, Handle = "sony", Name = "Sony", IsActive = true }
      );
      orgDb.ProductCategories.AddRange(
        new OrgProductCategory { Id = 1, TenantId = 1, Handle = "laptops", Name = "Laptops & Computers", IsActive = true },
        new OrgProductCategory { Id = 2, TenantId = 1, Handle = "monitors", Name = "Monitors & Displays", IsActive = true },
        new OrgProductCategory { Id = 3, TenantId = 1, Handle = "peripherals", Name = "Peripherals & Accessories", IsActive = true },
        new OrgProductCategory { Id = 4, TenantId = 1, Handle = "audio", Name = "Audio & Headphones", IsActive = true }
      );
      orgDb.DeliveryTypes.AddRange(
        new OrgDeliveryType { Id = 1, TenantId = 1, Handle = "nova-poshta", Name = "Nova Poshta Express", IsActive = true },
        new OrgDeliveryType { Id = 2, TenantId = 1, Handle = "courier", Name = "Doorstep Courier", IsActive = true },
        new OrgDeliveryType { Id = 3, TenantId = 1, Handle = "pickup", Name = "Warehouse Pickup", IsActive = true }
      );
      orgDb.PaymentMethods.AddRange(
        new OrgPaymentMethod { Id = 1, TenantId = 1, Handle = "bank-wire", Name = "Bank Wire Transfer (IBAN)", IsActive = true },
        new OrgPaymentMethod { Id = 2, TenantId = 1, Handle = "card", Name = "Credit / Debit Card", IsActive = true },
        new OrgPaymentMethod { Id = 3, TenantId = 1, Handle = "cod", Name = "Cash on Delivery", IsActive = true }
      );
      await orgDb.SaveChangesAsync();
    }

    // 9. Seed Org Warehouses
    if (!await orgDb.Warehouses.AnyAsync())
    {
      orgDb.Warehouses.AddRange(
        new OrgWarehouse { Id = 1, TenantId = 1, OrganizationId = 1, Code = "WH-CENTRAL", Name = "Central Logistics Hub", Address = "Kyiv, Stepana Bandery Ave 15", IsActive = true },
        new OrgWarehouse { Id = 2, TenantId = 1, OrganizationId = 1, Code = "WH-WEST", Name = "Western Fulfillment Center", Address = "Lviv, Horodotska St 242", IsActive = true },
        new OrgWarehouse { Id = 3, TenantId = 1, OrganizationId = 2, Code = "WH-SOUTH", Name = "Southern Transit Terminal", Address = "Odesa, Balkivska St 120", IsActive = true }
      );
      await orgDb.SaveChangesAsync();
    }

    // 10. Seed Org Customers
    if (!await orgDb.Customers.AnyAsync())
    {
      orgDb.Customers.AddRange(
        new OrgCustomer { Id = 1, TenantId = 1, Code = "CUST-001", Name = "NovaTech Solutions LLC", Email = "orders@novatech.ua", Phone = "+380 44 123 4567", Address = "Kyiv, Khreshchatyk 1", IsActive = true },
        new OrgCustomer { Id = 2, TenantId = 1, Code = "CUST-002", Name = "AgroTrade Global", Email = "contact@agrotrade.com", Phone = "+380 32 987 6543", Address = "Lviv, Stryiska 45", IsActive = true },
        new OrgCustomer { Id = 3, TenantId = 1, Code = "CUST-003", Name = "Dnipro Industrial Group", Email = "sales@dnipro-ind.ua", Phone = "+380 56 333 4455", Address = "Dnipro, Yavornitskoho 20", IsActive = true },
        new OrgCustomer { Id = 4, TenantId = 1, Code = "CUST-004", Name = "Galych Commerce", Email = "info@galych.com.ua", Phone = "+380 34 555 7788", Address = "Ivano-Frankivsk, Nezalezhnosti 12", IsActive = true }
      );
      await orgDb.SaveChangesAsync();
    }

    // 11. Seed Org Products
    if (!await orgDb.Products.AnyAsync())
    {
      orgDb.Products.AddRange(
        new OrgProduct { Id = 1, TenantId = 1, Article = "PRD-M3-MAX", Name = "Apple MacBook Pro 16\" M3 Max 64GB", Price = 159999m, PurchasePrice = 135000m, StockQuantity = 24, BrandId = 1, CategoryId = 1, UnitId = 1, IsActive = true },
        new OrgProduct { Id = 2, TenantId = 1, Article = "PRD-DELL-XPS", Name = "Dell XPS 15 9530 Core i9 32GB", Price = 98500m, PurchasePrice = 82000m, StockQuantity = 45, BrandId = 2, CategoryId = 1, UnitId = 1, IsActive = true },
        new OrgProduct { Id = 3, TenantId = 1, Article = "PRD-LG-ULTRA", Name = "LG UltraFine 32\" 4K Nano IPS Monitor", Price = 34999m, PurchasePrice = 28000m, StockQuantity = 80, BrandId = 3, CategoryId = 2, UnitId = 1, IsActive = true },
        new OrgProduct { Id = 4, TenantId = 1, Article = "PRD-MX-MASTER", Name = "Logitech MX Master 3S Wireless Mouse", Price = 4999m, PurchasePrice = 3800m, StockQuantity = 150, BrandId = 4, CategoryId = 3, UnitId = 1, IsActive = true },
        new OrgProduct { Id = 5, TenantId = 1, Article = "PRD-KEYCHRON", Name = "Keychron Q3 Pro Wireless Mechanical Keyboard", Price = 8499m, PurchasePrice = 6900m, StockQuantity = 60, BrandId = 5, CategoryId = 3, UnitId = 1, IsActive = true },
        new OrgProduct { Id = 6, TenantId = 1, Article = "PRD-SONY-WH", Name = "Sony WH-1000XM5 Noise Canceling Headphones", Price = 16999m, PurchasePrice = 13800m, StockQuantity = 75, BrandId = 6, CategoryId = 4, UnitId = 1, IsActive = true }
      );
      await orgDb.SaveChangesAsync();
    }

    // 12. Seed Ecom DocTypes and DocStatuses
    if (!await ecomDb.DocTypes.AnyAsync())
    {
      ecomDb.DocTypes.AddRange(
        new EcomDocType { Id = EcomData.DocType.CustomerOrderId, Handle = "CustomerOrder", Name = "Customer Order", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.PurchaseOrderId, Handle = "PurchaseOrder", Name = "Purchase Order", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.SalesInvoiceId, Handle = "SalesInvoice", Name = "Sales Invoice", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.SalesReturnId, Handle = "SalesReturn", Name = "Sales Return", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.StockReceiptId, Handle = "StockReceipt", Name = "Stock Receipt", IsActive = true },
        new EcomDocType { Id = EcomData.DocType.IncomingPaymentId, Handle = "IncomingPayment", Name = "Incoming Payment", IsActive = true }
      );
      ecomDb.DocStatuses.AddRange(
        new EcomDocStatus { Id = EcomData.DocStatus.CustomerOrderNewId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "New", Name = "New", Color = "sky", IsActive = true },
        new EcomDocStatus { Id = EcomData.DocStatus.CustomerOrderInProgressId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "InProgress", Name = "In Progress", Color = "indigo", IsActive = true },
        new EcomDocStatus { Id = EcomData.DocStatus.CustomerOrderConfirmedId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "Confirmed", Name = "Confirmed", Color = "emerald", IsActive = true },
        new EcomDocStatus { Id = EcomData.DocStatus.CustomerOrderCompletedId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "Completed", Name = "Completed", Color = "teal", IsActive = true },
        new EcomDocStatus { Id = EcomData.DocStatus.CustomerOrderCanceledId, DocTypeId = EcomData.DocType.CustomerOrderId, Handle = "Canceled", Name = "Canceled", Color = "rose", IsActive = true }
      );
      await ecomDb.SaveChangesAsync();
    }

    // 13. Seed Ecom Stock Balances
    if (!await ecomDb.StockBalances.AnyAsync())
    {
      ecomDb.StockBalances.AddRange(
        new EcomStockBalance { Id = 1, TenantId = 1, OrganizationId = 1, WarehouseId = 1, ProductId = 1, Article = "PRD-M3-MAX", Qty = 24, Reserved = 2, Expected = 10 },
        new EcomStockBalance { Id = 2, TenantId = 1, OrganizationId = 1, WarehouseId = 1, ProductId = 2, Article = "PRD-DELL-XPS", Qty = 45, Reserved = 5, Expected = 15 },
        new EcomStockBalance { Id = 3, TenantId = 1, OrganizationId = 1, WarehouseId = 1, ProductId = 3, Article = "PRD-LG-ULTRA", Qty = 80, Reserved = 10, Expected = 20 },
        new EcomStockBalance { Id = 4, TenantId = 1, OrganizationId = 1, WarehouseId = 1, ProductId = 4, Article = "PRD-MX-MASTER", Qty = 150, Reserved = 4, Expected = 50 },
        new EcomStockBalance { Id = 5, TenantId = 1, OrganizationId = 1, WarehouseId = 1, ProductId = 5, Article = "PRD-KEYCHRON", Qty = 60, Reserved = 5, Expected = 25 },
        new EcomStockBalance { Id = 6, TenantId = 1, OrganizationId = 1, WarehouseId = 1, ProductId = 6, Article = "PRD-SONY-WH", Qty = 75, Reserved = 0, Expected = 30 }
      );
      await ecomDb.SaveChangesAsync();
    }

    // 14. Seed Customer Orders with Lines
    if (!await ecomDb.Docs.AnyAsync())
    {
      var order1 = new EcomDoc
      {
        Id = 1,
        TenantId = 1,
        OrganizationId = 1,
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
        Note = "Priority shipment requested via Nova Poshta Express.",
        Lines =
        [
          new EcomDocLine { Id = 1, DocId = 1, TenantId = 1, ProductId = 1, Article = "PRD-M3-MAX", SortOrder = 1, Qty = 2, QtyReserved = 2, QtyDone = 0, Price = 159999m, Amount = 319998m },
          new EcomDocLine { Id = 2, DocId = 1, TenantId = 1, ProductId = 4, Article = "PRD-MX-MASTER", SortOrder = 2, Qty = 2, QtyReserved = 2, QtyDone = 0, Price = 4999m, Amount = 9998m }
        ]
      };

      var order2 = new EcomDoc
      {
        Id = 2,
        TenantId = 1,
        OrganizationId = 1,
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
        Note = "Partial advance payment received. Awaiting dispatch.",
        Lines =
        [
          new EcomDocLine { Id = 3, DocId = 2, TenantId = 1, ProductId = 2, Article = "PRD-DELL-XPS", SortOrder = 1, Qty = 5, QtyReserved = 5, QtyDone = 0, Price = 98500m, Amount = 492500m },
          new EcomDocLine { Id = 4, DocId = 2, TenantId = 1, ProductId = 5, Article = "PRD-KEYCHRON", SortOrder = 2, Qty = 5, QtyReserved = 5, QtyDone = 0, Price = 8499m, Amount = 42495m }
        ]
      };

      var order3 = new EcomDoc
      {
        Id = 3,
        TenantId = 1,
        OrganizationId = 1,
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
        Note = "New bulk request for design engineering dept.",
        Lines =
        [
          new EcomDocLine { Id = 5, DocId = 3, TenantId = 1, ProductId = 3, Article = "PRD-LG-ULTRA", SortOrder = 1, Qty = 10, QtyReserved = 10, QtyDone = 0, Price = 34999m, Amount = 349990m }
        ]
      };

      ecomDb.Docs.AddRange(order1, order2, order3);
      await ecomDb.SaveChangesAsync();
    }
  }
}

app.MapPlatform();

app.Run();
