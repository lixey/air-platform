using DemoApp.Color;
using DemoEcom.DocType;

namespace DemoEcom.DocStatus;

/// <summary>A status of a document type, read only: a row of code.</summary>
public sealed class EcomDocStatusFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomFormService<EcomDocStatus>(db, ecom)
{
  public override Feature Feature => Ecom.DocStatus;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.DocTypeId).Label("Type").AsReference<EcomDocTypeGridService>().Span(6);
    elements.Add(e => e.ColorId).AsReference<AppColorGridService>().Span(6);
    elements.Add(e => e.Name).Focus().Span(6);
    elements.Add(e => e.Handle).Span(6);
    elements.Add(e => e.IsEditable).Label("Editable").AsCheckbox().Span(3);
    elements.Add(e => e.IsPosted).Label("Posted").AsCheckbox().Span(3);
    elements.Add(e => e.IsFinal).Label("Final").AsCheckbox().Span(3);
    elements.Add(e => e.IsCanceled).Label("Canceled").AsCheckbox().Span(3);
    elements.Add(e => e.SortOrder).Span(3);
  }
}
