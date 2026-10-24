namespace Platform.Api.Auth.Contracts;

[UiContract]
public sealed record SwitchRequest
{
  public long Id { get; set; }
}
