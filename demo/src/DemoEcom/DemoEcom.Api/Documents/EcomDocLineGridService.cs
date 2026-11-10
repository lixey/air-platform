namespace DemoEcom.Documents;

public abstract class EcomDocLineGridService<TDocForm>(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocLine>(db, ecom)
  where TDocForm : class
{
  protected override bool IsReadOnly => base.IsReadOnly || ParentRecord<EcomDoc>() is not { DocStatus.IsEditable: true };

  protected override void SetParent(Parent parent) => parent.Bind<TDocForm>(l => l.DocId);

  protected override void SetQuery(Query query)
  {
    query.SortBy(r => r.SortOrder);
  }

  [RequireAction(EndpointAction.Edit)]
  public override Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default) =>
    base.Delete(request, cancellationToken);

  protected override void SetHooks(Hooks hooks)
  {
    base.SetHooks(hooks);
    hooks.AllowMultiDelete();
    hooks.BeforeDelete(ctx =>
    {
      if (ParentRecord<EcomDoc>() is not { DocStatus.IsEditable: true })
      {
        ctx.Refuse(EcomDocTexts.Of(L).NotEditable);
      }
    });
  }
}

