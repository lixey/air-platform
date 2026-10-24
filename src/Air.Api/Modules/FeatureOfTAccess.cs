namespace Platform.Api.Modules;

public sealed class Feature<TAccess> : Feature where TAccess : FeatureAccess, new()
{
  internal Feature(Feature source) : base(source)
  {
    AccessTo = new TAccess { Feature = this };
  }

  public TAccess AccessTo { get; }

  public new Feature<TAccess> ReadOnly(bool isReadOnly = true)
  {
    base.ReadOnly(isReadOnly);
    return this;
  }

  public new Feature<TAccess> WithoutDelete()
  {
    base.WithoutDelete();
    return this;
  }

  public new Feature<TAccess> AllowAgent(bool allow = true)
  {
    base.AllowAgent(allow);
    return this;
  }

  public new Feature<TAccess> WithTitle(string title)
  {
    base.WithTitle(title);
    return this;
  }

  public new Feature<TAccess> WithTitle(string legacyUi, string legacyDb)
  {
    base.WithTitle(legacyUi, legacyDb);
    return this;
  }
}
