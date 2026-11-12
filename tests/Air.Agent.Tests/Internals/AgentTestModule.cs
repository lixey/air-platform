namespace Platform.Agent.Tests.Internals;

[Module("AgentTest")]
internal sealed class AgentTestModule : ModuleDefinition<AgentTestModule>
{
  public static readonly Feature Categories = Feature<CategoryGridService>().AllowAgent();
  public static readonly Feature ReadOnlyCategories = Feature<ReadOnlyAgentCategoryGridService>().AllowAgent(AgentAccess.Read);
  public static readonly Feature Receipts = Feature<ReceiptGridService>().AllowAgent();
  public static readonly Feature Inspections = Feature<InspectionGridService>().AllowAgent();
  public static readonly Feature ReadOnlyChildNotes = Feature<ReadOnlyChildNoteGridService>().AllowAgent(AgentAccess.Read);
  public static readonly Feature LeafOneItems = Feature<LeafOneItemGridService>().AllowAgent();
  public static readonly Feature LeafTwoOrders = Feature<LeafTwoOrderGridService>().AllowAgent();
  public static readonly Feature PublicReport = Feature<PublicReportService>().ReadOnly().AllowAgent(AgentAccess.Read);
  public static readonly Feature SecretReport = Feature<SecretReportService>().ReadOnly();
  public static readonly Feature Secrets = Feature<SecretGridService>();
}
