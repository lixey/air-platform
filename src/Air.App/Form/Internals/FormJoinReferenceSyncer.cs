namespace Platform.App.Form.Internals;

internal static class FormJoinReferenceSyncer
{
  public static void Configure<TJoin>(
    FormElementDto dto,
    Expression<Func<TJoin, long>> ownerKey,
    Expression<Func<TJoin, long>> referenceKey)
    where TJoin : class, IEntity, new()
  {
    var ownerProp = (ownerKey.Body is UnaryExpression u1 ? u1.Operand : ownerKey.Body) is MemberExpression m1
      ? m1.Member as PropertyInfo
      : null;
    var refProp = (referenceKey.Body is UnaryExpression u2 ? u2.Operand : referenceKey.Body) is MemberExpression m2
      ? m2.Member as PropertyInfo
      : null;
    var getRefId = referenceKey.Compile();
    var binding = FormElementBinding.Of(dto);

    binding.JoinLoader = async (db, ownerId, ct) =>
    {
      var param = ownerKey.Parameters[0];
      var ownerPred = Expression.Lambda<Func<TJoin, bool>>(
        Expression.Equal(ownerKey.Body, Expression.Constant(ownerId)),
        param);
      return await db.Set<TJoin>()
        .AsNoTracking()
        .Where(ownerPred)
        .OrderBy(x => x.Id)
        .Select(referenceKey)
        .ToListAsync(ct);
    };

    binding.JoinSyncer = async (db, ownerId, desiredIds, ct) =>
    {
      var distinctDesired = desiredIds.Where(id => id > 0).Distinct().ToList();
      var param = ownerKey.Parameters[0];
      var ownerPred = Expression.Lambda<Func<TJoin, bool>>(
        Expression.Equal(ownerKey.Body, Expression.Constant(ownerId)),
        param);
      var existingRows = await db.Set<TJoin>().Where(ownerPred).ToListAsync(ct);
      var toRemove = existingRows.Where(r => !distinctDesired.Contains(getRefId(r))).ToList();
      if (toRemove.Count > 0)
      {
        db.Set<TJoin>().RemoveRange(toRemove);
      }

      var existingRefIds = existingRows.Select(getRefId).ToHashSet();
      var added = false;
      foreach (var refId in distinctDesired.Where(id => !existingRefIds.Contains(id)))
      {
        var joinEntity = new TJoin();
        ownerProp?.SetValue(joinEntity, ownerId);
        refProp?.SetValue(joinEntity, refId);
        if (joinEntity is IHasTenant tenantJoin &&
            tenantJoin.TenantId == 0 &&
            db is PlatformDbContext pDb &&
            pDb.TenantContext.TenantId is { } tid)
        {
          tenantJoin.TenantId = tid;
        }

        db.Set<TJoin>().Add(joinEntity);
        added = true;
      }

      return toRemove.Count > 0 || added;
    };
  }
}
