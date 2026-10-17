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
using DemoOrg.Product;
using DemoOrg.Warehouse;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.CustomerOrder;
  protected override string FormServicePath => "ecom/customerorderform";

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>()
      .Include(d => d.Customer)
      .Include(d => d.Warehouse)
      .Include(d => d.Manager)
      .Include(d => d.Organization)
      .Where(d => d.DocTypeId == EcomData.DocType.CustomerOrderId);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID").AsOpenForm<EcomCustomerOrderFormService>();
    columns.Add(r => r.Number).Label("Number");
    columns.Add(r => r.Date).Label("Date");
    columns.Add(r => r.Customer).Label("Customer").AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Warehouse).Label("Warehouse").AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Manager).Label("Manager").AsReference<AppUserGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).Label("Amount").AsMoney().Sum();
    columns.Add(r => r.PaidAmount).Label("Paid").AsMoney().Sum();
  }
}

public sealed class EcomCustomerOrderFormService(EcomDbContext db) : FormService<EcomDoc>(db)
{
  public override Feature Feature => Ecom.CustomerOrder;
  protected override string Title => "Customer Order";

  protected override void SetElements(Elements<EcomDoc> elements)
  {
    elements.Add(e => e.Number).Label("Order Number").AsInfo().Span(4);
    elements.Add(e => e.Date).Label("Date").Span(4);
    elements.Add(e => e.ManagerId).Label("Manager").AsReference<AppUserGridService>().Span(4);

    elements.Add(e => e.CustomerId).Label("Customer").AsReference<OrgCustomerGridService>().Required().Span(6);
    elements.Add(e => e.WarehouseId).Label("Warehouse").AsReference<OrgWarehouseGridService>().Required().Span(6);

    elements.AddSeparator("Delivery and Payment");
    elements.Add(e => e.DeliveryAddress).Label("Delivery Address").Span(12);
    elements.Add(e => e.Note).Label("Notes").AsTextarea().Span(12);

    elements.AddChild<EcomCustomerOrderLineGridService>().Key("lines").Label("Lines");
  }
}

public sealed class EcomCustomerOrderLineGridService(EcomDbContext db) : GridService<EcomDocLine, EcomDocLine>(db)
{
  public override Feature Feature => Ecom.CustomerOrder;
  protected override string FormServicePath => "ecom/customerorderlineform";

  protected override IQueryable<EcomDocLine> Query(PlatformDbContext context) =>
    context.Set<EcomDocLine>().Include(l => l.Product);

  protected override void SetColumns(Columns<EcomDocLine> columns)
  {
    columns.Add(r => r.Id).Label("ID").AsOpenForm<EcomCustomerOrderLineFormService>();
    columns.Add(r => r.SortOrder).Label("#");
    columns.Add(r => r.Product).Label("Product").AsReference<OrgProductGridService>();
    columns.Add(r => r.Article).Label("SKU / Article");
    columns.Add(r => r.Qty).Label("Quantity").Sum().Editable();
    columns.Add(r => r.Price).Label("Price").AsMoney().Editable();
    columns.Add(r => r.DiscountPercent).Label("Discount %").AsPercent().Editable();
    columns.Add(r => r.Amount).Label("Amount").AsMoney().Sum();
  }
}

public sealed class EcomCustomerOrderLineFormService(EcomDbContext db) : FormService<EcomDocLine>(db)
{
  public override Feature Feature => Ecom.CustomerOrder;
  protected override string Title => "Order Line";

  protected override void SetElements(Elements<EcomDocLine> elements)
  {
    elements.Add(e => e.ProductId).Label("Product").AsReference<OrgProductGridService>().Span(6);
    elements.Add(e => e.Article).Label("Article / SKU").Span(6);
    elements.Add(e => e.Qty).Label("Quantity").Span(4);
    elements.Add(e => e.Price).Label("Price").Span(4);
    elements.Add(e => e.DiscountPercent).Label("Discount %").Span(4);
    elements.Add(e => e.Amount).Label("Amount").Span(6);
  }
}
