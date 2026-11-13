namespace Platform.ArchTests;

public sealed class AssemblyGraphTests
{
  [Fact]
  public void AirData_HasZeroWebOrAgentAssemblyReferences()
  {
    Assembly[] dataAssemblies =
    [
      typeof(PlatformDbContext).Assembly,
      typeof(PlatformPostgresExtensions).Assembly,
      typeof(PlatformSqlServerExtensions).Assembly
    ];

    foreach (var asm in dataAssemblies)
    {
      var refs = asm.GetReferencedAssemblies().Select(r => r.Name ?? string.Empty).ToList();
      Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
      Assert.DoesNotContain("Air.Api", refs);
      Assert.DoesNotContain("Air.App", refs);
      Assert.DoesNotContain("Air.Mcp", refs);
      Assert.DoesNotContain("Air.Agent", refs);
    }
  }

  [Fact]
  public void AirApi_And_AirApp_RemainStrictlyLayeredAndFreeOfAgentBehavior()
  {
    var apiRefs = typeof(IPlatformService).Assembly.GetReferencedAssemblies().Select(r => r.Name ?? string.Empty).ToList();
    Assert.DoesNotContain("Air.App", apiRefs);
    Assert.DoesNotContain("Air.Mcp", apiRefs);
    Assert.DoesNotContain("Air.Agent", apiRefs);

    var appRefs = typeof(IGridService).Assembly.GetReferencedAssemblies().Select(r => r.Name ?? string.Empty).ToList();
    Assert.DoesNotContain("Air.Mcp", appRefs);
    Assert.DoesNotContain("Air.Agent", appRefs);

    var dataAgentTypes = typeof(PlatformDbContext).Assembly
      .GetTypes()
      .Where(t => t.Name.Contains("Agent", StringComparison.OrdinalIgnoreCase))
      .Select(t => t.FullName ?? t.Name)
      .Order(StringComparer.Ordinal)
      .ToArray();

    string[] expectedDataAgentTypes =
    [
      "Platform.Data.Entities.AgentMemoryKinds",
      "Platform.Data.Entities.AppAgentMemory",
      "Platform.Data.Entities.AppAgentModel",
      "Platform.Data.Entities.AppAgentProvider"
    ];
    Assert.Equal(expectedDataAgentTypes, dataAgentTypes);

    var apiAgentTypes = typeof(IPlatformService).Assembly
      .GetTypes()
      .Where(t => t.Name.Contains("Agent", StringComparison.OrdinalIgnoreCase))
      .Select(t => t.FullName ?? t.Name)
      .ToArray();

    string[] expectedApiAgentTypes = ["Platform.Api.Modules.AgentAccess"];
    Assert.Equal(expectedApiAgentTypes, apiAgentTypes);

    var appAgentTypes = typeof(IGridService).Assembly
      .GetTypes()
      .Where(t => t.Name.Contains("Agent", StringComparison.OrdinalIgnoreCase))
      .Select(t => t.FullName ?? t.Name)
      .ToArray();

    Assert.Empty(appAgentTypes);
  }

  [Fact]
  public void UiContracts_LiveInContractsNamespace_AndNeverExposeSensitiveProperties()
  {
    Assembly[] platformAssemblies =
    [
      typeof(PlatformDbContext).Assembly,
      typeof(IPlatformService).Assembly,
      typeof(IGridService).Assembly,
      typeof(AgentOptions).Assembly
    ];

    var contractTypes = platformAssemblies
      .SelectMany(a => a.GetTypes())
      .Where(t => t.GetCustomAttribute<UiContractAttribute>() != null)
      .ToList();

    Assert.NotEmpty(contractTypes);

    foreach (var type in contractTypes)
    {
      Assert.NotNull(type.Namespace);
      Assert.EndsWith(".Contracts", type.Namespace, StringComparison.Ordinal);

      var sensitiveProps = type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.GetCustomAttribute<SensitiveAttribute>(inherit: true) != null)
        .Select(p => $"{type.FullName}.{p.Name}")
        .ToList();

      Assert.Empty(sensitiveProps);
    }
  }
}
