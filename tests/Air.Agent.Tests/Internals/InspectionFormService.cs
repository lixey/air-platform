namespace Platform.Agent.Tests.Internals;

internal sealed class InspectionFormService(AgentTestDbContext db, PlatformAppService app)
  : FormService<AgentTestInspection>(db, app)
{
  public override Feature Feature => AgentTestModule.Inspections;
  protected override void SetQuery() => Query.Where(i => i.Code != "OTHER-DOCTYPE");
  protected override void SetElements()
  {
    Elements.UseStatusOf(new InspectionFlowService(db));
    Elements.Add(i => i.Code).Required();
    Elements.Add(i => i.Status);
    Elements.Add(i => i.Notes);
    Elements.AddChild<InspectionCheckpointGridService>().Key("checkpoints");
    Elements.AddChild<InspectionMeasurementGridService>().Key("measurements");
    Elements.AddChild<ReadOnlyChildNoteGridService>().Key("auditNotes");
    Elements.AddChild<HiddenChildLogGridService>().Key("secretLogs");
  }

  protected override void SetActions()
  {
    Actions.Add("CreateFollowUpReceipt")
      .AllowEdit()
      .AllowAgent()
      .When(i => i.Status == 2, "Only approved inspections can generate a receipt.")
      .Ask<TestInspectionCloneInput>(els =>
      {
        els.Add(x => x.CloneSuffix).Required();
        els.Add(x => x.CategoryId).AsReference<CategoryGridService>();
      })
      .Default((insp, _) => Task.FromResult(new TestInspectionCloneInput { CloneSuffix = $"FROM-{insp?.Code}" }))
      .Do(async (ctx, insp, input, ct) =>
      {
        var receipt = new AgentTestReceipt
        {
          Title = $"{insp.Code}:{input.CloneSuffix}",
          Amount = 250m,
          CategoryId = input.CategoryId
        };
        db.Receipts.Add(receipt);
        await db.SaveChangesAsync(ct);
        ctx.Open<ReceiptFormService>(receipt.Id);
      });

    Actions.Add("UiOnlyPing")
      .AllowEdit()
      .Do((_, _, _) => Task.CompletedTask);
  }
}
