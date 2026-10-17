using Air.App.Form;
using Air.App.Grid;
using Air.App.Modules;
using DemoOrg.Data;
using DemoOrg.Data.Models;

namespace DemoOrg.Warehouse;

public sealed class OrgWarehouseGridService(OrgDbContext db) : GridService<OrgWarehouse, OrgWarehouse>(db)
{
  public override Feature Feature => Org.Warehouse;
  protected override string FormServicePath => "org/warehouseform";

  protected override void SetColumns(Columns<OrgWarehouse> columns)
  {
    columns.Add(r => r.Id).Label("ID").AsOpenForm<OrgWarehouseFormService>();
    columns.Add(r => r.Code).Label("Code");
    columns.Add(r => r.Name).Label("Warehouse Name");
    columns.Add(r => r.Address).Label("Address");
    columns.Add(r => r.IsActive).Label("Active").AsCheckbox();
  }
}

public sealed class OrgWarehouseFormService(OrgDbContext db) : FormService<OrgWarehouse>(db)
{
  public override Feature Feature => Org.Warehouse;
  protected override string Title => "Warehouse";

  protected override void SetElements(Elements<OrgWarehouse> elements)
  {
    elements.Add(e => e.Code).Label("Warehouse Code").Span(4);
    elements.Add(e => e.Name).Label("Warehouse Name").Span(8);
    elements.Add(e => e.Address).Label("Address").Span(12);
    elements.Add(e => e.IsActive).Label("Active").AsCheckbox().Span(12);
  }
}
