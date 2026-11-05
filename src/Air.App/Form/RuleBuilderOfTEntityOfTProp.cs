namespace Platform.App.Form;

public class RuleBuilder<TEntity, TProp>(Func<FormSaveContext<TEntity>, TProp> getter, string? path = null)
{
  private sealed class ValidatorEntry
  {
    public required Func<TProp, RuleContext<TEntity>, ValidatorEntry, CancellationToken, Task> Execute { get; init; }
    public Func<TEntity, bool>? Condition { get; set; }
    public string? Message { get; set; }
    public string? Code { get; set; }
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
          ctx.AddFailure(entry.Message ?? "Invalid value.", path, entry.Code);
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
          ctx.AddFailure(entry.Message ?? "Invalid value.", path, entry.Code);
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
          ctx.AddFailure(entry.Message ?? "Invalid value.", path, entry.Code);
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
          ctx.AddFailure(entry.Message ?? "Invalid value.", path, entry.Code);
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
          ctx.AddFailure(entry.Message ?? "Value does not match required pattern.", path, entry.Code);
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
