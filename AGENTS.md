# air-platform: Agent Instructions

## 1. Communication & Workflow Protocol
- **Language Matching**: Always respond in the exact same language the user used in their message.
- **Task Restatement First**: Upon receiving a task, first restate it in clear, coherent prose (broken into numbered points if multi-part) and expand on any implicit details you notice are needed, so the user can see you understood it accurately before reporting actions.
- **Plan & Approval for Complex Tasks**: If a task requires extended analysis, touches core architecture, or involves design trade-offs, present a compact numbered implementation plan, impact analysis, and critical points first, and wait for explicit user approval before writing code.
- **Platform Evolution Over Workarounds**: The Air Platform is actively evolving. Never work around or fight platform limitations with hacks — instead, propose clean, universal architectural improvements to the platform itself with thorough justification.
- **Format, Verify, Commit & Push Automatically**: After every working change, run `make format` (`dotnet format` + `prettier`), verify zero absolute paths (`make check-paths`) and a clean build (`make build`), then immediately commit and `git push` without asking.

## 2. Core Principles (Radical Simplicity & Speed)
- **YAGNI & Anti-Bloat**: The best code is the code never written. Solve problems with the least new code. No speculative abstractions, wrappers, or "future-proofing".
- **No God-Classes**: Keep files small and single-purpose (~150–350 lines). Split large services by responsibility immediately.
- **Universal Metadata-Driven Design**: Platform code (`src/`, `air-ui/`) must never contain application-specific logic (`Demo`, `MyFin`). All capabilities (including the built-in AI assistant) must be driven universally by declarative metadata on `Feature`, `FormService` (`Elements`, `AsReference`, `PatchOnSelect`, `AddChild`), `GridService`, and `Rights`.
- **Zero Absolute Paths**: Never commit machine-specific or user home absolute filesystem paths in any tracked file. Always use workspace-relative paths.
- **Zero Premature Tests & Minimal Docs**: Do not write unit/integration tests or external specs (no OpenSpec) unless explicitly asked. Maintain only concise, practical guides in `docs/`.
- **Token Efficiency & Context Economy**: Keep responses concise, direct, and actionable. Make minimal, targeted diffs instead of rewriting large files.

## 3. Target Architecture
- **Tech Stack**: .NET 10, C# 14, Angular 22+ (Standalone components, Signals `signal` / `computed` / `linkedSignal`, Signal Forms), Tailwind CSS v4, Nx workspace.
- **Backend Projects (`src/`)**:
  - `Air.Data` / `Air.Data.Postgres`: EF Core 10 base, multi-tenancy, entities, querying. Strictly independent of ASP.NET Core/Web.
  - `Air.App`: Minimal API host, module lifecycle, screens, grids, forms, UI models, localization, reflection contract exporter.
  - `Air.Mcp` / `Air.Agent`: Built-in AI assistant (`Feature.AllowAgent()`), operating in-process via `FormService`/`GridService` and `<air_proposal>` confirmation cards, isolated by `(TenantId, UserId)` and user permissions.
  - `Air.UiContracts`: Fast CLI tool in `src/` to export TypeScript contracts (`contracts.ts`) and UI texts (`ui-texts.json`).
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

## 5. UI & Form Conventions
- **Reuse Platform Primitives**: Always use `air-ui` components, services, and tokens (`ui-nav`, `ui-btn`, `SelectService`, `OverlayService`, `fieldClasses`, `UiIcon` + `ICON_SET`, semantic classes `bg-surface`, `bg-field`, `border-line`, `border-field-edge`) instead of custom native controls. Use `flex items-center justify-center` on `<i [icon]="...">` tags.
- **Responsive Panels**: Respect `LayoutPanelsService` (`panels.isNarrow()`). On narrow screens, open side panels inside a modal dialog (`presentation: 'dialog'`).
- **Form Overlay Presentation Rule (`Popover` vs `Drawer`)**:
  - **Default (`Popover`)**: All standard and compact forms use `OverlayModel.Popover(handle)` by default in the base `FormService`. Child form services must NOT redundantly override `Presentation` when using `Popover`.
  - **Composite & Large Forms (`Drawer`)**: `OverlayModel.Drawer(handle)` is used ONLY for large multi-section/multi-tab forms and is **strictly mandatory** whenever a form has subordinate child data tables (`elements.AddChild<TGrid>()` without `.AsFiles()`).
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
