namespace DemoEcom.Documents;

public abstract class EcomDocLineGridService<TDocForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocLine, EcomDocLineRow>(db, ecom)
  where TDocForm : class
{
  protected override bool IsReadOnly => base.IsReadOnly || ParentRecord<EcomDoc>() is not { DocStatus.IsEditable: true };

  protected override void SetParent(Parent parent) => parent.Bind<TDocForm>(l => l.DocId);

  protected override void SetQuery(Query query)
  {
    query.Select(EcomDocLines.Row);
    query.SortBy(r => r.SortOrder);
  }

  [RequireAction(EndpointAction.Edit)]
  public override Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default) =>
    base.Delete(request, cancellationToken);

  protected override void SetHooks(Hooks hooks)
  {
    base.SetHooks(hooks);
    hooks.AllowMultiDelete();
    hooks.ValidateDelete(ctx =>
    {
      if (ParentRecord<EcomDoc>() is not { DocStatus.IsEditable: true })
      {
        ctx.Refuse(EcomDocTexts.Of(L).NotEditable);
      }
    });
    hooks.AfterDelete(async (ctx, ct) =>
    {
      var parentDoc = ParentRecord<EcomDoc>();
      if (parentDoc != null)
      {
        await EcomDocTotals.ApplyAsync(db, parentDoc, ct);
        await db.EcomDoc.Where(d => d.Id == parentDoc.Id)
          .ExecuteUpdateAsync(s => s
            .SetProperty(d => d.Qty, parentDoc.Qty)
            .SetProperty(d => d.Amount, parentDoc.Amount), ct);
        db.Entry(parentDoc).State = EntityState.Unchanged;
      }
    });
  }
}

