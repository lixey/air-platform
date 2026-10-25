using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Platform.Data.Internals;

internal sealed class ModuleOwnershipConvention(Type contextType) : IModelFinalizingConvention
{
  public void ProcessModelFinalizing(
    IConventionModelBuilder modelBuilder,
    IConventionContext<IConventionModelBuilder> context)
  {
    var currentModule = ModuleOwnershipExtensions.GetDeclaredModuleHandle(contextType);
    if (string.IsNullOrWhiteSpace(currentModule))
    {
      return;
    }

    var model = modelBuilder.Metadata;
    var owners = ModuleOwnershipExtensions.InferTableOwners(model, contextType);
    if (owners.Count == 0)
    {
      return;
    }

    foreach (var entity in model.GetEntityTypes())
    {
      if (entity.GetTableName() is not { } table)
      {
        continue;
      }

      var key = (entity.GetSchema(), table);
      if (owners.TryGetValue(key, out var owner))
      {
        entity.SetIsTableExcludedFromMigrations(!string.Equals(owner, currentModule, StringComparison.Ordinal));
      }
    }
  }
}
