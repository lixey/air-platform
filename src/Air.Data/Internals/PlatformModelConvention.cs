using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Platform.Data.Internals;

internal sealed class PlatformModelConvention(PlatformDbContext db) : IModelFinalizingConvention
{
  public void ProcessModelFinalizing(
    IConventionModelBuilder modelBuilder,
    IConventionContext<IConventionModelBuilder> context)
  {
    foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
    {
      if (entityType.IsOwned())
      {
        continue;
      }

      var clrType = entityType.ClrType;

      if (entityType.BaseType == null)
      {
        if (typeof(IHasTenant).IsAssignableFrom(clrType))
        {
          entityType.SetQueryFilter(TenantFilterExtensions.Name, BuildTenantFilter(clrType));
        }

        if (typeof(IInheritedByChildTenants).IsAssignableFrom(clrType))
        {
          var filter = clrType == typeof(AppAgentMemory)
            ? BuildAgentMemoryFilter()
            : BuildInheritedTenantFilter(clrType);
          entityType.SetQueryFilter(TenantFilterExtensions.Inherited, filter);
          entityType.FindProperty(nameof(IInheritedByChildTenants.TenantId))?.SetIsConcurrencyToken(true);
        }

        if (clrType == typeof(AppPlatformChange))
        {
          entityType.SetQueryFilter(TenantFilterExtensions.Name, BuildChangeJournalFilter());
        }
      }

      if (typeof(IHasVersion).IsAssignableFrom(clrType))
      {
        entityType.FindProperty(nameof(IHasVersion.Ver))?.SetIsConcurrencyToken(true);
      }

      foreach (var key in entityType.GetForeignKeys())
      {
        if (key.GetDeleteBehaviorConfigurationSource() == ConfigurationSource.Explicit)
        {
          continue;
        }

        var behavior =
          (typeof(BaseAppAuthAssignment).IsAssignableFrom(clrType) || typeof(BaseAppAuthSession).IsAssignableFrom(clrType)) &&
          key.Properties.Any(p => p.Name == nameof(BaseAppAuthAssignment.UserId))
            ? DeleteBehavior.Cascade
            : DeleteBehavior.Restrict;
        key.SetDeleteBehavior(behavior);
      }
    }
  }

  private LambdaExpression BuildTenantFilter(Type clrType)
  {
    var parameter = Expression.Parameter(clrType, "e");
    var property = Expression.Call(
      typeof(EF),
      nameof(EF.Property),
      [typeof(long)],
      parameter,
      Expression.Constant(nameof(IHasTenant.TenantId)));

    var tenantCtx = Expression.Property(Expression.Constant(db), nameof(PlatformDbContext.TenantContext));
    var isSystemScope = Expression.Property(tenantCtx, nameof(ITenantContext.IsSystemScope));
    var currentTenantId = Expression.Property(tenantCtx, nameof(ITenantContext.TenantId));

    var equality = Expression.Equal(Expression.Convert(property, typeof(long?)), currentTenantId);
    return Expression.Lambda(Expression.OrElse(isSystemScope, equality), parameter);
  }

  private LambdaExpression BuildInheritedTenantFilter(Type clrType)
  {
    var parameter = Expression.Parameter(clrType, "e");
    var property = Expression.Call(
      typeof(EF),
      nameof(EF.Property),
      [typeof(long?)],
      parameter,
      Expression.Constant(nameof(IInheritedByChildTenants.TenantId)));

    var tenantCtx = Expression.Property(Expression.Constant(db), nameof(PlatformDbContext.TenantContext));
    var isSystemScope = Expression.Property(tenantCtx, nameof(ITenantContext.IsSystemScope));
    var currentTenantId = Expression.Property(tenantCtx, nameof(ITenantContext.TenantId));

    var nullCheck = Expression.Equal(property, Expression.Constant(null, typeof(long?)));
    var equality = Expression.Equal(property, currentTenantId);
    return Expression.Lambda(Expression.OrElse(isSystemScope, Expression.OrElse(nullCheck, equality)), parameter);
  }

  private LambdaExpression BuildAgentMemoryFilter()
  {
    var parameter = Expression.Parameter(typeof(AppAgentMemory), "e");
    var tenantProp = Expression.Call(
      typeof(EF),
      nameof(EF.Property),
      [typeof(long?)],
      parameter,
      Expression.Constant(nameof(AppAgentMemory.TenantId)));
    var userProp = Expression.Call(
      typeof(EF),
      nameof(EF.Property),
      [typeof(long?)],
      parameter,
      Expression.Constant(nameof(AppAgentMemory.UserId)));

    var tenantCtx = Expression.Property(Expression.Constant(db), nameof(PlatformDbContext.TenantContext));
    var isSystemScope = Expression.Property(tenantCtx, nameof(ITenantContext.IsSystemScope));
    var currentTenantId = Expression.Property(tenantCtx, nameof(ITenantContext.TenantId));
    var currentUserId = Expression.Property(tenantCtx, nameof(ITenantContext.UserId));

    var nullLong = Expression.Constant(null, typeof(long?));
    var tenantAllowed = Expression.OrElse(
      Expression.Equal(tenantProp, nullLong),
      Expression.Equal(tenantProp, currentTenantId));
    var userAllowed = Expression.OrElse(
      Expression.Equal(userProp, nullLong),
      Expression.AndAlso(
        Expression.NotEqual(currentUserId, nullLong),
        Expression.Equal(userProp, currentUserId)));

    return Expression.Lambda(
      Expression.OrElse(isSystemScope, Expression.AndAlso(tenantAllowed, userAllowed)),
      parameter);
  }

  private LambdaExpression BuildChangeJournalFilter()
  {
    var parameter = Expression.Parameter(typeof(AppPlatformChange), "e");
    var property = Expression.Call(
      typeof(EF),
      nameof(EF.Property),
      [typeof(long?)],
      parameter,
      Expression.Constant(nameof(AppPlatformChange.TenantId)));

    var tenantCtx = Expression.Property(Expression.Constant(db), nameof(PlatformDbContext.TenantContext));
    var isSystemScope = Expression.Property(tenantCtx, nameof(ITenantContext.IsSystemScope));
    var currentTenantId = Expression.Property(tenantCtx, nameof(ITenantContext.TenantId));

    var hasTenant = Expression.NotEqual(currentTenantId, Expression.Constant(null, typeof(long?)));
    var equality = Expression.Equal(property, currentTenantId);
    return Expression.Lambda(Expression.OrElse(isSystemScope, Expression.AndAlso(hasTenant, equality)), parameter);
  }
}
