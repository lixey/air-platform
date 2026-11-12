namespace DemoApp.Role;

public sealed class AppRoleFormService(AppDbContext db, AppService app)
  : AppFormService<AppAuthRole>(db, app)
{
  public override Feature Feature => App.Role;

  protected override async Task SetElementsAsync(Elements elements, CancellationToken cancellationToken)
  {
    elements.Add(e => e.Name).Focus();
    elements.Add(e => e.TenantId).AsReference<AppTenantGridService>();
    elements.Add(e => e.SortOrder);
    elements.Add(e => e.IsActive).AsCheckbox();
    elements.Add(e => e.Description).AsTextarea();

    elements.AddTab("Rights");
    elements.Add(e => e.Permissions).InputOnly().AsRightsTree();

    elements.AddChild<AppRoleUserGridService>().Key("users").Label("Users");
  }

  protected override void SetRules(Rules rules)
  {
    var taken = L["A role of this name already exists in its tenant."];
    rules.For(e => e.Name).CustomAsync(async (name, c, cancellationToken) =>
    {
      var id = c.Entity.Id;
      var tenant = c.Entity.TenantId;
      if (!string.IsNullOrWhiteSpace(name) &&
          await db.AppAuthRole.AnyAsync(r => r.Id != id && r.TenantId == tenant && r.Name == name, cancellationToken))
      {
        c.AddFailure(taken);
      }
    });
  }

  protected override void SetHooks(Hooks hooks)
  {
    hooks.AfterLoad(async (c, cancellationToken) =>
    {
      if (c.IsNew)
      {
        c.Entity.TenantId = db.TenantContext.TenantId;
        return;
      }

      var shown = ShownKeys();
      var granted = await Grants(c.Entity.Id).Select(g => g.Permission).ToListAsync(cancellationToken);
      c.Entity.Permissions = [.. granted.Where(shown.Contains).Order(StringComparer.Ordinal)];
    });

    hooks.BeforeSave(async (c, cancellationToken) =>
    {
      if (!c.IsNew && (app == null || !await PlatformAppService.CoversRoleGrantsAsync(db, app, User, c.Entity.Id, cancellationToken)))
      {
        c.Refuse(L["You cannot modify a role with permissions you do not hold."]);
        return;
      }

      var requested = (c.Entity.Permissions ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToHashSet(StringComparer.Ordinal);
      if (app == null || !app.Covers(requested))
      {
        c.Refuse(L["You cannot modify a role with permissions you do not hold."]);
        return;
      }

      if (c.IsNew)
      {
        return;
      }

      var shown = ShownKeys();
      var wanted = requested.Where(shown.Contains).ToHashSet(StringComparer.Ordinal);
      var losesAdmin = !c.Entity.IsActive || shown.Contains("*") && !wanted.Contains("*");
      if (losesAdmin && await IsLastActiveAdminRoleAsync(c.Entity.Id, c.Entity.TenantId, cancellationToken))
      {
        c.Refuse(L["The last administrator role cannot be deactivated or stripped of administrator rights."]);
      }
    });

    hooks.AfterSave(async (c, cancellationToken) =>
    {
      if (app == null)
      {
        return;
      }

      var shown = ShownKeys();
      var wanted = (c.Entity.Permissions ?? []).Where(shown.Contains).Where(p => app.Covers([p])).ToHashSet(StringComparer.Ordinal);
      var grants = await Grants(c.Entity.Id).ToListAsync(cancellationToken);
      db.RemoveRange(grants.Where(g => shown.Contains(g.Permission) && !wanted.Contains(g.Permission)));
      db.AddRange(wanted.Except(grants.Select(g => g.Permission)).Select(p => new AppAuthGrant { AuthRoleId = c.Entity.Id, Permission = p }));
    });
  }

  private IReadOnlySet<string> ShownKeys() =>
    User is { IsAuthenticated: true }
      ? RightsTree.FlattenKeys(RightsTree.Build(ModuleCatalog.Instance, User.Permissions))
      : new HashSet<string>(StringComparer.Ordinal);

  private async Task<bool> IsLastActiveAdminRoleAsync(long roleId, long? tenantId, CancellationToken cancellationToken)
  {
    if (!await Grants(roleId).AnyAsync(g => g.Permission == "*", cancellationToken))
    {
      return false;
    }

    return !await db.AppAuthRole.AnyAsync(
      r => r.Id != roleId && r.TenantId == tenantId && r.IsActive &&
           db.AppAuthGrant.Any(g => g.AuthRoleId == r.Id && g.Permission == "*"),
      cancellationToken);
  }

  private IQueryable<AppAuthGrant> Grants(long role) => db.AppAuthGrant.Where(g => g.AuthRoleId == role);
}
