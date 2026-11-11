using Platform.App.Form.Internals;
using Platform.App.Internals;

namespace Platform.App.Form.Actions;

public sealed class TransitionAsk<TInput>(Action<Elements<TInput>> configure) : RecordAsk
  where TInput : class, new()
{
  private Elements<TInput> CreateElements()
  {
    var elements = new Elements<TInput>();
    configure(elements);
    FormConstraintExtractor.ApplyElementConstraints(typeof(TInput), efEntityType: null, elements.List);
    return elements;
  }

  public override IReadOnlyList<FormElementDto> BuildElements() => CreateElements().List;

  public override void ValidateConfiguration(Type ownerServiceType)
  {
    var elements = CreateElements();
    foreach (var el in elements.List)
    {
      if (el.ReferenceServiceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(ownerServiceType, el.ReferenceServiceType, $"Ask.AsReference on '{el.Path}'");
      }

      if (el.CreateFormServiceType != null)
      {
        ModuleCatalog.EnsureModuleServiceAccessAllowed(ownerServiceType, el.CreateFormServiceType, $"Ask.AllowCreate on '{el.Path}'");
      }
    }
  }

  public override async Task<IReadOnlyList<FormElementDto>> BuildResolvedElementsAsync(
    Type ownerServiceType,
    Feature feature,
    PlatformDbContext db,
    PlatformAppService? app,
    IUserContext user,
    Localizer localizer,
    CancellationToken ct = default)
  {
    var elements = CreateElements();
    await FormReferenceResolver.LocalizeAndResolveSelectElementsAsync<TInput>(
      ownerServiceType,
      feature,
      db,
      app,
      user,
      localizer,
      isReadOnly: false,
      elements,
      ct);
    return elements.List;
  }

  public override object? Read(JsonElement? input)
  {
    if (input is null || input.Value.ValueKind != JsonValueKind.Object)
    {
      return new TInput();
    }

    var el = input.Value;
    if (el.TryGetProperty("value", out var nestedValue) &&
        nestedValue.ValueKind == JsonValueKind.Object &&
        (typeof(FormRequest).IsAssignableFrom(typeof(TInput)) ||
         PropertyAccessorCache.GetProperty(typeof(TInput), "Value") == null))
    {
      return nestedValue.Deserialize<TInput>(PlatformJsonOptions.DefaultOptions) ?? new TInput();
    }

    return el.Deserialize<TInput>(PlatformJsonOptions.DefaultOptions) ?? new TInput();
  }

  public override IReadOnlyList<MessageDto> Validate(object? input, ILocalizer localizer)
  {
    var target = input as TInput ?? new TInput();
    var elements = CreateElements();
    var saveCtx = new FormSaveContext<TInput> { Entity = target, IsNew = true };
    var ruleCtx = new RuleContext<TInput> { SaveContext = saveCtx };
    var loc = localizer as Localizer ?? Localizer.Instance;
    FormValidator.ValidateElementConstraints(db: null, loc, elements.List, target, ruleCtx, onlyAcceptingInput: true);
    return ruleCtx.Failures;
  }

  public override async Task<IReadOnlyList<MessageDto>> ValidateAsync(
    object? input,
    PlatformDbContext db,
    PlatformAppService? app,
    Localizer localizer,
    CancellationToken ct = default)
  {
    var target = input as TInput ?? new TInput();
    var elements = CreateElements();
    var saveCtx = new FormSaveContext<TInput> { Entity = target, IsNew = true, Db = db };
    var ruleCtx = new RuleContext<TInput> { SaveContext = saveCtx };
    FormValidator.ValidateElementConstraints(db, localizer, elements.List, target, ruleCtx, onlyAcceptingInput: true);
    await FormValidator.ValidateReferenceElementsAsync(
      db,
      app,
      localizer,
      elements.List,
      target,
      isNew: true,
      originalValues: null,
      ruleCtx,
      ct);
    return ruleCtx.Failures;
  }
}
