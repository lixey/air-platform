namespace Platform.Api.Host.Internals;

internal sealed record PlatformJobRegistration(Type JobType, TimeSpan Interval, TimeSpan? InitialDelay = null);
