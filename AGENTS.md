# air-platform: Agent Instructions

## 1. Communication & Workflow Protocol
- **Language Matching**: Always respond in the exact same language the user used in their message.
- **Task Restatement First**: Upon receiving a task, first restate it in clear, coherent prose (broken into numbered points if multi-part) and expand on any implicit details you notice are needed, so the user can see you understood it accurately before reporting actions.
- **Plan & Approval for Complex Tasks**: If a task requires extended analysis, touches core architecture, or involves design trade-offs, present a compact numbered implementation plan, impact analysis, and critical points first, and wait for explicit user approval before writing code.
- **Platform Evolution Over Workarounds**: The Air Platform is actively evolving. Never work around or fight platform limitations with hacks — instead, propose clean, universal architectural improvements to the platform itself with thorough justification.
- **Controlled Subagent Audit (`claude-opus-5-5-high` via `agy` or `claude` CLI)**: At the end of every change (before final commit/push), launch an independent controlled audit session explicitly on **Claude Opus 5.5 (High)** (`make audit`, which runs `agy --model claude-opus-5-5-high --dangerously-skip-permissions -p "<audit prompt>"` and automatically falls back to `claude --model claude-opus-5-5 --effort high --dangerously-skip-permissions -p "<audit prompt>"` if `agy` quota/limits are exhausted, or use `make audit AUDIT_CLI=claude`; never use `invoke_subagent` with `TypeName: 'research'` as it forces a lighter Gemini model) to review the diff for architectural compliance, edge cases, and regressions. Fix objective issues immediately; if the auditor raises a controversial design trade-off, surface it to the user for discussion.
- **Format, Verify, Commit & Push Automatically**: After every working change, run `make format` (`dotnet format` + `prettier`), verify the unified gate (`make gate`: `format-check`, `check-paths`, `check-arch`, `check-ts`, `build`, `test`), then immediately commit and `git push` without asking.
- **Mandatory Harness Telemetry & Self-Assessment Footer**: At the end of every change/task response, include a concise 3-line telemetry footer:
  1. **MCP Usage & Verdict**: How much Rider/WebStorm MCP was used (`0%`–`100%`, tools called) and an honest verdict on whether it helped or if CLI/native tools were faster.
  2. **Change Stats & Self-Score**: Number of files/projects touched, verification gate status, and self-assessment score (`1–10`) with brief rationale.
  3. **Harness & Environment Recommendations**: Concrete suggestions for improving `AGENTS.md`, `Makefile`, or developer/agent tooling.

## 2. Core Principles (Radical Simplicity & Speed)
- **YAGNI & Anti-Bloat**: The best code is the code never written. Solve problems with the least new code. No speculative abstractions, wrappers, or "future-proofing".
- **No God-Classes & Strictly One Type Per File**: Keep files small and single-purpose (~150–350 lines). Every class, record, struct, interface, or enum MUST reside in its own dedicated `.cs` file named after the type (use `OfT` suffix for generic overloads, e.g., `PlatformModuleOfT.cs`).
- **Strict Folder & File Naming Conventions (`Contracts/`, `Internals/`, `*Extensions.cs`)**:
  - **`Contracts/`**: Every wire contract (`*Dto`, `*Request`, `*Model`, contract enums/attributes) MUST live inside a `Contracts/` folder.
  - **`Internals/`**: Every `internal` helper, parser, cache, or implementation class MUST live inside an `Internals/` (plural) folder. Never add `*.Internals` namespaces to `GlobalUsings.cs` — use a standard local `using ...Internals;` at the top of the `.cs` files that consume them.
  - **`*Extensions.cs`**: Every static class containing extension methods (`this ...`) and its `.cs` file MUST end with the suffix `Extensions`.
- **Brand-Neutral `Platform.*` Namespaces & Physical Path Matching**: The brand `Air` may ONLY appear in `.csproj` / `.slnx` project names. All platform C# root namespaces use `Platform.*` (`Platform.Data`, `Platform.Api`, `Platform.App`, `Platform.Agent`, `Platform.Mcp`, `Platform.UiContracts`) for effortless rebranding. Every `.cs` file's `namespace` MUST strictly match its physical directory path relative to its project root.
- **Universal Metadata-Driven Design**: Platform code (`src/`, `air-ui/`) must never contain application-specific logic (`Demo`, `MyFin`). All capabilities (including the built-in AI assistant) must be driven universally by declarative metadata on `Feature`, `FormService` (`Elements`, `AsReference`, `PatchOnSelect`, `AddChild`), `GridService`, and `Rights`.
- **Zero Absolute Paths**: Never commit machine-specific or user home absolute filesystem paths in any tracked file. Always use workspace-relative paths.
- **Zero Premature Tests & Minimal Docs**: Do not write unit/integration tests or external specs (no OpenSpec) unless explicitly asked. Maintain only concise, practical guides in `docs/`.
- **Token Efficiency & Context Economy**: Keep responses concise, direct, and actionable. Make minimal, targeted diffs instead of rewriting large files.

