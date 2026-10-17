using Air.App.Grid;
using Air.App.Modules;
using Air.Data;
using DemoEcom.CustomerOrder;
using DemoEcom.Data;
using DemoEcom.Data.Models;
using DemoOrg;
using DemoOrg.Customer;
using DemoOrg.Warehouse;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.Doc;

public sealed class EcomDocGridService(EcomDbContext db) : GridService<EcomDoc, EcomDoc>(db)
{
  public override Feature Feature => Ecom.Doc;
  protected override string FormServicePath => "ecom/customerorderform";

  protected override IQueryable<EcomDoc> Query(PlatformDbContext context) =>
    context.Set<EcomDoc>()
      .Include(d => d.Customer)
      .Include(d => d.Warehouse)
      .Include(d => d.Organization);

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).Label("ID").AsOpenForm<EcomCustomerOrderFormService>();
    columns.Add(r => r.Number).Label("Number");
    columns.Add(r => r.Date).Label("Date");
    columns.Add(r => r.Customer).Label("Customer").AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Warehouse).Label("Warehouse").AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).Label("Amount").AsMoney().Sum();
  }
}

public sealed class EcomDocTypeGridService(EcomDbContext db) : GridService<EcomDocType, EcomDocType>(db)
{
  public override Feature Feature => Ecom.DocType;

  protected override void SetColumns(Columns<EcomDocType> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Handle).Label("Handle");
    columns.Add(r => r.Name).Label("Document Type");
  }
}

public sealed class EcomDocStatusGridService(EcomDbContext db) : GridService<EcomDocStatus, EcomDocStatus>(db)
{
  public override Feature Feature => Ecom.DocStatus;

  protected override void SetColumns(Columns<EcomDocStatus> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.DocTypeId).Label("Doc Type");
    columns.Add(r => r.Handle).Label("Handle");
    columns.Add(r => r.Name).Label("Status Name");
    columns.Add(r => r.Color).Label("Color");
  }
}
