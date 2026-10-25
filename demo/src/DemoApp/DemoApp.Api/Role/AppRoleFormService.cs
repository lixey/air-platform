namespace DemoApp.Role;

public sealed class AppRoleFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppAuthRole, AppRoleEdit>(db, app)
{
  private IReadOnlySet<string> _shown = new HashSet<string>();

  public override Feature Feature => App.Role;

  protected override async Task SetElementsAsync(Elements elements, CancellationToken cancellationToken)
  {
    elements.Add(e => e.Name).Focus();
    elements.Add(e => e.TenantId).InputOnly().AsReference<AppTenantGridService>();
    elements.Add(e => e.SortOrder);
    elements.Add(e => e.IsActive).AsCheckbox();
    elements.Add(e => e.Description).AsTextarea();

    elements.AddTab("Rights");
    _shown = elements.Add(e => e.Permissions).InputOnly().AsRightsTree().TreeKeys;

    elements.AddChild<AppRoleUserGridService>().Key("users").Label("Users");
  }

  protected override void SetRules(Rules rules)
  {
    var taken = L["A role of this name already exists in its tenant."];
    rules.For(e => e.Name).CustomAsync(async (name, c, cancellationToken) =>
    {
      var mutation = SaveContextOf(c);
      var id = mutation.Entity.Id;
      var tenant = mutation.Input.TenantId;
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
        c.Input.TenantId = db.TenantContext.TenantId;
        return;
      }

      c.Input.TenantId = c.Entity.TenantId;
      var granted = await Grants(c.Entity.Id).Select(g => g.Permission).ToListAsync(cancellationToken);
      c.Input.Permissions = [.. granted.Where(_shown.Contains).Order(StringComparer.Ordinal)];
    });

    hooks.BeforeValidate(c => c.Entity.TenantId = c.Input.TenantId);

    hooks.BeforeDelete(async (c, cancellationToken) =>
    {
      if (await IsLastActiveAdminRoleAsync(c.Entity.Id, c.Entity.TenantId, cancellationToken))
      {
        c.Refuse(L["The last administrator role cannot be deleted."]);
      }
    });

    hooks.BeforeSave(async (c, cancellationToken) =>
    {
      if (c.IsNew)
      {
        return;
      }

      var wanted = (c.Input.Permissions ?? []).Where(_shown.Contains).ToHashSet(StringComparer.Ordinal);
      var losesAdmin = !c.Input.IsActive || _shown.Contains("*") && !wanted.Contains("*");
      if (losesAdmin && await IsLastActiveAdminRoleAsync(c.Entity.Id, c.Entity.TenantId, cancellationToken))
      {
        c.Refuse(L["The last administrator role cannot be deactivated or stripped of administrator rights."]);
      }
    });

    hooks.AfterSave(async (c, cancellationToken) =>
    {
      var wanted = (c.Input.Permissions ?? []).Where(_shown.Contains).ToHashSet(StringComparer.Ordinal);
      var grants = await Grants(c.Entity.Id).ToListAsync(cancellationToken);
      db.RemoveRange(grants.Where(g => _shown.Contains(g.Permission) && !wanted.Contains(g.Permission)));
      db.AddRange(wanted.Except(grants.Select(g => g.Permission)).Select(p => new AppAuthGrant { AuthRoleId = c.Entity.Id, Permission = p }));
    });
  }

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
