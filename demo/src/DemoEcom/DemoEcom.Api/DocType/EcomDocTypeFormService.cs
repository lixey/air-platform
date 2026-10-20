namespace DemoEcom.DocType;

/// <summary>A document type, read only: a row of code.</summary>
public sealed class EcomDocTypeFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomFormService<EcomDocType>(db, ecom)
{
  public override Feature Feature => Ecom.DocType;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Focus();
    elements.Add(e => e.Handle).Span(8);
    elements.Add(e => e.Prefix).Span(4);
  }
}
