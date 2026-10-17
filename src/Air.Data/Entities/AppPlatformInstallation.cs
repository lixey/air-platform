using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Air.Data.Entities;

[Comment("Installation of a module's initial data"), Index(nameof(Module), IsUnique = true)]
public class AppPlatformInstallation : BaseEntity
{
  [Required, MaxLength(128), Comment("Handle of the module whose initial data is installed")]
  public string Module { get; set; } = string.Empty;

  [Comment("When the initial data was written")]
  public DateTimeOffset InstalledAt { get; set; }
}
