namespace Platform.App.Grid;

public sealed record GridCreateDescriptor(Type FormServiceType, string ServicePath, string Label, string? Color = null);
