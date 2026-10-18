using Air.App.Overlay;
using DemoEcom.Data;
using DemoEcom.Data.Documents;
using Microsoft.EntityFrameworkCore;

namespace DemoEcom.Documents;

public abstract class EcomDocFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomFormService<EcomDoc>(db, ecom)
{
  protected abstract long Type { get; }

  protected virtual EcomDocFlowService? Flow => null;

  protected IReadOnlyList<EcomDocStatus> Statuses { get; private set; } = [];

  protected EcomDoc? Doc { get; private set; }

  protected EcomDocStatus? Status { get; private set; }

  protected bool Frozen => Status is not { IsEditable: true };

  protected override bool IsReadOnly => base.IsReadOnly || Status is { IsFinal: true };

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override async Task SetElementsAsync(Elements elements, CancellationToken cancellationToken)
  {
    if (Flow != null)
    {
      elements.UseStatusOf(Flow);
    }
    Statuses = await EcomDocStatuses.OfTypeAsync(db, Type, cancellationToken);
    Doc = IsNew ? null : await db.EcomDoc.Include(doc => doc.DocStatus).FirstOrDefaultAsync(doc => doc.Id == Request.Id, cancellationToken);
    Status = IsNew ? (Statuses.Count > 0 ? Statuses[0] : null) : Doc?.DocStatus;
    await base.SetElementsAsync(elements, cancellationToken);
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.AfterLoad((c, cancellationToken) =>
      c.IsNew ? EcomDocDefaults.NewAsync(c.Entity, Type, cancellationToken) : Task.CompletedTask);
    hooks.BeforeSave((c, cancellationToken) =>
    {
      if (c.Entity.DocTypeId == 0)
      {
        c.Entity.DocTypeId = Type;
      }
      var status = Status ?? (Statuses.Count > 0 ? Statuses[0] : null);
      if (c.Entity.DocStatusId == 0 && status != null)
      {
        c.Entity.DocStatusId = status.Id;
      }
      return EcomDocActions.SaveAsync(db, c, null, status ?? new EcomDocStatus { IsEditable = true }, cancellationToken);
    });
  }
}
