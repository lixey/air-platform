namespace Platform.Agent.Tests.Internals;

internal sealed class LeafOneItemFormService(TestLeafOneDbContext db, PlatformAppService app)
  : FormService<TestLeafOneItem>(db, app)
{
  public override Feature Feature => AgentTestModule.LeafOneItems;
  protected override void SetElements()
  {
    Elements.Add(x => x.Code).Required();
    Elements.Add(x => x.CategoryId).AsReference<CategoryGridService>();
  }
}
