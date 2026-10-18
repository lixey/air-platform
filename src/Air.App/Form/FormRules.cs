using System.Text.RegularExpressions;
using Air.Data.Contracts;

namespace Air.App.Form;

public class RuleContext<TInput>
{
  public required object? Target { get; init; }
  public required FormSaveContext<object, TInput> SaveContext { get; init; }
  public List<MessageDto> Failures { get; } = [];
  internal string? CurrentPath { get; set; }

  public void AddFailure(string message, string? path = null) =>
    Failures.Add(new MessageDto
    {
      Detail = message,
      Path = path ?? CurrentPath,
      Type = MessageType.Error
    });
}

public class RuleBuilder<TInput, TProp>(Func<FormSaveContext<object, TInput>, TProp> getter, string? path = null)
{
  private readonly List<Func<TProp, RuleContext<TInput>, CancellationToken, Task>> _validators = [];
  private string? _lastErrorMessage;
  private string? _lastErrorCode;

  public RuleBuilder<TInput, TProp> Custom(Action<TProp, RuleContext<TInput>> action)
  {
    _validators.Add((val, ctx, ct) => { action(val, ctx); return Task.CompletedTask; });
    return this;
  }

  public RuleBuilder<TInput, TProp> CustomAsync(Func<TProp, RuleContext<TInput>, CancellationToken, Task> func)
  {
    _validators.Add(func);
    return this;
  }

  public RuleBuilder<TInput, TProp> Must(Func<TProp, bool> predicate)
  {
    _validators.Add((val, ctx, ct) =>
    {
      if (!predicate(val))
      {
        ctx.AddFailure(_lastErrorMessage ?? "Invalid value.", path);
      }
      return Task.CompletedTask;
    });
    return this;
  }

  public RuleBuilder<TInput, TProp> Must(Func<TInput, TProp, bool> predicate)
  {
    _validators.Add((val, ctx, ct) =>
    {
      if (!predicate(ctx.SaveContext.Input, val))
      {
        ctx.AddFailure(_lastErrorMessage ?? "Invalid value.", path);
      }
      return Task.CompletedTask;
    });
    return this;
  }

  public RuleBuilder<TInput, TProp> MustAsync(Func<TProp, CancellationToken, Task<bool>> predicate)
  {
    _validators.Add(async (val, ctx, ct) =>
    {
      if (!await predicate(val, ct))
      {
        ctx.AddFailure(_lastErrorMessage ?? "Invalid value.", path);
      }
    });
    return this;
  }

  public RuleBuilder<TInput, TProp> MustAsync(Func<TInput, TProp, CancellationToken, Task<bool>> predicate)
  {
    _validators.Add(async (val, ctx, ct) =>
    {
      if (!await predicate(ctx.SaveContext.Input, val, ct))
      {
        ctx.AddFailure(_lastErrorMessage ?? "Invalid value.", path);
      }
    });
    return this;
  }

  public RuleBuilder<TInput, TProp> Matches(string pattern)
  {
    var regex = new Regex(pattern);
    _validators.Add((val, ctx, ct) =>
    {
      if (val is string str && !string.IsNullOrEmpty(str) && !regex.IsMatch(str))
      {
        ctx.AddFailure(_lastErrorMessage ?? "Value does not match required pattern.", path);
      }
      return Task.CompletedTask;
    });
    return this;
  }

  public RuleBuilder<TInput, TProp> WithMessage(string message)
  {
    _lastErrorMessage = message;
    return this;
  }

  public RuleBuilder<TInput, TProp> WithErrorCode(string code)
  {
    _lastErrorCode = code;
    return this;
  }

  internal async Task ValidateAsync(FormSaveContext<object, TInput> ctx, RuleContext<TInput> ruleCtx, CancellationToken ct)
  {
    var prevPath = ruleCtx.CurrentPath;
    ruleCtx.CurrentPath = path;
    try
    {
      var val = getter(ctx);
      foreach (var v in _validators)
      {
        await v(val, ruleCtx, ct);
      }
    }
    finally
    {
      ruleCtx.CurrentPath = prevPath;
    }
  }
}
