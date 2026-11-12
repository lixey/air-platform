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

  public static async Task<bool> ResolveIsUkrainianAsync(
    BaseAppUser user,
    HttpRequest request,
    IAuthStore authStore,
    CancellationToken ct)
  {
    var code = await authStore.ResolveLanguageCodeAsync(user.LanguageId, ct);
    if (!string.IsNullOrEmpty(code))
    {
      return code == "uk";
    }

    return IsUkrainian(request);
  }

  public static bool IsUkrainian(HttpRequest request) =>
    PlatformEndpointsExtensions.ResolveRequestCulture(request)
      .TwoLetterISOLanguageName.Equals("uk", StringComparison.OrdinalIgnoreCase);

  public static async Task<UserProfileDto> BuildUserProfileDtoAsync(
    BaseAppUser user,
    SecurityInfo securityInfo,
    bool isUk,
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
      Language = isUk ? "uk" : "en",
      TimeZone = !string.IsNullOrEmpty(resolvedTimeZone) ? resolvedTimeZone : "UTC",
      MustChangePassword = user.MustChangePassword,
      Permissions = securityInfo.Permissions.ToList()
    };
  }

  public static async Task<SessionDto> BuildSessionDtoAsync(
    BaseAppUser user,
    SecurityInfo securityInfo,
    bool isUk,
    long? tenantId,
    TimeSpan accessTokenLifetime,
    IAuthStore authStore,
    CancellationToken ct) =>
    new()
    {
      User = await BuildUserProfileDtoAsync(user, securityInfo, isUk, tenantId, authStore, ct),
      ExpiresIn = (int)Math.Max(1, accessTokenLifetime.TotalSeconds)
    };
}
