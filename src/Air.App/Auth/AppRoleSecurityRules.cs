namespace Platform.App.Auth;

/// <summary>
/// Shared RBAC coverage, administrator-role retention, and grant-synchronization rules and hooks
/// for application <see cref="BaseAppAuthRole"/> services.
/// </summary>
public static class AppRoleSecurityRules
{
  public static void AddNameRules<TRole>(
    FormService<TRole> service,
    FormService<TRole>.Rules rules)
    where TRole : BaseAppAuthRole, new() =>
    AddNameRules(rules, service.DbContext);

  public static void AddFormHooks<TRole, TGrant>(
    FormService<TRole> service,
    FormService<TRole>.Hooks hooks)
    where TRole : BaseAppAuthRole, new()
    where TGrant : BaseAppAuthGrant, new() =>
    AddFormHooks<TRole, TGrant>(hooks, service.DbContext, service.PlatformApp, () => service.User, () => service.Catalog);

  public static void AddDeleteHooks<TRole, TGrant, TAssignment>(
    GridService<TRole, TRole> service,
    GridService<TRole, TRole>.Hooks hooks)
    where TRole : BaseAppAuthRole, new()
    where TGrant : BaseAppAuthGrant
    where TAssignment : BaseAppAuthAssignment =>
    AddDeleteHooks<TRole, TGrant, TAssignment>(hooks, service.DbContext, service.PlatformApp, () => service.User);

  public static void AddNameRules<TRole>(
    FormService<TRole>.Rules rules,
    PlatformDbContext db)
    where TRole : BaseAppAuthRole, new()
  {
    var l = Localizer.Instance;
    rules.For(e => e.Name).CustomAsync(async (name, c, ct) =>
    {
      if (string.IsNullOrWhiteSpace(name))
      {
        return;
      }

      var id = c.Entity.Id;
      var hasMultiTenantDirectory = db.SetOfBase<BaseAppTenant>() != null;
      var tenant = hasMultiTenantDirectory
        ? c.Entity.TenantId
        : c.Entity.TenantId ?? db.TenantContext.TenantId;
      if (await db.Set<TRole>().AnyAsync(r => r.Id != id && r.TenantId == tenant && r.Name == name, ct))
      {
        c.AddFailure(l["RoleNameAlreadyInUse"]);
      }
    });
  }

