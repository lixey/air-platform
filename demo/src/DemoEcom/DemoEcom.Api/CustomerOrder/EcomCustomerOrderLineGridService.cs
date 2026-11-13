namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomCustomerOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrder;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomCustomerOrderLineFormService>();
    Columns.Add(r => r.SortOrder);
    Columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    Columns.Add(r => r.Product.Article);
    Columns.Add(r => r.Qty).Label("Quantity").Sum().Editable();
    Columns.Add(r => r.QtyReserved).Label("Reserved").Sum();
    Columns.Add(r => r.QtyDone).Label("Shipped").Sum();
    Columns.Add(r => r.Price).AsMoney().Editable();
    Columns.Add(r => r.DiscountPercent).Label("Discount").AsPercent().Editable();
    Columns.Add(r => r.Amount).AsMoney().Sum();
  }

  protected override void SetNav()
  {
    base.SetNav();
    Nav.AddPicker<EcomCustomerOrderProductGridService>(AddProduct, L["Add by article"]).MinLength(3);
  }

  [Endpoint]
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

