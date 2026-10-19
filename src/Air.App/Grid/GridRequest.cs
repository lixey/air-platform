using Air.Data.Contracts;

namespace Air.App.Grid;

[ClientOptional]
[UiContract]
public class GridRequest : ListRequest
{
  public bool IsDataOnly { get; set; }
  public long? Id { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ParentService { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? ParentId { get; set; }

  public List<long>? SelectedIds { get; set; }
  public List<int>? SelectedVersions { get; set; }
  public List<string>? ReferenceLabelPaths { get; set; }
  public List<string>? PatchSourceColumns { get; set; }
  public bool IsReferencePicker { get; set; }
  public bool IncludesInactive { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<string>? NamedFilters { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, JsonElement>? FormDraft { get; set; }
}
