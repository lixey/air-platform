namespace Platform.App.Form;

public class RuleBuilder<TEntity, TProp>(Func<FormSaveContext<TEntity>, TProp> getter, string? path = null)
{
  private sealed class ValidatorEntry
  {
    public required Func<TProp, RuleContext<TEntity>, ValidatorEntry, CancellationToken, Task> Execute { get; init; }
    public Func<TEntity, bool>? Condition { get; set; }
    public string? Message { get; set; }
    public string? Code { get; set; }
    public bool IsWarning { get; set; }
  }

  private void Emit(RuleContext<TEntity> ctx, ValidatorEntry entry, string defaultMessageKey)
  {
    var message = entry.Message != null ? ctx.Localizer[entry.Message] : ctx.Localizer[defaultMessageKey];
    if (entry.IsWarning)
    {
      ctx.Warn(message, path, entry.Code);
    }
    else
    {
      ctx.AddFailure(message, path, entry.Code);
    }
  }

  private readonly List<ValidatorEntry> _entries = [];

  public RuleBuilder<TEntity, TProp> Custom(Action<TProp, RuleContext<TEntity>> action)
  {
    _entries.Add(new ValidatorEntry
    {
      Execute = (val, ctx, _, _) =>
      {
        action(val, ctx);
        return Task.CompletedTask;
      }
    });
    return this;
  }

  public RuleBuilder<TEntity, TProp> CustomAsync(Func<TProp, RuleContext<TEntity>, CancellationToken, Task> func)
  {
    _entries.Add(new ValidatorEntry
    {
      Execute = (val, ctx, _, ct) => func(val, ctx, ct)
    });
    return this;
  }

  public RuleBuilder<TEntity, TProp> Must(Func<TProp, bool> predicate)
  {
    _entries.Add(new ValidatorEntry
    {
      Execute = (val, ctx, entry, _) =>
      {
        if (!predicate(val))
        {
          Emit(ctx, entry, "FormValidationInvalid");
        }

        return Task.CompletedTask;
      }
    });
    return this;
  }

  public RuleBuilder<TEntity, TProp> Must(Func<TEntity, TProp, bool> predicate)
  {
    _entries.Add(new ValidatorEntry
    {
      Execute = (val, ctx, entry, _) =>
      {
        if (!predicate(ctx.SaveContext.Entity, val))
        {
          Emit(ctx, entry, "FormValidationInvalid");
        }

        return Task.CompletedTask;
      }
    });
    return this;
  }

  public RuleBuilder<TEntity, TProp> MustAsync(Func<TProp, CancellationToken, Task<bool>> predicate)
  {
    _entries.Add(new ValidatorEntry
    {
      Execute = async (val, ctx, entry, ct) =>
      {
        if (!await predicate(val, ct))
        {
          Emit(ctx, entry, "FormValidationInvalid");
        }
      }
    });
    return this;
  }

  public RuleBuilder<TEntity, TProp> MustAsync(Func<TEntity, TProp, CancellationToken, Task<bool>> predicate)
  {
    _entries.Add(new ValidatorEntry
    {
      Execute = async (val, ctx, entry, ct) =>
      {
        if (!await predicate(ctx.SaveContext.Entity, val, ct))
        {
          Emit(ctx, entry, "FormValidationInvalid");
        }
      }
    });
    return this;
  }

  public RuleBuilder<TEntity, TProp> Matches(string pattern)
  {
    var regex = new Regex(pattern);
    _entries.Add(new ValidatorEntry
    {
      Execute = (val, ctx, entry, _) =>
      {
        if (val is string str && !string.IsNullOrEmpty(str) && !regex.IsMatch(str))
        {
          Emit(ctx, entry, "FormValidationPattern");
        }

        return Task.CompletedTask;
      }
    });
    return this;
  }

  public RuleBuilder<TEntity, TProp> WithMessage(string message)
  {
    if (_entries.Count > 0)
    {
      _entries[^1].Message = message;
    }

    return this;
  }

  public RuleBuilder<TEntity, TProp> WithErrorCode(string code)
  {
    if (_entries.Count > 0)
    {
      _entries[^1].Code = code;
    }

    return this;
  }

  public RuleBuilder<TEntity, TProp> AsWarning(bool isWarning = true)
  {
    if (_entries.Count > 0)
    {
      _entries[^1].IsWarning = isWarning;
    }

    return this;
  }

  public RuleBuilder<TEntity, TProp> When(Func<TEntity, bool> condition)
  {
    if (_entries.Count > 0)
    {
      _entries[^1].Condition = condition;
    }

    return this;
  }

  internal async Task ValidateAsync(FormSaveContext<TEntity> ctx, RuleContext<TEntity> ruleCtx, CancellationToken ct)
  {
    var prevPath = ruleCtx.CurrentPath;
    ruleCtx.CurrentPath = path;
    try
    {
      var val = getter(ctx);
      foreach (var entry in _entries)
      {
        if (entry.Condition != null && !entry.Condition(ctx.Entity))
        {
          continue;
        }

        await entry.Execute(val, ruleCtx, entry, ct);
      }
    }
    finally
    {
      ruleCtx.CurrentPath = prevPath;
    }
  }
}
