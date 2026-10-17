using Air.Data.Entities;

namespace DemoApp.Data;

public class Project : BaseEntity, IHasTenant, IHasTimestamps, IHasVersion
{
  public long TenantId { get; set; }
  public string Title { get; set; } = string.Empty;
  public string? Description { get; set; }
  public string Status { get; set; } = "Active";
  public decimal? Budget { get; set; }
  public DateTime? DueDate { get; set; }

  public List<TaskItem> Tasks { get; set; } = [];
}
