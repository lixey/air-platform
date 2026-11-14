namespace Platform.Data.Tests.Internals;

internal sealed class TestDataUser : BaseAppUser, IHasTenant
{
  public long TenantId { get; set; }
}
