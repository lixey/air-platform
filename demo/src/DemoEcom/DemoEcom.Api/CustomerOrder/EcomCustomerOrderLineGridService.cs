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
    nav.AddPicker<EcomCustomerOrderProductGridService>(AddProduct, L["Add by article"]).MinLength(3);
  }

  [RequireAction(EndpointAction.Edit)]
  public async Task<GridActionDto> AddProduct(GridPickRequest request, CancellationToken cancellationToken = default)
  {
    if (request.ParentId is not { } parentId || parentId <= 0)
    {
      throw new InvalidOperationException("Parent customer order id is required.");
    }

    ParentId = parentId;
    if (IsReadOnly)
    {
      return new GridActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = EcomDocTexts.Of(L).NotEditable, Code = EcomDocActions.NotEditable, Type = MessageType.Error }]
      };
    }

    var order = await db.EcomDoc.FirstOrDefaultAsync(
      d => d.Id == parentId && d.DocTypeId == DemoEcom.Data.EcomData.DocType.CustomerOrderId,
      cancellationToken)
      ?? throw new KeyNotFoundException($"Order {parentId} not found.");

    var product = await db.OrgProduct.FirstOrDefaultAsync(p => p.Id == request.PickedId, cancellationToken)
      ?? throw new KeyNotFoundException($"Product {request.PickedId} not found.");

    var line = new EcomDocLine
    {
      DocId = parentId,
      ProductId = product.Id,
      Qty = 1m
    };

    var input = new EcomDocLineEdit();
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
    await db.EcomDoc.Where(d => d.Id == order.Id)
      .ExecuteUpdateAsync(s => s
        .SetProperty(d => d.Qty, order.Qty)
        .SetProperty(d => d.Amount, order.Amount), cancellationToken);
    db.Entry(order).State = EntityState.Unchanged;

    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }
}

