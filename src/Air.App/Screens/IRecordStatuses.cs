namespace Platform.App.Screens;

public interface IRecordStatuses
{
  LambdaExpression StatusMemberExpression { get; }
  IReadOnlyList<OptionDto> Statuses { get; }
  IReadOnlyList<RecordTransition> AllTransitions => [];
  IReadOnlyList<Type> Followers { get; }
  bool FollowsChildFlow(Type childFlowType) => false;
  PropertyInfo? FindParentKeyProperty(Type childEntityType) => null;
  void RegisterMetadata() { }
  Task FollowMovesAsync(IMutationContext context, IReadOnlySet<long> movedRecordIds, CancellationToken cancellationToken);
  Task<FlowDescriptionDto> DescribeFlowAsync(CancellationToken cancellationToken = default) =>
    Task.FromResult(new FlowDescriptionDto());

  Task<StatusMovePreviewDto> PreviewMoveAsync(
    object record,
    string transitionName,
    JsonElement? askInput = null,
    IReadOnlySet<string>? skipValidationPaths = null,
    CancellationToken cancellationToken = default) =>
    throw new InvalidOperationException("Status flow is not configured.");
}