## 3. Target Architecture
- **Tech Stack**: .NET 10, C# 14, Angular 22+ (Standalone components, Signals `signal` / `computed` / `linkedSignal`, Signal Forms), Tailwind CSS v4, Nx workspace.
- **Backend Projects (`src/`)**:
  - `Air.Data` / `Air.Data.Postgres` / `Air.Data.SqlServer` (`Platform.Data.*`): EF Core 10 base, multi-tenancy, entities (`AppAgentModel`, `AppAgentMemory`), querying. Strictly independent of ASP.NET Core/Web.
  - `Air.Api` (`Platform.Api`): Headless modular API host, module lifecycle (`IPlatformModule`, `Feature`, `Right`), authentication, sessions, RBAC, localization, mutations, file storage/streaming, and universal `PlatformService` HTTP dispatch. Zero dependency on backoffice UI engines.
  - `Air.App` (`Platform.App`): Rich backoffice UI engine built on `Air.Api` — `GridService`, `FormService`, `ReportService`, `ScreenService`, `StatusFlowService`, `OverlayModel`, `PanelLayout`, `RightsTree`, and TypeScript `ContractExporter`. Host entry point via `PlatformHost.RunAsync(args, options => ..., app => ...)`.
  - `Air.Mcp` / `Air.Agent` (`Platform.Mcp` / `Platform.Agent`): Built-in AI assistant (`Feature.AllowAgent()`), operating in-process via `FormService`/`GridService`, persistent DB memory & rules (`AppAgentMemory`), and `<air_proposal>` confirmation cards, isolated by `(TenantId, UserId)` and user permissions.
  - `Air.UiContracts` (`Platform.UiContracts`): Fast CLI tool in `src/` to export TypeScript contracts (`contracts.ts`) and UI texts (`ui-texts.json`).
- **Frontend (`air-ui/` & `demo/web/`)**:
  - Dynamic component resolution via `UI_COMPONENTS` mapped by `kind`.
  - Semantic color tokens, Tailwind CSS v4, WCAG AA.
- **Demo (`demo/`)**:
  - Reference application in Nx (`demo/web`) and modular backend (`demo/src/Demo.Api`).

## 4. Code Style, `GlobalUsings.cs`, Contracts & Localization
- **`GlobalUsings.cs` & Clean Imports**: Shared platform and library namespaces must be declared in `Directory.Build.props` and each project's `GlobalUsings.cs`. Every `.cs` file must be free of unused or duplicate `using` directives (enforced via `IDE0005` during `make format`).
- **Zero Hardcoded Strings**: All user-facing texts must live in `.resx` + `.uk.resx` resources (`PlatformResource` + `UiTexts.cs` for platform, `<Module>Resource` for modules) respecting `CultureInfo.CurrentUICulture`.
- **Typed Contracts**: Decorate shared DTOs with `[UiContract]` and export via `make contracts`. Never hardcode backend lists or models in the frontend.
- **PascalCase `Handle` Rule**: Every `Handle` (on modules, dictionary/code entities, menu items, agent providers) MUST be a `PascalCase` identifier (`^[A-Z][a-zA-Z0-9]*$`, e.g. `App`, `Fin`, `En`, `Ua`, `Uah`, `Usd`, `Slate`, `Agy`). Use `Code` or `Id` for external formats (ISO `UAH`, culture `uk-UA`, CLI model IDs).
- **Simple, Everyday English Vocabulary & Readability Over Line-Count Savings**: Always use clear, common English words in identifiers (e.g., `RemoveUserAsync`, `ClearAsync` instead of rare words like `EvictUserAsync`). Never sacrifice code readability or clarity for small line-count reductions — any deduplication that might obscure flow must be reviewed case-by-case with the user first.

