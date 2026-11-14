namespace Platform.App.Form.Contracts;

public sealed class FormCompositeDraftRequest
{
  public long? RecordId { get; init; }
  public JsonElement? Args { get; init; }
  public JsonElement? Values { get; init; }
  public string MultiSelectMode { get; init; } = "replace";
  public int MaxPreloadDictionarySize { get; init; } = 50;
  public Func<Feature, Feature, bool>? ChildPermissionFilter { get; init; }
  public Func<FormElementDto, string, string?>? CreateFormResolver { get; init; }
}
