using System.ComponentModel.DataAnnotations;
using DemoApp.Data.Models;

namespace DemoOrg.Data.Models;

[Index(nameof(TenantId), nameof(OrganizationId), nameof(Name), IsUnique = true)]
public sealed class OrgWarehouse : AppBaseModel
{
  public long OrganizationId { get; set; }
  public OrgOrganization Organization { get; set; } = null!;

  [Required, MaxLength(128)]
  public string Name { get; set; } = string.Empty;

  [MaxLength(512)]
  public string? Address { get; set; }

  public bool IsActive { get; set; } = true;

  public int SortOrder { get; set; }
}