  public static void AddFormHooks<TRole, TGrant>(
    FormService<TRole>.Hooks hooks,
    PlatformDbContext db,
    PlatformAppService? app,
    Func<IUserContext?> userProvider,
    Func<ModuleCatalog> catalogProvider)
    where TRole : BaseAppAuthRole, new()
    where TGrant : BaseAppAuthGrant, new()
  {
    var l = Localizer.Instance;

    IReadOnlySet<string> ShownKeys() =>
      userProvider() is { IsAuthenticated: true } user
        ? RightsTree.FlattenKeys(RightsTree.Build(catalogProvider(), user.Permissions))
        : new HashSet<string>(StringComparer.Ordinal);

    IQueryable<TGrant> Grants(long roleId) => db.Set<TGrant>().Where(g => g.AuthRoleId == roleId);

    async Task<bool> IsLastActiveAdminRoleAsync(long roleId, long? tenantId, CancellationToken ct)
    {
      if (!await Grants(roleId).AnyAsync(g => g.Permission == "*", ct))
      {
        return false;
      }

      return !await db.Set<TRole>().AnyAsync(
        r => r.Id != roleId && r.TenantId == tenantId && r.IsActive &&
             db.Set<TGrant>().Any(g => g.AuthRoleId == r.Id && g.Permission == "*"),
        ct);
    }

    hooks.AfterLoad(async (c, ct) =>
    {
      if (c.IsNew)
      {
        c.Entity.TenantId = db.TenantContext.TenantId;
        return;
      }

      var shown = ShownKeys();
      var granted = await Grants(c.Entity.Id).Select(g => g.Permission).ToListAsync(ct);
      c.Entity.Permissions = [.. granted.Where(shown.Contains).Order(StringComparer.Ordinal)];
    });

    hooks.Compute(c =>
    {
      if (db.SetOfBase<BaseAppTenant>() == null)
      {
        c.Entity.TenantId ??= db.TenantContext.TenantId;
      }
    });

    hooks.BeforeSave(async (c, ct) =>
    {
      var user = userProvider();
      if (!c.IsNew && (app == null || !await PlatformAppService.CoversRoleGrantsAsync(db, app, user, c.Entity.Id, ct)))
      {
        c.Refuse(l["RoleOutOfPermissionScope"]);
        return;
      }

      var requested = (c.Entity.Permissions ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToHashSet(StringComparer.Ordinal);
      if (app == null || !app.Covers(requested))
      {
        c.Refuse(l["RoleOutOfPermissionScope"]);
        return;
      }

      if (c.IsNew)
      {
        return;
      }

      var shown = ShownKeys();
      var wanted = requested.Where(shown.Contains).ToHashSet(StringComparer.Ordinal);
      var losesAdmin = !c.Entity.IsActive || shown.Contains("*") && !wanted.Contains("*");
      if (losesAdmin && await IsLastActiveAdminRoleAsync(c.Entity.Id, c.Entity.TenantId, ct))
      {
        c.Refuse(l["LastAdminRoleCannotLoseAdmin"]);
      }
    });

    hooks.AfterSave(async (c, ct) =>
    {
      if (app == null)
      {
        return;
      }

      var shown = ShownKeys();
      var wanted = (c.Entity.Permissions ?? []).Where(shown.Contains).Where(p => app.Covers([p])).ToHashSet(StringComparer.Ordinal);
      var grants = await Grants(c.Entity.Id).ToListAsync(ct);
      db.RemoveRange(grants.Where(g => shown.Contains(g.Permission) && !wanted.Contains(g.Permission)));
      db.AddRange(wanted.Except(grants.Select(g => g.Permission)).Select(p => new TGrant { AuthRoleId = c.Entity.Id, Permission = p }));
    });
  }

  public static void AddDeleteHooks<TRole, TGrant, TAssignment>(
    GridService<TRole, TRole>.Hooks hooks,
    PlatformDbContext db,
    PlatformAppService? app,
    Func<IUserContext?> userProvider)
    where TRole : BaseAppAuthRole, new()
    where TGrant : BaseAppAuthGrant
    where TAssignment : BaseAppAuthAssignment
  {
    var l = Localizer.Instance;

    hooks.BeforeDelete(async (c, ct) =>
    {
      var user = userProvider();
      var roleIds = c.Records.Select(r => r.Id).ToList();
      foreach (var role in c.Records)
      {
        if (app == null || !await PlatformAppService.CoversRoleGrantsAsync(db, app, user, role.Id, ct))
        {
          c.Refuse(l["RoleOutOfPermissionScope"], role.Id);
          return;
        }
      }

      var adminRoleIds = (await db.Set<TGrant>()
        .Where(g => roleIds.Contains(g.AuthRoleId) && g.Permission == "*")
        .Select(g => g.AuthRoleId)
        .Distinct()
        .ToListAsync(ct))
        .ToHashSet();

      var hasMultiTenantDirectory = db.SetOfBase<BaseAppTenant>() != null;
      foreach (var role in c.Records.Where(r => adminRoleIds.Contains(r.Id)))
      {
        var tenantId = hasMultiTenantDirectory
          ? role.TenantId
          : role.TenantId ?? db.TenantContext.TenantId;
        var hasRemainingActiveAdminRole = await db.Set<TRole>().AnyAsync(
          other => !roleIds.Contains(other.Id) && other.TenantId == tenantId && other.IsActive &&
                   db.Set<TGrant>().Any(g => g.AuthRoleId == other.Id && g.Permission == "*"),
          ct);
        if (!hasRemainingActiveAdminRole)
        {
          c.Refuse(l["LastAdminRoleCannotBeDeleted"], role.Id);
          return;
        }
      }

      db.RemoveRange(await db.Set<TAssignment>().Where(a => roleIds.Contains(a.AuthRoleId)).ToListAsync(ct));
      db.RemoveRange(await db.Set<TGrant>().Where(g => roleIds.Contains(g.AuthRoleId)).ToListAsync(ct));
    });
  }
}
