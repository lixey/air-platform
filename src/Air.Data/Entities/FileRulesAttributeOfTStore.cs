namespace Platform.Data.Entities;

public sealed class FileRulesAttribute<TStore>() : FileRulesAttribute(typeof(TStore))
  where TStore : FileStore;
