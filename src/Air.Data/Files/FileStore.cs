namespace Air.Data.Files;

public abstract class FileStore
{
  public const int KeyMaxLength = 32;

  public static string KeyOf(Type store)
  {
    ArgumentNullException.ThrowIfNull(store);
    if (!typeof(FileStore).IsAssignableFrom(store))
    {
      throw new InvalidOperationException($"Store class '{store.Name}' does not derive from {nameof(FileStore)}.");
    }

    var key = store.GetCustomAttribute<FileStoreAttribute>(inherit: false)?.Key
              ?? throw new InvalidOperationException(
                $"Store class '{store.Name}' declares no [FileStore]: a store is named by its key, [FileStore(\"media\")]."
              );
    if (!IsKey(key))
    {
      throw new InvalidOperationException(
        $"Store class '{store.Name}' declares the key '{key}': a store key is [a-z][a-z0-9_]* of at most {KeyMaxLength} characters."
      );
    }

    return key;
  }

  public static IReadOnlyList<string> KeysOf(IEnumerable<Type> entities)
  {
    ArgumentNullException.ThrowIfNull(entities);
    var stores = new Dictionary<string, Type>(StringComparer.Ordinal);
    foreach (var entity in entities.Where(e => typeof(BaseAppFile).IsAssignableFrom(e) && !e.IsAbstract))
    {
      var rules = FileRulesAttribute.Of(entity);
      if (rules.StoreType is not { } store)
      {
        continue;
      }

      var key = KeyOf(store);
      if (stores.TryGetValue(key, out var holder) && holder != store)
      {
        throw new InvalidOperationException(
          $"Store classes '{holder.Name}' and '{store.Name}' both declare the key '{key}': a key names one store."
        );
      }

      stores[key] = store;
    }

    return [.. stores.Keys];
  }

  private static bool IsKey(string? key) =>
    key is { Length: > 0 and <= KeyMaxLength } && char.IsAsciiLetterLower(key[0]) &&
    key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');
}
