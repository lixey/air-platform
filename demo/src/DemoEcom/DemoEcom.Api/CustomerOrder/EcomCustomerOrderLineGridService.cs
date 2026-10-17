using Air.App.Contracts;
using Air.App.Grid;
using DemoEcom.Documents;
using DemoOrg.Product;

namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomCustomerOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrder;

  protected override void SetColumns(Columns<EcomDocLineRow> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomCustomerOrderLineFormService>();
    columns.Add(r => r.SortOrder);
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Article);
    columns.Add(r => r.Qty).Label("Quantity").Sum().Editable();
    columns.Add(r => r.QtyReserved).Label("Reserved").Sum();
    columns.Add(r => r.QtyDone).Label("Shipped").Sum();
    columns.Add(r => r.Price).AsMoney().Editable();
    columns.Add(r => r.DiscountPercent).Label("Discount").AsPercent().Editable();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);
    nav.AddPicker<OrgProductGridService>(AddProduct, "Add by article").MinLength(3);
  }

  public Task<GridActionDto> AddProduct(GridPickRequest request, CancellationToken cancellationToken = default)
  {
    return Task.FromResult(new GridActionDto { Outcome = MutationOutcome.Committed });
  }
}
