# air-platform: Agent Instructions

## Core Principles (Radical Simplicity & Speed)
- **YAGNI & Anti-Bloat**: The best code is the code never written. Solve problems with the least new code. No speculative abstractions, wrappers, or "future-proofing".
- **Zero Premature Tests**: Do NOT write unit, integration, or architecture tests during engine assembly unless explicitly asked. Verify correctness via build (`dotnet build`) and direct execution. Tests belong at the end.
- **No Documentation Overhead**: Do not maintain markdown documentation, specs (no OpenSpec), or heavy changelogs. Zero token waste on docs during development.
- **Token Efficiency & Context Economy**:
  - Keep responses concise, direct, and actionable.
  - Make minimal, targeted diffs instead of rewriting large files.
  - Do not recite back entire files or explain basic programming concepts.

## Target Architecture
- **Tech Stack**: .NET 10, C# 13, Angular 19+, Tailwind CSS v4, Nx workspace.
- **Backend Projects (`src/`)**:
  - `Air.Data`: EF Core 10 base, tenancy, entities, querying. Strictly independent of ASP.NET Core/Web so modules can inherit it cleanly.
  - `Air.App`: Merged Minimal API host, module lifecycle, screens, grids, forms, UI models, reflection contract exporter.
  - `Air.Data.Postgres`: PostgreSQL EF Core provider.
  - `Air.UiContracts`: Fast CLI tool in `src/` to export TypeScript contracts (`contracts.ts`) and texts (`ui-texts.json`).
- **Frontend (`air-ui/`)**:
  - Angular standalone components, Signals (`signal`, `computed`, `linkedSignal`), Signal Forms.
  - Dynamic component resolution via `UI_COMPONENTS` mapped by `kind`.
  - Semantic color tokens, Tailwind v4, WCAG AA.
- **Demo (`demo/`)**:
  - Reference application in Nx (`apps/demo` + `libs/air-ui`) and modular backend (`Demo.Api`).

## Workflow Rules
- **Interactive Alignment**: Always consult the user with recommendations before porting or adapting components from `/Volumes/Pro/Ai/air/air-space`.
- **Consistency**: Keep one uniform shape across components, grids, forms, and models.
- **PascalCase `Handle` Rule**: Every `Handle` (on modules, dictionary/code entities, menu items, etc.) MUST always be a `PascalCase` identifier (`^[A-Z][a-zA-Z0-9]*$`, e.g. `App`, `Fin`, `En`, `Ua`, `Uah`, `Usd`, `Slate`). Whenever an external or standard representation in another format is needed (e.g. ISO currency code `UAH`, culture code `uk-UA`), use a dedicated property such as `Code`.
- **Form Overlay Presentation Rule (`Popover` vs `Drawer`)**:
  - **Default (`Popover`)**: All standard and compact forms use `OverlayModel.Popover(handle)` by default (defined in the base `FormService` / `AppFormService` / `FinFormService`). Child form services must NOT redundantly override `Presentation` when using `Popover`.
  - **Composite & Large Forms (`Drawer`)**: `OverlayModel.Drawer(handle)` is used ONLY for large multi-section/multi-tab forms and is **strictly mandatory** whenever a form has subordinate child tables (`elements.AddChild<TGrid>()` without `.AsFiles()`).
- **Fast Feedback Loop**: Check compilation and run minimal checks.
