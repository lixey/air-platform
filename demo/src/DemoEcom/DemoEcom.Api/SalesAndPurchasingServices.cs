using Air.App.Form;
using Air.App.Grid;
using Air.App.Modules;
using Air.Data;
using DemoApp;
using DemoApp.User;
using DemoEcom.Data;
using DemoEcom.Data.Models;
using DemoOrg;
using DemoOrg.Customer;
using DemoOrg.Warehouse;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.Sales;

public sealed class EcomSalesInvoiceGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.SalesInvoice;
  protected override string FormServicePath => "ecom/salesinvoiceform";

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>()
      .Include(d => d.Customer)
      .Include(d => d.Warehouse)
      .Where(d => d.DocTypeId == EcomData.DocType.SalesInvoiceId);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID").AsOpenForm<EcomSalesInvoiceFormService>();
    columns.Add(r => r.Number).Label("Number");
    columns.Add(r => r.Date).Label("Date");
    columns.Add(r => r.Customer).Label("Customer").AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Warehouse).Label("Warehouse").AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Amount).Label("Amount").AsMoney().Sum();
  }
}

public sealed class EcomSalesInvoiceFormService(EcomDbContext db) : FormService<EcomDoc>(db)
{
  public override Feature Feature => Ecom.SalesInvoice;
  protected override string Title => "Sales Invoice";

  protected override void SetElements(Elements<EcomDoc> elements)
  {
    elements.Add(e => e.Number).Label("Invoice Number").AsInfo().Span(4);
    elements.Add(e => e.Date).Label("Date").Span(4);
    elements.Add(e => e.CustomerId).Label("Customer").AsReference<OrgCustomerGridService>().Required().Span(4);
    elements.Add(e => e.WarehouseId).Label("Warehouse").AsReference<OrgWarehouseGridService>().Required().Span(4);
    elements.Add(e => e.Amount).Label("Total Amount").Span(4);
  }
}

public sealed class EcomSalesReturnGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.SalesReturn;

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>()
      .Include(d => d.Customer)
      .Where(d => d.DocTypeId == EcomData.DocType.SalesReturnId);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Number).Label("Number");
    columns.Add(r => r.Date).Label("Date");
    columns.Add(r => r.Customer).Label("Customer").AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Amount).Label("Amount").AsMoney().Sum();
  }
}

public sealed class EcomCustomerOrderFulfillmentGridService(EcomDbContext db) : GridService<EcomDocLine, EcomDocLine>(db)
{
  public override Feature Feature => Ecom.CustomerOrderFulfillment;

  protected override IQueryable<EcomDocLine> Query(PlatformDbContext context) =>
    context.Set<EcomDocLine>()
      .Include(l => l.Product)
      .Include(l => l.Doc);

  protected override void SetColumns(Columns<EcomDocLine> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Article).Label("Article / SKU");
    columns.Add(r => r.Qty).Label("Ordered Qty").Sum();
    columns.Add(r => r.QtyReserved).Label("Reserved").Sum();
    columns.Add(r => r.QtyDone).Label("Fulfilled").Sum();
  }
}

public sealed class EcomPurchaseOrderGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.PurchaseOrder;

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>().Where(d => d.DocTypeId == EcomData.DocType.PurchaseOrderId);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Number).Label("Number");
    columns.Add(r => r.Date).Label("Date");
    columns.Add(r => r.Amount).Label("Amount").AsMoney().Sum();
  }
}

public sealed class EcomSupplierInvoiceGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.SupplierInvoice;

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>().Where(d => d.DocTypeId == EcomData.DocType.SupplierInvoiceId);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Number).Label("Number");
    columns.Add(r => r.Date).Label("Date");
    columns.Add(r => r.Amount).Label("Amount").AsMoney().Sum();
  }
}

public sealed class EcomSupplierReturnGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.SupplierReturn;

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>().Where(d => d.DocTypeId == EcomData.DocType.SupplierReturnId);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Number).Label("Number");
    columns.Add(r => r.Date).Label("Date");
    columns.Add(r => r.Amount).Label("Amount").AsMoney().Sum();
  }
}
