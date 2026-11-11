namespace Platform.App.Tests.Internals;

internal sealed class MockBadActionGridService(AppTestDbContext db, PlatformAppService? app = null)
  : GridService<TestTag>(db, app)
{
  public override Feature Feature => MockBadActionModule.Items;

  protected override void SetColumns(Columns columns) => columns.Add(x => x.Name);

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);
    nav.AddPicker<TestTagGridService>(UnattributedRun);
  }

  public Task<GridActionDto> UnattributedRun(GridPickRequest request, CancellationToken ct = default) =>
    Task.FromResult(new GridActionDto { Outcome = MutationOutcome.Committed });
}
