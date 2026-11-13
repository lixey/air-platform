namespace DemoEcom.DocType;

/// <summary>A document type, read only: a row of code.</summary>
public sealed class EcomDocTypeFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomFormService<EcomDocType>(db, ecom)
{
  public override Feature Feature => Ecom.DocType;

  protected override void SetElements()
  {
    Elements.Add(e => e.Name).Focus();
    Elements.Add(e => e.Handle).Span(8);
    Elements.Add(e => e.Prefix).Span(4);
  }
}
