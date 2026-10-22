# New Application Setup Guide

Follow this checklist when creating a new application repository alongside `air-platform` (e.g. `../my-app` referencing `../air-platform`).

---

## 1. Repository Root Files

Create the following files at the root of the new application repository:

1. **`Directory.Build.props`**:
   ```xml
   <Project>
     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
       <LangVersion>preview</LangVersion>
       <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
       <GenerateDocumentationFile>true</GenerateDocumentationFile>
       <NoWarn>$(NoWarn);CS1591;CS9107</NoWarn>
     </PropertyGroup>

     <ItemGroup Condition="$(MSBuildProjectName.EndsWith('.Api')) and '$(MSBuildProjectName)' != 'MyHost.Api'">
       <FrameworkReference Include="Microsoft.AspNetCore.App" />
     </ItemGroup>
   </Project>
   ```
2. **`.editorconfig`**: Copy `.editorconfig` from `air-platform/.editorconfig` so C# (`dotnet format`) and TypeScript/HTML (`prettier`) rules match the platform.
3. **`.githooks/pre-commit` & `.githooks/commit-msg`**: Copy `.githooks/` from `air-platform/.githooks/`, mark executable (`chmod +x .githooks/*`), and enable via `git config core.hooksPath .githooks`.
4. **`AGENTS.md`**: Configure agent instructions including dedicated agent dev ports (distinct from IDE ports).
5. **`Makefile`**: Include `build`, `format`, `format-check`, `check-paths`, `export-contracts`, `db-prepare`, `db-reset`, `agent-up`, `agent-restart`, `agent-down`, and `mcp-check`.

---

## 2. Backend Solution & Module Structure (`src/`)

Each module `<Mod>` is split into three projects under `src/<Mod>/`:

- **`<Mod>.Data`**:
  - `<Mod>DbContext : PlatformDbContext`
  - Entity models in `Models/` inheriting `BaseEntity`, `BaseDictionaryEntity`, `BaseCodeEntity`, or `BaseExplicitIdEntity`.
  - `GlobalUsings.cs` containing `Air.Data.*`, `Microsoft.EntityFrameworkCore`, and `<Mod>.Data.Models`.
- **`<Mod>.Api`**:
  - `<Mod>.cs`: Module handle, feature constants, and `ModuleDefinition` (menu, features, `.AllowAgent()` where needed).
  - `<Mod>Module.cs`: `PlatformModule` registration (`AddPlatformDbContext<<Mod>DbContext>`, resource registration).
  - Base services: `<Mod>Service`, `<Mod>GridService`, `<Mod>FormService`, `<Mod>FileGridService`.
  - Feature folders (`<Feature>/<Mod><Feature>GridService.cs`, `<Mod><Feature>FormService.cs`).
  - `Resources/<Mod>Resource.resx` and localized `.resx` files.
  - `GlobalUsings.cs` containing all `Air.App.*`, `Air.Data.*`, and `<Mod>.*` feature namespaces so feature files need zero local `using` statements.
- **`<Mod>.Migrations`**:
  - `<Mod>Seed.cs` and seed data classes.
  - `GlobalUsings.cs`.

### Host & DevMigrations Projects

- **`src/<Host>.Api`**:
  - Registers modules via `builder.AddPlatformModule<...>()` and optional `builder.Services.AddAirAgent()`.
  - Maps endpoints via `app.MapPlatform()` and `app.MapAirAgent()`.
  - Supports `--export-ui-contracts <output-dir>` via `ContractExporter.TryRunCliAsync(app, args)`.
  - Has its own `GlobalUsings.cs`.
- **`src/<Host>.DevMigrations`**:
  - CLI runner for `prepare` and `reset` database commands.
  - Has its own `GlobalUsings.cs`.

---

## 3. Frontend Workspace (`<web-dir>/`)

1. **Nx & Angular 22+ Workspace**:
   - Create symlink `.air-ui -> ../../air-platform/air-ui/src` inside `<web-dir>/` so TypeScript path mappings (`@air/ui`) resolve directly against platform source without absolute paths.
   - Configure `tsconfig.base.json` paths:
     ```json
     "paths": {
       "@air/ui": ["./.air-ui/index.ts"]
     }
     ```
2. **Proxy Configuration (`proxy.conf.cjs`)**:
   - Read backend target URL from an environment variable with fallback to the default IDE backend port:
     ```js
     const target = process.env.MY_APP_API_URL || 'http://localhost:5095';
     module.exports = {
       '/api': { target, secure: false },
     };
     ```
3. **Contract Export**:
   - Run `make export-contracts` to generate `contracts.ts` and `ui-texts.json` from the backend host into the frontend app.

---

## 4. Verification Before First Commit

1. Run `make db-reset` to verify schema creation and seed data.
2. Run `make export-contracts` to verify UI contract and localization export.
3. Run `make format` to format all C# and frontend files and strip unused `using`s.
4. Run `make check-paths` to ensure zero absolute machine paths exist in tracked files.
5. Run `make agent-restart` to verify both backend and frontend start cleanly on agent ports.
