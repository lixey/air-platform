namespace Platform.App.Form.Internals;

internal readonly record struct ElementResolveScope(
  Type OwnerServiceType,
  Feature Feature,
  PlatformDbContext Db,
  PlatformAppService? App,
  IUserContext User,
  Localizer Localizer,
  bool IsReadOnly);
