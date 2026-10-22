namespace DemoEcom.Documents;

public abstract class EcomDocFlowService(EcomDbContext db) : StatusFlowService<EcomDoc>(db)
{
  private const int NoteMaxLength = 2000;
  private IReadOnlyList<EcomDocStatus>? _statuses;

  protected abstract long Type { get; }
  protected EcomDocTexts Texts => field ??= EcomDocTexts.Of(L);

  protected override void SetFlow(Flow<EcomDoc> flow) => flow.State(doc => doc.DocStatusId);

  protected async Task PostAsync(Move<EcomDoc> move, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(move);
    var statuses = await StatusesAsync(cancellationToken);
    var from = statuses.Single(status => status.Id == move.From);
    var to = statuses.Single(status => status.Id == move.To);
    foreach (var refusal in await EcomDocStatuses.ChangeAsync(db, move.Record, from, to, move.At, cancellationToken))
    {
      move.Context.AddError(new MutationError(refusal.Code, Texts.Refusal(refusal, NameOf(to.Id)), RecordId: move.Record.Id));
    }
  }

  protected Task NoteAsync(Move<EcomDoc> move, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(move);
    var doc = move.Record;
    if (move.Note is null)
    {
      return Task.CompletedTask;
    }

    var text = move.Note.Trim();
    var note = string.IsNullOrWhiteSpace(doc.Note) ? text : $"{doc.Note}\n{text}";
    if (note.Length > NoteMaxLength)
    {
      move.Context.AddError(new MutationError(EcomDocActions.NoteFull, Texts.NoteFull, Path: EcomDocActions.ReasonPath, RecordId: doc.Id));
      return Task.CompletedTask;
    }

    doc.Note = note;
    return Task.CompletedTask;
  }

  protected override Task OnMovedAsync(Move<EcomDoc> move, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(move);
    db.Add(new EcomDocMove
    {
      TenantId = move.Record.TenantId,
      Doc = move.Record,
      Transition = move.Transition,
      FromId = move.From,
      ToId = move.To,
      UserId = move.UserId,
      At = move.At,
      Note = move.Note
    });
    return Task.CompletedTask;
  }

  protected override ValueTask<string> RefusalAsync(Transition<EcomDoc> transition, long status, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(transition);
    return new(Texts.Transition(NameOf(transition.To)));
  }

  protected override async Task LockAsync(IMutationContext context, IReadOnlyList<EcomDoc> records, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(records);
    var organizations = records.Select(doc => new[] { doc.OrganizationId });
    if (EcomMoney.Moves(Type))
    {
      await context.LockAsync<EcomMoneyBalance>(organizations, cancellationToken);
    }
    else
    {
      await context.LockAsync<EcomStockBalance>(organizations, cancellationToken);
    }

    await base.LockAsync(context, records, cancellationToken);
  }

  private async Task<IReadOnlyList<EcomDocStatus>> StatusesAsync(CancellationToken cancellationToken) =>
    _statuses ??= await EcomDocStatuses.OfTypeAsync(db, Type, cancellationToken);
}
