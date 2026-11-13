namespace DemoEcom.Documents;

public abstract class EcomDocFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomFormService<EcomDoc>(db, ecom)
{
  protected abstract long Type { get; }

  protected virtual EcomDocFlowService? Flow => null;

  protected IReadOnlyList<EcomDocStatus> Statuses
  {
    get => ExecutionItems.TryGetValue(nameof(Statuses), out var v) && v is IReadOnlyList<EcomDocStatus> list ? list : [];
    private set => ExecutionItems[nameof(Statuses)] = value;
  }

  protected EcomDoc? Doc => CurrentEntity;

  protected EcomDocStatus? Status
  {
    get => ExecutionItems.TryGetValue(nameof(Status), out var v) && v is EcomDocStatus s ? s : Doc?.DocStatus;
    private set => ExecutionItems[nameof(Status)] = value;
  }

  protected bool Frozen => Status is not { IsEditable: true };

  protected override bool IsReadOnly => base.IsReadOnly || Status is { IsFinal: true };

  protected override void SetQuery(Query query)
  {
    query.From(db.EcomDoc.Include(doc => doc.DocStatus));
    query.Where(doc => doc.DocTypeId == Type);
  }

  protected override async Task SetElementsAsync(Elements elements, CancellationToken cancellationToken)
  {
    if (Flow != null)
    {
      elements.UseStatusOf(Flow);
    }

    var statuses = await EcomDocStatuses.OfTypeAsync(db, Type, cancellationToken);
    Statuses = statuses;
    Status = IsNew
      ? statuses.FirstOrDefault()
      : (Doc?.DocStatus ?? statuses.FirstOrDefault(s => s.Id == Doc?.DocStatusId));
    await base.SetElementsAsync(elements, cancellationToken);
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.AfterLoad((c, cancellationToken) =>
      c.IsNew ? EcomDocDefaults.NewAsync(c.Entity, Type, Time.Today, cancellationToken) : Task.CompletedTask);
    hooks.BeforeSave(async (c, cancellationToken) =>
    {
      if (c.Entity.DocTypeId == 0)
      {
        c.Entity.DocTypeId = Type;
      }

      var statuses = Statuses.Count > 0 ? Statuses : await EcomDocStatuses.OfTypeAsync(db, Type, cancellationToken);
      var status = c.IsNew
        ? (statuses.Count > 0 ? statuses[0] : null)
        : statuses.FirstOrDefault(s => s.Id == c.Entity.DocStatusId);
      if (c.Entity.DocStatusId == 0 && status != null)
      {
        c.Entity.DocStatusId = status.Id;
      }

      await EcomDocActions.SaveAsync(db, c, User?.UserId, status ?? new EcomDocStatus { IsEditable = true }, cancellationToken);
    });
  }
}

