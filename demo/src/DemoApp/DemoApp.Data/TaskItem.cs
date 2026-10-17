using Air.Data.Entities;

namespace DemoApp.Data;

public class TaskItem : BaseEntity, IHasTenant, IHasTimestamps, IHasVersion
{
  public long TenantId { get; set; }
  public long ProjectId { get; set; }
  public Project? Project { get; set; }
  public string Title { get; set; } = string.Empty;
  public string? Description { get; set; }
  public string Priority { get; set; } = "Medium";
  public string Status { get; set; } = "Todo";
  public int? EstimateHours { get; set; }
  public bool IsCompleted { get; set; }
  public DateTime? DueDate { get; set; }
}
