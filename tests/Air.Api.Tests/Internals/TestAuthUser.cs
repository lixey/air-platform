namespace Platform.Api.Tests.Internals;

internal sealed class TestAuthUser : BaseAppUser, IHasTenant
{
  public long TenantId { get; set; }
}
