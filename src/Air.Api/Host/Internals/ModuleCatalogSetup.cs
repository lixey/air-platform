namespace Platform.Api.Host.Internals;

internal sealed record ModuleCatalogSetup(Action<ModuleCatalog> Configure, string? Key = null);
