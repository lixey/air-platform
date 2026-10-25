namespace DemoOrg.Product;

public sealed class OrgProductFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgProduct>(db, org)
{
  public const string Currency = "UAH";

  public override Feature Feature => Org.Product;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.SupplierId).AsReference<OrgSupplierGridService>().Required().Span(6);
    elements.Add(e => e.BrandId).AsReference<OrgBrandGridService>().Required().Span(6);
    elements.Add(e => e.CategoryId).AsReference<OrgProductCategoryGridService>().Span(6);
    elements.Add(e => e.UnitId).AsReference<OrgUnitGridService>().Required().Span(6);
    elements.Add(e => e.Article)
      .Unique("The supplier already has a product of this brand and article.", e => e.SupplierId, e => e.BrandId).Span(4);
    elements.Add(e => e.Name).Focus().Span(8);
    elements.Add(e => e.Barcode).Span(6);
    elements.Add(e => e.CustomsCode).Span(6);
    elements.Add(e => e.Weight).Label("Weight, kg").Min(0).Step(0.001).Span(4);
    elements.Add(e => e.Price).Min(0).AsMoney(Currency).Span(4);
    elements.Add(e => e.PurchasePrice).Min(0).AsMoney(Currency).Span(4);
    elements.Add(e => e.IsActive).AsCheckbox();
    elements.Add(e => e.Description).AsTextarea();
    elements.AddChild<OrgProductFileGridService>().Key("files").Label("Photos").AsFiles();
  }

  protected override void SetRules(Rules rules)
  {
    var customsCode = L["A customs code is 4, 6, 8 or 10 digits."];
    var barcode = L["A barcode is 8 to 14 digits."];
    rules.For(e => e.CustomsCode).Matches(@"\A(?:[0-9]{4}|[0-9]{6}|[0-9]{8}|[0-9]{10})\z")
      .WithMessage(customsCode).WithErrorCode("product.customsCode");
    rules.For(e => e.Barcode).Matches(@"\A[0-9]{8,14}\z").WithMessage(barcode).WithErrorCode("product.barcode");
  }
}
