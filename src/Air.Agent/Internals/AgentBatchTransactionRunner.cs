using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace Platform.Agent.Internals;

internal static class AgentBatchTransactionRunner
{
  public static IReadOnlyList<Type> ComputeLeafContextTypes(IEnumerable<Type> candidateContextTypes)
  {
    var distinct = candidateContextTypes
      .Where(t => typeof(PlatformDbContext).IsAssignableFrom(t))
      .Distinct()
      .ToList();

    return distinct
      .Where(c => !distinct.Any(other => other != c && c.IsAssignableFrom(other)))
      .ToList();
  }

  public static AgentModuleScope SelectScopeForService(
    IReadOnlyList<AgentModuleScope> scopes,
    Type? serviceType,
    AgentModuleScope fallbackScope)
  {
    var targetType = AgentModuleScope.ResolveTargetContextType(serviceType);
    if (targetType == null)
    {
      return fallbackScope;
    }

    return scopes.FirstOrDefault(s => targetType.IsInstanceOfType(s.Db)) ?? fallbackScope;
  }

  public static async Task RunAtomicAsync(
    IServiceProvider rootServices,
    IReadOnlyList<Type> requiredContextTypes,
    PlatformDbContext fallbackDb,
    PlatformAppService fallbackApp,
    Func<IReadOnlyList<AgentModuleScope>, CancellationToken, Task> action,
    CancellationToken ct)
  {
    var leafTypes = ComputeLeafContextTypes(requiredContextTypes);
    var scopes = new List<AgentModuleScope>();

    try
    {
      if (leafTypes.Count == 0 || rootServices.GetService<DbScope>() == null && leafTypes.All(t => t.IsInstanceOfType(fallbackDb)))
      {
        scopes.Add(AgentModuleScope.OpenDefault(rootServices, fallbackDb, fallbackApp));
      }
      else
      {
        foreach (var leafType in leafTypes)
        {
          scopes.Add(AgentModuleScope.OpenForContextType(rootServices, leafType, fallbackDb, fallbackApp));
        }
      }

      var contexts = scopes.Select(s => s.Db).Distinct().ToList();
      if (contexts.Count == 1)
      {
        await contexts[0].RunMutationAsync(token => action(scopes, token), ct);
        return;
      }

      await ExecuteMultiContextMutationAsync(contexts, scopes, action, ct);
    }
    finally
    {
      for (var i = scopes.Count - 1; i >= 0; i--)
      {
        await scopes[i].DisposeAsync();
      }
    }
  }

  private static async Task ExecuteMultiContextMutationAsync(
    IReadOnlyList<PlatformDbContext> contexts,
    IReadOnlyList<AgentModuleScope> scopes,
    Func<IReadOnlyList<AgentModuleScope>, CancellationToken, Task> action,
    CancellationToken ct)
  {
    var runHandles = new List<IDisposable>(contexts.Count);
    IDbContextTransaction? primaryTx = null;
    var separateTransactions = new List<IDbContextTransaction>();
    var sharedSecondaries = new List<PlatformDbContext>();
    var primary = contexts[0];
    var openedPrimaryConnection = false;

    try
    {
      foreach (var ctx in contexts)
      {
        runHandles.Add(ctx.BeginMutationRun());
      }

      DbTransaction? sharedDbTx = null;
      DbConnection? primaryConn = null;
      string? primaryConnString = null;

      if (primary.Database.IsRelational())
      {
        primaryConn = primary.Database.GetDbConnection();
        primaryConnString = primary.Database.GetConnectionString();
        if (primaryConn.State != ConnectionState.Open)
        {
          await primary.Database.OpenConnectionAsync(ct);
          openedPrimaryConnection = true;
        }

        if (primary.Database.CurrentTransaction == null)
        {
          primaryTx = await primary.Database.BeginTransactionAsync(ct);
          sharedDbTx = primaryTx.GetDbTransaction();
        }
        else
        {
          sharedDbTx = primary.Database.CurrentTransaction.GetDbTransaction();
        }
      }

      for (var i = 1; i < contexts.Count; i++)
      {
        var secondary = contexts[i];
        if (!secondary.Database.IsRelational())
        {
          continue;
        }

        var secConn = secondary.Database.GetDbConnection();
        var secConnString = secondary.Database.GetConnectionString();
        var canShareConnection = primaryConn != null &&
          sharedDbTx != null &&
          (ReferenceEquals(secConn, primaryConn) ||
           !string.IsNullOrEmpty(primaryConnString) &&
           string.Equals(primaryConnString, secConnString, StringComparison.Ordinal) &&
           !primaryConnString.Contains(":memory:", StringComparison.OrdinalIgnoreCase));

        if (canShareConnection)
        {
          if (!ReferenceEquals(secConn, primaryConn))
          {
            if (secConn.State != ConnectionState.Closed)
            {
              await secondary.Database.CloseConnectionAsync();
            }

            secondary.Database.SetDbConnection(primaryConn, contextOwnsConnection: false);
          }

          await secondary.Database.UseTransactionAsync(sharedDbTx, ct);
          sharedSecondaries.Add(secondary);
        }
        else if (secondary.Database.CurrentTransaction == null)
        {
          separateTransactions.Add(await secondary.Database.BeginTransactionAsync(ct));
        }
      }

      await action(scopes, ct);

      if (contexts.Any(c => c.IsMutationRollbackOnly))
      {
        throw new InvalidOperationException(Localizer.Instance["MutationRefused"]);
      }

      foreach (var ctx in contexts)
      {
        await ctx.FlushMutationJournalAsync(ct);
      }

      foreach (var secTx in separateTransactions)
      {
        await secTx.CommitAsync(ct);
      }

      if (primaryTx != null)
      {
        await primaryTx.CommitAsync(ct);
      }
    }
    catch
    {
      foreach (var secTx in separateTransactions)
      {
        try
        {
          await secTx.RollbackAsync(CancellationToken.None);
        }
        catch { }
      }

      if (primaryTx != null)
      {
        try
        {
          await primaryTx.RollbackAsync(CancellationToken.None);
        }
        catch { }
      }

      foreach (var ctx in contexts)
      {
        ctx.ResetMutationState();
      }

      throw;
    }
    finally
    {
      foreach (var secondary in sharedSecondaries)
      {
        try
        {
          await secondary.Database.UseTransactionAsync(null, CancellationToken.None);
        }
        catch { }
      }

      foreach (var secTx in separateTransactions)
      {
        await secTx.DisposeAsync();
      }

      if (primaryTx != null)
      {
        await primaryTx.DisposeAsync();
      }

      if (openedPrimaryConnection)
      {
        try
        {
          await primary.Database.CloseConnectionAsync();
        }
        catch { }
      }

      for (var i = runHandles.Count - 1; i >= 0; i--)
      {
        runHandles[i].Dispose();
      }
    }
  }
}
