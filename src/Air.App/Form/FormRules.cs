using System.Text.RegularExpressions;

namespace Air.App.Form;

public class RuleContext<TInput>
{
  public required object? Target { get; init; }
  public required FormSaveContext<object, TInput> SaveContext { get; init; }
  public List<MessageDto> Failures { get; } = [];
  internal string? CurrentPath { get; set; }

  public void AddFailure(string message, string? path = null, string? code = null) =>
    Failures.Add(new MessageDto
    {
      Detail = message,
      Path = path ?? CurrentPath,
      Code = code,
      Type = MessageType.Error
    });
}

public class RuleBuilder<TInput, TProp>(Func<FormSaveContext<object, TInput>, TProp> getter, string? path = null)
{
  private sealed class ValidatorEntry
  {
    public required Func<TProp, RuleContext<TInput>, ValidatorEntry, CancellationToken, Task> Execute { get; init; }
    public string? Message { get; set; }
    public string? Code { get; set; }
  }

  private readonly List<ValidatorEntry> _entries = [];

  public RuleBuilder<TInput, TProp> Custom(Action<TProp, RuleContext<TInput>> action)
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

  public RuleBuilder<TInput, TProp> CustomAsync(Func<TProp, RuleContext<TInput>, CancellationToken, Task> func)
  {
    _entries.Add(new ValidatorEntry
    {
      Execute = (val, ctx, _, ct) => func(val, ctx, ct)
    });
    return this;
  }

  public RuleBuilder<TInput, TProp> Must(Func<TProp, bool> predicate)
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

  public RuleBuilder<TInput, TProp> Must(Func<TInput, TProp, bool> predicate)
  {
    _entries.Add(new ValidatorEntry
    {
      Execute = (val, ctx, entry, _) =>
      {
        if (!predicate(ctx.SaveContext.Input, val))
        {
          ctx.AddFailure(entry.Message ?? "Invalid value.", path, entry.Code);
        }

        return Task.CompletedTask;
      }
    });
    return this;
  }

  public RuleBuilder<TInput, TProp> MustAsync(Func<TProp, CancellationToken, Task<bool>> predicate)
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

  public RuleBuilder<TInput, TProp> MustAsync(Func<TInput, TProp, CancellationToken, Task<bool>> predicate)
  {
    _entries.Add(new ValidatorEntry
    {
      Execute = async (val, ctx, entry, ct) =>
      {
        if (!await predicate(ctx.SaveContext.Input, val, ct))
        {
          ctx.AddFailure(entry.Message ?? "Invalid value.", path, entry.Code);
        }
      }
    });
    return this;
  }

  public RuleBuilder<TInput, TProp> Matches(string pattern)
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

  public RuleBuilder<TInput, TProp> WithMessage(string message)
  {
    if (_entries.Count > 0)
    {
      _entries[^1].Message = message;
    }

    return this;
  }

  public RuleBuilder<TInput, TProp> WithErrorCode(string code)
  {
    if (_entries.Count > 0)
    {
      _entries[^1].Code = code;
    }

    return this;
  }

  internal async Task ValidateAsync(FormSaveContext<object, TInput> ctx, RuleContext<TInput> ruleCtx, CancellationToken ct)
  {
    var prevPath = ruleCtx.CurrentPath;
    ruleCtx.CurrentPath = path;
    try
    {
      var val = getter(ctx);
      foreach (var entry in _entries)
      {
        await entry.Execute(val, ruleCtx, entry, ct);
      }
    }
    finally
    {
      ruleCtx.CurrentPath = prevPath;
    }
  }
}