## 5. UI & Form Conventions
- **Reuse Platform Primitives**: Always use `air-ui` components, services, and tokens (`ui-nav`, `ui-btn`, `SelectService`, `OverlayService`, `fieldClasses`, `UiIcon` + `ICON_SET`, semantic classes `bg-surface`, `bg-field`, `border-line`, `border-field-edge`) instead of custom native controls. Use `flex items-center justify-center` on `<i [icon]="...">` tags.
- **Responsive Panels**: Respect `LayoutPanelsService` (`panels.isNarrow()`). On narrow screens, open side panels inside a modal dialog (`presentation: 'dialog'`).
- **Form Overlay Presentation Rule (`Drawer` vs `Popover`)**:
  - **Default (`Drawer`)**: All standard and composite forms use `OverlayModel.Drawer(handle)` by default in the base `FormService` (as most entities have subordinate child data tables via `elements.AddChild(...)`). Child form services must NOT redundantly override `Presentation` when using `Drawer`.
  - **Compact Forms (`Popover`)**: Override `Presentation` with `OverlayModel.Popover(handle)` only for compact standalone forms that have no subordinate child data tables.
- **Bounded Agent Queries**: Small dictionaries (`<= 50` records) are preloaded in full; larger dictionaries (`> 50` records) are queried on-demand via server-side `Search`.

## 6. Makefile Commands, Dev Ports, MCP & Git Commit Rules
- **Dev Server Ports**:
  - Standard IDE ports (Rider / WebStorm): Backend `5091`, Frontend `4300`.
  - Dedicated Agent ports: Backend `5191`, Frontend `4400` (`make agent-up`, `make agent-restart`, `make agent-down`). Always use and restart the agent ports after changes so IDE sessions remain undisturbed.
- **JetBrains MCP**: Use Rider MCP (`64482`) and WebStorm MCP (`64542`) when available (`make mcp-check`) for diagnostics and refactorings; always run `make format` before committing.
- **Git Commit Rules**:
  - Write commit messages strictly in **English (`en-US`)** with no markdown code fences and no non-breaking spaces.
  - First line format: `<prefix>: <concise summary>` where `<prefix>` is `add:`, `upd:`, `del:`, `fix:`, `style:`, or `chore:`.
  - Never mix mechanical refactorings (`upd:` with first body line `mechanical`) and logic changes in the same commit. Include `breaking: <what>, <who>, <what to do>` when breaking public contracts.

## 7. Architectural Decisions Record (ADR) — Canonical Invariants
The following architectural decisions are settled and MUST NOT be challenged or reversed in future reviews or audits:
1. **`Air.Api` and `Air.App` Are Strictly Separate Projects**: `Air.Api` is a lightweight headless API host (used standalone for public/external APIs without backoffice UI engines), while `Air.App` layers the backoffice UI engines (`GridService`, `FormService`, `ReportService`, `ScreenService`, `StatusFlowService`) on top. Never merge `Air.Api` and `Air.App`.
2. **Application-Local `App` Module (`DemoApp`, `MyApp`, `BccApp`)**: Each application maintains its own `App` module so application-specific identity/organization fields and rules (e.g., `EmployeeId`) never leak into other applications. Common extraction will only be revisited after 3–4 production applications.
3. **Explicit `public override Feature Feature => ...` in Every Service**: Every service explicitly declares its `Feature` property so developers immediately see which feature and RBAC scope a service belongs to without searching module registrations.
4. **Module Base Services (`*AppService`, `*GridService`, `*FormService`) & Dedicated Service Files**:
   - Every service resides in its own dedicated file (even when minimal) so the entry point is always predictable.
   - If a module needs a grid/lookup over another module's entity with module-specific filtering or columns, it defines its own `*GridService` inside its own module so specificity never leaks into the base module.
   - Module/app context (`AppService app`, `FinAppService app`, etc.) is injected so `app` is always at hand (`app.Can(...)`, `app.Time`, `app.L[...]`) and provides a natural home for module-level shared logic.
   - **Canonical Permission Check**: Always write `app.Can(...)` (e.g., `app.Can(Fin.BankSync.Edit)`) in services rather than `User.Can(...)` or `app.User.Can(...)`.
5. **Separate `*.Migrations.csproj` Assemblies**: EF Core migrations (with multi-thousand-line `*ModelSnapshot.cs` and `*.Designer.cs` files) and `Seeds` MUST live in dedicated `*.Migrations.csproj` projects so they do not bloat `*.Data` compilation or runtime dependencies.
6. **JetBrains Rider File & Namespace Convention**: Follow standard Rider `New -> Class` behavior (`1 type = 1 file`, and folder names `Contracts/` and `Internals/` included in the `namespace`). However, `*.Internals` namespaces MUST NOT be placed in `GlobalUsings.cs`; use a local `using ...Internals;` directive in the specific `.cs` files that need them.
7. **`Air.Mcp` & `Air.Agent` Providers Retained**: `Air.Mcp` is actively maintained for external MCP agent integrations; `agy` is the primary AI provider while other providers remain in place.
