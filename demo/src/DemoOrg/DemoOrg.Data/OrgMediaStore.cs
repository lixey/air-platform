using Air.Data.Files;

namespace DemoOrg.Data;

[FileStore(Key)]
public sealed class OrgMediaStore : FileStore
{
  public const string Key = "org_media";
}
