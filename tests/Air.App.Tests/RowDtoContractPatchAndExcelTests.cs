using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Http.HttpResults;
using Platform.App.Grid.Internals;
using Platform.App.Tests.Internals;

namespace Platform.App.Tests;

public sealed class RowDtoContractPatchAndExcelTests : IDisposable
{
  private readonly SqliteConnection _connection;

  public RowDtoContractPatchAndExcelTests()
  {
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    ModuleCatalog.Instance.Register(new TestAppModuleImpl());
  }

  public void Dispose() => _connection.Dispose();

  private AppTestDbContext CreateDbContext(long tenantId = 1, long userId = 10, params string[] rights)
  {
    var options = new DbContextOptionsBuilder<AppTestDbContext>()
      .UseSqlite(_connection)
      .Options;
    var effectiveRights = rights.Length > 0
      ? new HashSet<string>(rights, StringComparer.OrdinalIgnoreCase)
      : new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase);
    var db = new AppTestDbContext(options, new FixedTenant(tenantId, userId, effectiveRights));
    db.Database.EnsureCreated();
    return db;
  }

  [Fact]
  public void ContractExporter_EnforcesUiContract_Inheritance_Enums_Vocabularies_Primitives_AndCollisions()
  {
    // 1. Default platform contracts contain RowDto, OpenTargetDto, and GridPageDto<TRow = RowDto>
    var baseTs = ContractExporter.GenerateContracts();
    Assert.Contains("export interface RowDto {", baseTs, StringComparison.Ordinal);
    Assert.Contains("export interface OpenTargetDto {", baseTs, StringComparison.Ordinal);
    Assert.Contains("export interface GridPageDto<TRow = RowDto> extends Page<TRow> {", baseTs, StringComparison.Ordinal);

    // 2. Valid dynamic assembly: [UiContract] base + derived inheritance (DeclaredOnly), Guid/uint/ulong, string enum, vocabulary
    var validAsm = BuildDynamicContractAssembly(
      "Wave5ValidContracts",
      mod =>
      {
        var uiContractAttr = new CustomAttributeBuilder(typeof(UiContractAttribute).GetConstructor(Type.EmptyTypes)!, []);

        var vocab = mod.DefineType(
          "SampleContracts.SampleToneVocabulary",
          TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        vocab.SetCustomAttribute(uiContractAttr);
        var constField = vocab.DefineField("Calm", typeof(string), FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal);
        constField.SetConstant("calm");
        var constField2 = vocab.DefineField("Bold", typeof(string), FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal);
        constField2.SetConstant("bold");
        vocab.CreateType();

        var enumBuilder = mod.DefineEnum("SampleContracts.SampleWireState", TypeAttributes.Public, typeof(int));
        enumBuilder.SetCustomAttribute(uiContractAttr);
        enumBuilder.SetCustomAttribute(new CustomAttributeBuilder(
          typeof(JsonConverterAttribute).GetConstructor([typeof(Type)])!,
          [typeof(JsonStringEnumConverter)]));
        enumBuilder.DefineLiteral("Idle", 0);
        enumBuilder.DefineLiteral("Running", 1);
        var wireEnumType = enumBuilder.CreateTypeInfo();

        var baseDto = mod.DefineType("SampleContracts.SampleBaseContractDto", TypeAttributes.Public | TypeAttributes.Class);
        baseDto.SetCustomAttribute(uiContractAttr);
        DefineAutoProperty(baseDto, "CorrelationId", typeof(Guid));
        DefineAutoProperty(baseDto, "AttemptCount", typeof(uint));
        var baseDtoType = baseDto.CreateType();

        var derivedDto = mod.DefineType("SampleContracts.SampleDerivedContractDto", TypeAttributes.Public | TypeAttributes.Sealed, baseDtoType);
        derivedDto.SetCustomAttribute(uiContractAttr);
        DefineAutoProperty(derivedDto, "Sequence", typeof(ulong));
        DefineAutoProperty(derivedDto, "State", wireEnumType);
        derivedDto.CreateType();
      });

    var customTs = ContractExporter.GenerateContracts(validAsm);
    Assert.Contains("export type SampleToneVocabulary = 'calm' | 'bold';", customTs, StringComparison.Ordinal);
    Assert.Contains("export type SampleWireState = 'Idle' | 'Running';", customTs, StringComparison.Ordinal);
    Assert.Contains("export interface SampleBaseContractDto {\n  correlationId: string;\n  attemptCount: number;\n}", customTs.Replace("\r\n", "\n"), StringComparison.Ordinal);
    Assert.Contains("export interface SampleDerivedContractDto extends SampleBaseContractDto {\n  sequence: number;\n  state: SampleWireState;\n}", customTs.Replace("\r\n", "\n"), StringComparison.Ordinal);

    // 3. Unmarked referenced property type fails fast instead of silently emitting Record<string, unknown>
    var unmarkedAsm = BuildDynamicContractAssembly(
      "Wave5UnmarkedContracts",
      mod =>
      {
        var uiContractAttr = new CustomAttributeBuilder(typeof(UiContractAttribute).GetConstructor(Type.EmptyTypes)!, []);
        var unmarked = mod.DefineType("SampleContracts.UnmarkedInnerType", TypeAttributes.Public | TypeAttributes.Sealed).CreateType();
        var outer = mod.DefineType("SampleContracts.BrokenOuterDto", TypeAttributes.Public | TypeAttributes.Sealed);
        outer.SetCustomAttribute(uiContractAttr);
        DefineAutoProperty(outer, "Inner", unmarked);
        outer.CreateType();
      });

    var unmarkedEx = Assert.Throws<InvalidOperationException>(() => ContractExporter.GenerateContracts(unmarkedAsm));
    Assert.Contains("[UiContract]", unmarkedEx.Message, StringComparison.Ordinal);
    Assert.Contains("UnmarkedInnerType", unmarkedEx.Message, StringComparison.Ordinal);

    // 4. Duplicate TypeScript contract name across two namespaces fails fast
    var duplicateAsm = BuildDynamicContractAssembly(
      "Wave5DuplicateContracts",
      mod =>
      {
        var uiContractAttr = new CustomAttributeBuilder(typeof(UiContractAttribute).GetConstructor(Type.EmptyTypes)!, []);
        var first = mod.DefineType("NsOne.CollisionDto", TypeAttributes.Public | TypeAttributes.Sealed);
        first.SetCustomAttribute(uiContractAttr);
        DefineAutoProperty(first, "Id", typeof(long));
        first.CreateType();

        var second = mod.DefineType("NsTwo.CollisionDto", TypeAttributes.Public | TypeAttributes.Sealed);
        second.SetCustomAttribute(uiContractAttr);
        DefineAutoProperty(second, "Name", typeof(string));
        second.CreateType();
      });

    var dupEx = Assert.Throws<InvalidOperationException>(() => ContractExporter.GenerateContracts(duplicateAsm));
    Assert.Contains("Duplicate TypeScript contract name 'CollisionDto'", dupEx.Message, StringComparison.Ordinal);
  }

  [Fact]
  public async Task RowPathReader_And_GridExcelWriter_PreserveExactDecimals_Longs_DictionaryKeys_AndStreamCompute()
  {
    // 1. RowPathReader prioritizes dictionary entries & RowDto.Cells over CLR properties (Count, Item, Keys, Values)
    var rowDto = new RowDto
    {
      Id = 9007199254740993L,
      Ver = 7,
      Cells = new Dictionary<string, object?>
      {
        ["count"] = 42,
        ["keys"] = "K-100",
        ["values"] = "V-200",
        ["item"] = "SKU-9",
        ["customer"] = new Dictionary<string, object?> { ["name"] = "Acme Corp" }
      }
    };

    Assert.Equal(42, RowPathReader.Compile(typeof(RowDto), "count")!(rowDto));
    Assert.Equal("K-100", RowPathReader.Compile(typeof(RowDto), "keys")!(rowDto));
    Assert.Equal("V-200", RowPathReader.Compile(typeof(RowDto), "values")!(rowDto));
    Assert.Equal("SKU-9", RowPathReader.Compile(typeof(RowDto), "item")!(rowDto));
    Assert.Equal("Acme Corp", RowPathReader.Compile(typeof(RowDto), "customer.name")!(rowDto));
    Assert.Equal(9007199254740993L, RowPathReader.Compile(typeof(RowDto), "id")!(rowDto));
    Assert.Equal(7L, RowPathReader.Compile(typeof(RowDto), "ver")!(rowDto));

    var rawDict = new Dictionary<string, object?>
    {
      ["count"] = 999,
      ["keys"] = "dict-keys-cell",
      ["values"] = "dict-values-cell",
      ["item"] = "dict-item-cell"
    };
    Assert.Equal(999, RowPathReader.Compile(typeof(Dictionary<string, object?>), "count")!(rawDict));
    Assert.Equal("dict-keys-cell", RowPathReader.Compile(typeof(Dictionary<string, object?>), "keys")!(rawDict));
    Assert.Equal("dict-values-cell", RowPathReader.Compile(typeof(Dictionary<string, object?>), "values")!(rawDict));
    Assert.Equal("dict-item-cell", RowPathReader.Compile(typeof(Dictionary<string, object?>), "item")!(rawDict));

    // 2. GridExcelWriter writes exact InvariantCulture decimals & 64-bit longs without IEEE-754 double rounding,
    //    and stops streaming immediately at limit + 1 with truncation note.
    var columns = new List<GridColumnDto>
    {
      new() { Path = "bigId", Label = "BigId", Type = UiFieldType.Number, Cell = new GridCellNumberModel { Decimals = 0 } },
      new() { Path = "preciseAmount", Label = "Amount", Type = UiFieldType.Number, Cell = new GridCellMoneyModel { Decimals = 2 } }
    };

    var yielded = 0;
    async IAsyncEnumerable<object> StreamRowsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
      ct.ThrowIfCancellationRequested();
      yielded++;
      yield return new RowDto
      {
        Id = 1,
        Cells = new Dictionary<string, object?>
        {
          ["bigId"] = 9007199254740993L,
          ["preciseAmount"] = 123456789012345678.99m
        }
      };

      yielded++;
      yield return new RowDto
      {
        Id = 2,
        Cells = new Dictionary<string, object?>
        {
          ["bigId"] = 9007199254740995L,
          ["preciseAmount"] = -987654321098765432.11m
        }
      };

      yielded++;
      yield return new RowDto
      {
        Id = 3,
        Cells = new Dictionary<string, object?>
        {
          ["bigId"] = 3L,
          ["preciseAmount"] = 1.00m
        }
      };

      throw new InvalidOperationException("Must not enumerate beyond limit + 1!");
    }

    using var ms = new MemoryStream();
    var (writtenCount, isTruncated) = await GridExcelWriter.WriteAsync(
      ms,
      "Precision:Export/Test",
      columns,
      _ => null,
      StreamRowsAsync(),
      Localizer.Instance,
      TimeZoneInfo.Utc,
      limit: 2,
      truncationNote: "Cut at 2 rows");

    Assert.Equal(2, writtenCount);
    Assert.True(isTruncated);
    Assert.Equal(3, yielded);

    ms.Position = 0;
    using (var doc = SpreadsheetDocument.Open(ms, isEditable: false))
    {
      var wsPart = doc.WorkbookPart!.WorksheetParts.Single();
      var cells = wsPart.Worksheet.Descendants<Cell>().ToDictionary(c => c.CellReference!.Value!, c => c);
      Assert.Equal("9007199254740993", cells["A2"].CellValue!.Text);
      Assert.Equal("123456789012345678.99", cells["B2"].CellValue!.Text);
      Assert.Equal("9007199254740995", cells["A3"].CellValue!.Text);
      Assert.Equal("-987654321098765432.11", cells["B3"].CellValue!.Text);
      Assert.Equal("Cut at 2 rows", cells["A5"].InlineString!.Text!.Text);
    }

    // 3. GridService.ExportAsync streams RowComputeAction into xlsx
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));
    var inv = new TestInvoice { Number = "INV-XLSX", Status = 1 };
    db.Invoices.Add(inv);
    await db.SaveChangesAsync();

    var line = new TestInvoiceLine
    {
      InvoiceId = inv.Id,
      Title = "ExactLine",
      Amount = 123456789012.34m,
      Priority = TestPriority.Draft
    };
    db.InvoiceLines.Add(line);
    await db.SaveChangesAsync();

    var invoiceFormPath = ServicePathResolver.Resolve(typeof(TestInvoiceFormService));
    var grid = new TestDisabledPatchLineGridService(db, app);
    var exportResult = await grid.ExportAsync(
      new GridRequest { ParentService = invoiceFormPath, ParentId = inv.Id },
      CancellationToken.None);
    var fileResult = Assert.IsType<FileStreamHttpResult>(exportResult);
    await using (fileResult.FileStream)
    {
      using var exportedDoc = SpreadsheetDocument.Open(fileResult.FileStream, isEditable: false);
      var ws = exportedDoc.WorkbookPart!.WorksheetParts.Single().Worksheet;
      var inlineTexts = ws.Descendants<InlineString>().Select(s => s.Text?.Text).Where(t => t != null).ToList();
      Assert.Contains($"badge:{line.Id}", inlineTexts);
      var rawNumbers = ws.Descendants<CellValue>().Select(v => v.Text).ToList();
      Assert.Contains("123456789012.34", rawNumbers);
    }
  }

  [Fact]
  public async Task PatchOnSelect_AppliesOntoDisabledTarget_InDraft_FormSave_AndGridSaveCell_WhileBlockingClientTampering()
  {
    await using var db = CreateDbContext();
    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));

    var inv = new TestInvoice { Number = "INV-PATCH", Status = 1 };
    var tagStarter = new TestTag { Name = "Starter Pack", IsActive = true, DefaultPrice = 42.75m };
    var tagEnterprise = new TestTag { Name = "Enterprise Pack", IsActive = true, DefaultPrice = 105.50m };
    db.Invoices.Add(inv);
    db.Tags.AddRange(tagStarter, tagEnterprise);
    await db.SaveChangesAsync();

    var invoiceFormPath = ServicePathResolver.Resolve(typeof(TestInvoiceFormService));
    var form = new TestDisabledPatchLineFormService(db, app);

    // 1. PrepareDraftAsync patches .Disabled() Amount and OnlyWhenEmpty Title when empty
    var draft1 = await form.PrepareDraftAsync(
      recordId: null,
      overrides: new Dictionary<string, object?> { ["tagId"] = tagStarter.Id },
      parentId: inv.Id,
      parentService: invoiceFormPath,
      triggerField: "tagId");
    Assert.Equal(42.75m, Convert.ToDecimal(draft1.Values["amount"], CultureInfo.InvariantCulture));
    Assert.Equal("Starter Pack", draft1.Values["title"]?.ToString());

    // OnlyWhenEmpty keeps existing user-typed Title while still updating .Disabled() Amount
    var draft2 = await form.PrepareDraftAsync(
      recordId: null,
      overrides: new Dictionary<string, object?>
      {
        ["tagId"] = tagEnterprise.Id,
        ["title"] = "Custom Title"
      },
      parentId: inv.Id,
      parentService: invoiceFormPath,
      triggerField: "tagId");
    Assert.Equal(105.50m, Convert.ToDecimal(draft2.Values["amount"], CultureInfo.InvariantCulture));
    Assert.Equal("Custom Title", draft2.Values["title"]?.ToString());

    // 2. SaveAsync (create): forged client Amount (999999m) and HiddenNote ("FORGED") are discarded on .Disabled() fields,
    //    server applies authentic tagStarter.DefaultPrice (42.75m) & tagStarter.Name ("Starter Pack"), and Priority = 0 (Draft) is preserved!
    var createPayload = JsonSerializer.SerializeToElement(new
    {
      tagId = tagStarter.Id,
      amount = 999999m,
      hiddenNote = "FORGED",
      title = "Line A",
      priority = 0
    });
    var createdObj = await form.SaveAsync(
      createPayload,
      new FormRequest { ParentId = inv.Id, ParentService = invoiceFormPath, Value = createPayload },
      CancellationToken.None);
    var createdEntity = Assert.IsType<TestInvoiceLine>(createdObj);

    var savedLine = await db.InvoiceLines.AsNoTracking().FirstAsync(l => l.Id == createdEntity.Id);
    Assert.Equal(42.75m, savedLine.Amount);
    Assert.Equal("Starter Pack", savedLine.HiddenNote);
    Assert.Equal("Line A", savedLine.Title);
    Assert.Equal(TestPriority.Draft, savedLine.Priority);

    // 3. SaveAsync (update) without changing Tag: forged client Amount is rejected and Amount remains 42.75m
    var tamperSameTagPayload = JsonSerializer.SerializeToElement(new
    {
      tagId = tagStarter.Id,
      amount = 777777m,
      hiddenNote = "FORGED-2",
      title = "Edited Title",
      priority = 0
    });
    var updateSameTagForm = new TestDisabledPatchLineFormService(db, app);
    await updateSameTagForm.SaveAsync(
      tamperSameTagPayload,
      new FormRequest
      {
        Id = savedLine.Id,
        Ver = savedLine.Ver,
        ParentId = inv.Id,
        ParentService = invoiceFormPath,
        Value = tamperSameTagPayload
      },
      CancellationToken.None);

    var afterSameTag = await db.InvoiceLines.AsNoTracking().FirstAsync(l => l.Id == savedLine.Id);
    Assert.Equal(42.75m, afterSameTag.Amount);
    Assert.Equal("Starter Pack", afterSameTag.HiddenNote);
    Assert.Equal("Edited Title", afterSameTag.Title);

    // 4. SaveAsync (update) changing Tag to tagEnterprise: server updates .Disabled() Amount to 105.50m,
    //    while OnlyWhenEmpty keeps non-empty .Disabled() HiddenNote ("Starter Pack")!
    var changeTagPayload = JsonSerializer.SerializeToElement(new
    {
      tagId = tagEnterprise.Id,
      amount = 888888m,
      hiddenNote = "FORGED-3",
      title = "Edited Title",
      priority = (int)TestPriority.Urgent
    });
    var updateChangedTagForm = new TestDisabledPatchLineFormService(db, app);
    await updateChangedTagForm.SaveAsync(
      changeTagPayload,
      new FormRequest
      {
        Id = afterSameTag.Id,
        Ver = afterSameTag.Ver,
        ParentId = inv.Id,
        ParentService = invoiceFormPath,
        Value = changeTagPayload
      },
      CancellationToken.None);

    var afterChangedTag = await db.InvoiceLines.AsNoTracking().FirstAsync(l => l.Id == savedLine.Id);
    Assert.Equal(105.50m, afterChangedTag.Amount);
    Assert.Equal("Starter Pack", afterChangedTag.HiddenNote);
    Assert.Equal("Edited Title", afterChangedTag.Title);
    Assert.Equal(TestPriority.Urgent, afterChangedTag.Priority);

    // 5. GridService.SaveCellAsync editing Tag cell back to tagStarter updates .Disabled() Amount in DB and returned RowDto!
    await using var sp = new ServiceCollection()
      .AddTransient(_ => new TestDisabledPatchLineFormService(db, app))
      .BuildServiceProvider();
    var grid = new TestDisabledPatchLineGridService(db, app);
    var cellDto = await grid.SaveCellAsync(
      new GridCellRequest
      {
        Id = afterChangedTag.Id,
        Ver = afterChangedTag.Ver,
        ParentId = inv.Id,
        ParentService = invoiceFormPath,
        Path = "tag",
        Value = JsonSerializer.SerializeToElement(tagStarter.Id)
      },
      sp,
      CancellationToken.None);
    Assert.Equal(MutationOutcome.Committed, cellDto.Outcome);
    Assert.NotNull(cellDto.Row);
    Assert.Equal(42.75m, Convert.ToDecimal(cellDto.Row.Cells["amount"], CultureInfo.InvariantCulture));

    var afterGridCell = await db.InvoiceLines.AsNoTracking().FirstAsync(l => l.Id == savedLine.Id);
    Assert.Equal(42.75m, afterGridCell.Amount);
  }

  [Fact]
  public async Task ExportAsync_Over500RowsWithComputeSecondaryQueries_ClosesReaderBetweenChunks()
  {
    var interceptor = new SingleActiveReaderInterceptor();
    var options = new DbContextOptionsBuilder<AppTestDbContext>()
      .UseSqlite(_connection)
      .AddInterceptors(interceptor)
      .Options;
    var rights = new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase);
    await using var db = new AppTestDbContext(options, new FixedTenant(1, 10, rights));
    db.Database.EnsureCreated();

    db.Tags.AddRange(new TestTag { Name = "T1" }, new TestTag { Name = "T2" });
    for (var i = 1; i <= 525; i++)
    {
      db.Invoices.Add(new TestInvoice { Number = $"INV-{i:D4}", Status = 1 });
    }

    await db.SaveChangesAsync();
    Assert.Equal(0, interceptor.ActiveReaders);

    var app = new TestPlatformAppService(db, new FixedUser(10, "*"));
    var grid = new TestComputedExportGridService(db, app);
    var result = await grid.ExportAsync(new GridRequest(), CancellationToken.None);

    var fileResult = Assert.IsType<FileStreamHttpResult>(result);
    await using var stream = fileResult.FileStream;
    Assert.Equal(2, grid.ComputeBatchCalls);
    Assert.Equal(0, interceptor.ActiveReaders);

    using var doc = SpreadsheetDocument.Open(stream, isEditable: false);
    var sheetData = doc.WorkbookPart!.WorksheetParts.Single().Worksheet.GetFirstChild<SheetData>()!;
    var rows = sheetData.Elements<Row>().ToList();
    Assert.Equal(526, rows.Count);
    Assert.Contains("INV-0525:2", rows[^1].InnerText, StringComparison.Ordinal);
  }

  private static Assembly BuildDynamicContractAssembly(string name, Action<ModuleBuilder> defineTypes)
  {
    var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
    var modBuilder = asmBuilder.DefineDynamicModule(name + ".dll");
    defineTypes(modBuilder);
    return asmBuilder;
  }

  private static void DefineAutoProperty(TypeBuilder typeBuilder, string name, Type propertyType)
  {
    var field = typeBuilder.DefineField($"<{name}>k__BackingField", propertyType, FieldAttributes.Private);
    var prop = typeBuilder.DefineProperty(name, PropertyAttributes.None, propertyType, null);

    var getter = typeBuilder.DefineMethod(
      $"get_{name}",
      MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
      propertyType,
      Type.EmptyTypes);
    var getIl = getter.GetILGenerator();
    getIl.Emit(OpCodes.Ldarg_0);
    getIl.Emit(OpCodes.Ldfld, field);
    getIl.Emit(OpCodes.Ret);

    var setter = typeBuilder.DefineMethod(
      $"set_{name}",
      MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
      null,
      [propertyType]);
    var setIl = setter.GetILGenerator();
    setIl.Emit(OpCodes.Ldarg_0);
    setIl.Emit(OpCodes.Ldarg_1);
    setIl.Emit(OpCodes.Stfld, field);
    setIl.Emit(OpCodes.Ret);

    prop.SetGetMethod(getter);
    prop.SetSetMethod(setter);
  }
}
