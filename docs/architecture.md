# Air Platform Architecture

Air Platform is a high-velocity, metadata-driven application engine built on **.NET 10 (C# 14)** and **Angular 22+ (Tailwind CSS v4, Nx)**.

---

## 1. Repository & Project Layout

- **`src/Air.Data`**: Core data layer, EF Core 10 `PlatformDbContext`, multi-tenancy (`ITenantEntity`, `TenantFilter`), base entities (`BaseEntity`, `BaseDictionaryEntity`, `BaseCodeEntity`, `BaseExplicitIdEntity`), file storage abstractions, and expression-tree query engine (`DataProvider<T>`, `QueryMap<T>`). Strictly independent of ASP.NET Core.
- **`src/Air.Data.Postgres`**: PostgreSQL EF Core conventions (snake_case table/column naming, concurrency tokens, timestamps) and registration extensions.
- **`src/Air.App`**: Minimal API host (`MapPlatform`), module system (`PlatformModule`, `ModuleDefinition`, `Feature`), `GridService`, `FormService`, `StatusFlowService`, `FileGridService`, localization (`ILocalizer`, `.resx`), RBAC (`RightsTree`, `GrantedRights`), and TypeScript contract exporter (`ContractExporter`).
- **`src/Air.Mcp`**: Internal MCP tool definitions (`IMcpTool`).
- **`src/Air.Agent`**: Universal AI assistant runtime (`AirAgentRuntime`, `AgentFeatureRegistry`, `AgentContextQueryService`, `AgentPromptBuilder`, `AgentProposalBuilder`, `AgentProposalExecutor`, `AgentStore`) and provider abstractions (`IAgentProvider`, `AgyCliAgentProvider`).
- **`src/Air.UiContracts`**: CLI utility to export platform TypeScript interfaces (`contracts.ts`) and localized UI strings (`ui-texts.json`) into `air-ui/src/core/generated/`.
- **`air-ui/`**: Shared Angular 22+ standalone UI library (`@air/ui`) using Signals (`signal`, `computed`, `linkedSignal`), dynamic component resolution (`UI_COMPONENTS`), and Tailwind CSS v4 semantic tokens.
- **`demo/`**: Reference multi-module application (`DemoApp`, `DemoOrg`, `DemoEcom`, `Demo.Api`, `Demo.DevMigrations`, `demo/web`).

---

## 2. Core Design Conventions

### 2.1. PascalCase `Handle` Rule

Every `Handle` (on modules, features, dictionary/code entities, menu items, etc.) **must** be a `PascalCase` identifier (`^[A-Z][a-zA-Z0-9]*$`, e.g. `App`, `Fin`, `En`, `Ua`, `Uah`, `Usd`, `Slate`).
When an external or standard representation is needed (such as ISO currency code `UAH` or culture code `uk-UA`), store it in a dedicated property such as `Code`.

### 2.2. Form Overlay Presentation (`Popover` vs `Drawer`)

- **`Popover` (Default)**: Standard and compact forms use `OverlayModel.Popover(handle)` by default in the base `FormService`. Child form services must **not** redundantly override `Presentation` when using `Popover`.
- **`Drawer` (Composite & Large Forms)**: `OverlayModel.Drawer(handle)` is used only for large multi-section/multi-tab forms and is **mandatory** whenever a form contains subordinate child data grids (`elements.AddChild<TGrid>()` without `.AsFiles()`).

### 2.3. Localization & Contracts

- All user-facing strings (labels, hints, navigation titles, validation errors, statuses, AI assistant UI) must reside in `.resx` resources (`PlatformResource` or module `*Resource`) and `UiTexts.cs`.
- Never hardcode user-facing strings in C# or TypeScript/HTML templates.
- Whenever C# UI contracts or `UiTexts` change, regenerate frontend contracts (`make export-contracts`).

### 2.4. Universal AI Agent (`Air.Agent`)

- Enable AI assistant support on any feature by calling `.AllowAgent()` on its `Feature` registration in the module definition.
- `Air.Agent` automatically discovers all `.AllowAgent()` features that the current user has permission to view/edit (`GrantedRights`), builds a compact schema from `FormService` elements (including reference fields, child line grids, and file attachments), and preloads small reference dictionaries (`<= 50` rows) while providing on-demand database search for larger tables (`> 50` rows).
- All mutations proposed by the AI assistant (`create` or `update`) require explicit user confirmation in the chat UI and execute through the standard `FormService.SaveAsync` validation and persistence pipeline.
