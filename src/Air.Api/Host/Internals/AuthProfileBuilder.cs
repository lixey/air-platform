namespace Platform.Api.Host.Internals;

internal static class AuthProfileBuilder
{
  public static LoginScreenDto BuildLoginScreenDto(CultureInfo culture) =>
    new()
    {
      Title = Localizer.Instance["SignIn"],
      Login = Localizer.Instance["Login"],
      Password = Localizer.Instance["Password"],
      Submit = Localizer.Instance["SignIn"],
      InvalidCredentials = Localizer.Instance["InvalidCredentials"],
      TooManyAttempts = Localizer.Instance["TooManyAttempts"],
      Unavailable = Localizer.Instance["Unavailable"],
      ChangePasswordTitle = Localizer.Instance["ChangePasswordTitle"],
      CurrentPassword = Localizer.Instance["PasswordCurrent"],
      NewPassword = Localizer.Instance["PasswordNew"],
      RepeatPassword = Localizer.Instance["PasswordRepeat"],
      ChangePasswordSubmit = Localizer.Instance["ChangePasswordSubmit"],
      PasswordsDiffer = Localizer.Instance["PasswordsDiffer"],
      SignOut = Localizer.Instance["SignOut"],
      Culture = culture.Name
    };

  public static async Task<PlatformCulture> ResolveUserCultureAsync(
    BaseAppUser user,
    HttpRequest request,
    IAuthStore authStore,
    ICultureCatalog cultures,
    CancellationToken ct)
  {
    var code = await authStore.ResolveLanguageCodeAsync(user.LanguageId, ct);
    return !string.IsNullOrWhiteSpace(code)
      ? cultures.Resolve(code)
      : cultures.ResolveRequest(request);
  }

  public static async Task<UserProfileDto> BuildUserProfileDtoAsync(
    BaseAppUser user,
    SecurityInfo securityInfo,
    PlatformCulture culture,
    long? tenantId,
    IAuthStore authStore,
    CancellationToken ct)
  {
    var effectiveTenantId = tenantId ?? (user as IHasTenant)?.TenantId ?? securityInfo.HomeTenantId;
    var resolvedTimeZone = await authStore.ResolveTimeZoneAsync(user.TimeZone, effectiveTenantId, ct);
    return new UserProfileDto
    {
      Id = user.Id,
      Login = user.Login,
      Name = user.Name ?? user.Login,
      Email = user.Email ?? "",
      Language = culture.ShortCode,
      TimeZone = !string.IsNullOrEmpty(resolvedTimeZone) ? resolvedTimeZone : "UTC",
      MustChangePassword = user.MustChangePassword,
      Permissions = securityInfo.Permissions.ToList()
    };
  }

  public static async Task<SessionDto> BuildSessionDtoAsync(
    BaseAppUser user,
    SecurityInfo securityInfo,
    PlatformCulture culture,
    long? tenantId,
    TimeSpan accessTokenLifetime,
    IAuthStore authStore,
    CancellationToken ct) =>
    new()
    {
      User = await BuildUserProfileDtoAsync(user, securityInfo, culture, tenantId, authStore, ct),
      ExpiresIn = (int)Math.Max(1, accessTokenLifetime.TotalSeconds)
    };
}
