namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomCustomerOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrder;

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomCustomerOrderLineFormService>();
    columns.Add(r => r.SortOrder);
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Product.Article);
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

  [RequireAction(Right.Edit)]
  public Task<GridActionDto> AddProduct(GridPickRequest request, CancellationToken cancellationToken = default) =>
    AttachPickedAsync<OrgProduct>(
      request,
      (_, picked, line, token) =>
      {
        if (ParentRecord<EcomDoc>() is not { } doc)
        {
          throw new KeyNotFoundException();
        }

        line.ProductId = picked.Id;
        line.Qty = 1m;
        return EcomDocLines.PriceAsync(db, doc, line, isNew: true, p => p.Price, token);
      },
      cancellationToken);
}

