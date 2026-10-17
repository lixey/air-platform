namespace DemoApp.Data.Models;

/// <summary>
/// A color: a row of code, global, one per hue of the platform's palette.
/// </summary>
[Index(nameof(Handle), IsUnique = true)]
public sealed class AppColor : BaseDictionaryEntity, IReadOnlyEntity;
