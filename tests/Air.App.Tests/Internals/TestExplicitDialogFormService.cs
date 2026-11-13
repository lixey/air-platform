namespace Platform.App.Tests.Internals;

internal sealed class TestExplicitDialogFormService(AppTestDbContext db, PlatformAppService? app = null)
  : FormService<TestTag>(db, app)
{
  public override Feature Feature => TestAppModule.Tags;

  protected override OverlayBuilder? Presentation(string handle) =>
    OverlayModel.Dialog(handle).Width(480);

  protected override void SetElements() =>
    Elements.Add(t => t.Name).Required();
}
