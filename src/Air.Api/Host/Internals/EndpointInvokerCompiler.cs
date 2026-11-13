namespace Platform.Api.Host.Internals;

internal static class EndpointInvokerCompiler
{
  private static readonly MethodInfo AwaitResultTaskMethod =
    typeof(EndpointInvokerCompiler).GetMethod(nameof(AwaitResultTaskAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo AwaitVoidTaskMethod =
    typeof(EndpointInvokerCompiler).GetMethod(nameof(AwaitVoidTaskAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo AwaitTypedTaskOpenMethod =
    typeof(EndpointInvokerCompiler).GetMethod(nameof(AwaitTypedTaskAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo WrapSyncResultMethod =
    typeof(EndpointInvokerCompiler).GetMethod(nameof(WrapSyncResult), BindingFlags.NonPublic | BindingFlags.Static)!;

  public static Func<object, object?[], Task<IResult>> Compile(Type targetType, MethodInfo method)
  {
    var targetParam = Expression.Parameter(typeof(object), "target");
    var argsParam = Expression.Parameter(typeof(object?[]), "args");

    var parameters = method.GetParameters();
    var callArgs = new Expression[parameters.Length];
    for (var i = 0; i < parameters.Length; i++)
    {
      var elemExpr = Expression.ArrayIndex(argsParam, Expression.Constant(i));
      callArgs[i] = ConvertArgument(elemExpr, parameters[i].ParameterType);
    }

    var instanceExpr = Expression.Convert(targetParam, method.DeclaringType ?? targetType);
    var callExpr = Expression.Call(instanceExpr, method, callArgs);
    var bodyExpr = WrapReturnExpression(callExpr, method.ReturnType);
    return Expression.Lambda<Func<object, object?[], Task<IResult>>>(bodyExpr, targetParam, argsParam).Compile();
  }

  private static Expression WrapReturnExpression(Expression callExpr, Type returnType)
  {
    if (returnType == typeof(Task<IResult>))
    {
      return Expression.Call(AwaitResultTaskMethod, callExpr);
    }

    if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
    {
      var innerType = returnType.GetGenericArguments()[0];
      return Expression.Call(AwaitTypedTaskOpenMethod.MakeGenericMethod(innerType), callExpr);
    }

    if (typeof(Task).IsAssignableFrom(returnType))
    {
      return Expression.Call(AwaitVoidTaskMethod, Expression.Convert(callExpr, typeof(Task)));
    }

    if (returnType == typeof(void))
    {
      return Expression.Block(
        callExpr,
        Expression.Call(WrapSyncResultMethod, Expression.Constant(null, typeof(object))));
    }

    return Expression.Call(WrapSyncResultMethod, Expression.Convert(callExpr, typeof(object)));
  }

  private static Expression ConvertArgument(Expression valueExpr, Type parameterType)
  {
    if (!parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) is not null)
    {
      return Expression.Convert(valueExpr, parameterType);
    }

    return Expression.Condition(
      Expression.Equal(valueExpr, Expression.Constant(null, typeof(object))),
      Expression.Default(parameterType),
      Expression.Unbox(valueExpr, parameterType));
  }

  private static Task<IResult> AwaitResultTaskAsync(Task<IResult> task) => task;

  private static async Task<IResult> AwaitVoidTaskAsync(Task task)
  {
    await task.ConfigureAwait(false);
    return PlatformEndpointsExtensions.ToMutationResult(
      new MutationResultDto { Outcome = MutationOutcome.Committed });
  }

  private static async Task<IResult> AwaitTypedTaskAsync<T>(Task<T> task)
  {
    var result = await task.ConfigureAwait(false);
    if (result is IResult httpResult)
    {
      return httpResult;
    }

    return PlatformEndpointsExtensions.ToMutationResult(
      (object?)result ?? new MutationResultDto { Outcome = MutationOutcome.Committed });
  }

  private static Task<IResult> WrapSyncResult(object? result) =>
    Task.FromResult(
      result is IResult httpResult
        ? httpResult
        : PlatformEndpointsExtensions.ToMutationResult(
          result ?? new MutationResultDto { Outcome = MutationOutcome.Committed }));
}
