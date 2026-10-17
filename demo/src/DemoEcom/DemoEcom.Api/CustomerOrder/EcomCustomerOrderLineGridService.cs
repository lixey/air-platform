using Air.App.Contracts;
using Air.App.Grid;
using DemoEcom.Data.Documents;
using DemoEcom.Data.Models;
using DemoEcom.Documents;
using DemoOrg.Product;
using Microsoft.EntityFrameworkCore;

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
    nav.AddPicker<EcomCustomerOrderProductGridService>(AddProduct, "Add by article").MinLength(3);
  }

  public async Task<GridActionDto> AddProduct(GridPickRequest request, CancellationToken cancellationToken = default)
  {
    if (request.ParentId is not { } parentId || parentId <= 0)
    {
      throw new InvalidOperationException("Parent customer order id is required.");
    }

    var order = await db.EcomDoc.FirstOrDefaultAsync(d => d.Id == parentId, cancellationToken)
      ?? throw new KeyNotFoundException($"Order {parentId} not found.");

    var product = await db.OrgProduct.FirstOrDefaultAsync(p => p.Id == request.PickedId, cancellationToken)
      ?? throw new KeyNotFoundException($"Product {request.PickedId} not found.");

    var line = new EcomDocLine
    {
      DocId = parentId,
      ProductId = product.Id,
      Qty = 1m
    };

    var input = new EcomDocLine();
    await EcomDocLines.PriceAsync(
      db,
      order,
      line,
      input,
      isNew: true,
      p => p.Price,
      cancellationToken
    );

    db.EcomDocLine.Add(line);
    await db.SaveChangesAsync(cancellationToken);

    await EcomDocTotals.ApplyAsync(db, order, cancellationToken);

    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }
}

