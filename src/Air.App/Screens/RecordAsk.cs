namespace Platform.App.Screens;

public abstract class RecordAsk
{
  public abstract IReadOnlyList<FormElementDto> BuildElements();

  public virtual void ValidateConfiguration(Type ownerServiceType)
  {
  }

  public virtual Task<IReadOnlyList<FormElementDto>> BuildResolvedElementsAsync(
    Type ownerServiceType,
    Feature feature,
    PlatformDbContext db,
    PlatformAppService? app,
    IUserContext user,
    Localizer localizer,
    CancellationToken ct = default) =>
    Task.FromResult(BuildElements());

  public abstract object? Read(JsonElement? input);

  public virtual IReadOnlyList<MessageDto> Validate(object? input, ILocalizer localizer) => [];

  public virtual Task<IReadOnlyList<MessageDto>> ValidateAsync(
    object? input,
    PlatformDbContext db,
    PlatformAppService? app,
    Localizer localizer,
    CancellationToken ct = default) =>
    Task.FromResult(Validate(input, localizer));
}
