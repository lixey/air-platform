using Air.App.Form;
using Air.App.Grid;
using Air.App.Modules;
using DemoOrg.Data;
using DemoOrg.Data.Models;

namespace DemoOrg;

public sealed class OrgOrganizationGridService(OrgDbContext db) : GridService<OrgOrganization, OrgOrganization>(db)
{
  public override Feature Feature => Org.Organization;
  protected override string FormServicePath => "org/organizationform";

  protected override void SetColumns(Columns<OrgOrganization> columns)
  {
    columns.Add(r => r.Id).Label("ID").AsOpenForm<OrgOrganizationFormService>();
    columns.Add(r => r.Code).Label("Code");
    columns.Add(r => r.Name).Label("Name");
    columns.Add(r => r.Color).Label("Color");
    columns.Add(r => r.IsActive).Label("Active").AsCheckbox();
  }
}

public sealed class OrgOrganizationFormService(OrgDbContext db) : FormService<OrgOrganization>(db)
{
  public override Feature Feature => Org.Organization;
  protected override string Title => "Organization";

  protected override void SetElements(Elements<OrgOrganization> elements)
  {
    elements.Add(e => e.Code).Label("Code").Span(4);
    elements.Add(e => e.Name).Label("Name").Span(8);
    elements.Add(e => e.Color).Label("Color").Span(6);
    elements.Add(e => e.IsActive).Label("Active").AsCheckbox().Span(6);
  }
}

public sealed class OrgBrandGridService(OrgDbContext db) : GridService<OrgBrand, OrgBrand>(db)
{
  public override Feature Feature => Org.Brand;
  protected override void SetColumns(Columns<OrgBrand> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Name).Label("Brand Name");
    columns.Add(r => r.IsActive).Label("Active").AsCheckbox();
  }
}

public sealed class OrgProductCategoryGridService(OrgDbContext db) : GridService<OrgProductCategory, OrgProductCategory>(db)
{
  public override Feature Feature => Org.ProductCategory;
  protected override void SetColumns(Columns<OrgProductCategory> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Name).Label("Category Name");
    columns.Add(r => r.IsActive).Label("Active").AsCheckbox();
  }
}

public sealed class OrgUnitGridService(OrgDbContext db) : GridService<OrgUnit, OrgUnit>(db)
{
  public override Feature Feature => Org.Unit;
  protected override void SetColumns(Columns<OrgUnit> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Code).Label("Code");
    columns.Add(r => r.Name).Label("Name");
  }
}

public sealed class OrgCurrencyGridService(OrgDbContext db) : GridService<OrgCurrency, OrgCurrency>(db)
{
  public override Feature Feature => Org.Currency;
  protected override void SetColumns(Columns<OrgCurrency> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Code).Label("Code");
    columns.Add(r => r.Name).Label("Name");
    columns.Add(r => r.Symbol).Label("Symbol");
  }
}

public sealed class OrgDeliveryTypeGridService(OrgDbContext db) : GridService<OrgDeliveryType, OrgDeliveryType>(db)
{
  public override Feature Feature => Org.DeliveryType;
  protected override void SetColumns(Columns<OrgDeliveryType> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Name).Label("Delivery Type");
    columns.Add(r => r.IsActive).Label("Active").AsCheckbox();
  }
}

public sealed class OrgPaymentMethodGridService(OrgDbContext db) : GridService<OrgPaymentMethod, OrgPaymentMethod>(db)
{
  public override Feature Feature => Org.PaymentMethod;
  protected override void SetColumns(Columns<OrgPaymentMethod> columns)
  {
    columns.Add(r => r.Id).Label("ID");
    columns.Add(r => r.Name).Label("Payment Method");
    columns.Add(r => r.IsActive).Label("Active").AsCheckbox();
  }
}
