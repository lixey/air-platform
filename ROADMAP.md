# Air Platform (Gen 3) — Architectural Audit & Evolution Roadmap

## 1. Executive Summary & 3-Generation Platform Comparison

This document summarizes an exhaustive, file-by-file architectural audit across all three platform generations and their applications:
1. **Generation 1 (`Zen Platform`)**: `zen-workspace` (`Zen.Core`, `Zen.Data`, `Zen.Api`, `Zen.App`, `Zen.Automation`, `Zen.Mcp`, `Zen.NovaPoshtaUa`) + production apps (`ad-bcc`, `bcc-commerce`).
2. **Generation 2 (`Air Space`)**: `air-space` (`Air.Data`, `Air.Api`, `Air.App`, `Air.Biz`, `air-ui`) + `autoklad-workspace`.
3. **Generation 3 (`Air Platform` — Target)**: `air-platform` (`Air.Data`, `Air.Data.Postgres`, `Air.Data.SqlServer`, `Air.App`, `Air.Mcp`, `Air.Agent`, `Air.UiContracts`, `air-ui`) + `../my-port` (first production target) and `../ak-bcc` (Autoklad ERP/E-Commerce migration).

### 1.1 Comparative Scorecard Across Generations

| Architectural Dimension | Gen 1: `Zen Platform` | Gen 2: `Air Space` | Gen 3: `Air Platform` (Current) | Target Gen 3 (`Air Platform` vNext) |
| :--- | :--- | :--- | :--- | :--- |
| **DTO & Contract Architecture** | God-DTOs (`TableColumnDto` 40+ props, `BaseFormElementDto` 50+ props) wrapped in `ResultDto<TData, TMeta>` | Clean polymorphic `[JsonPolymorphic]` slot models (`PanelModel`, `CellModel`, `FormElementModel`, `NavElementModel`) + separated `ScreenDto` vs `GridPageDto<TRow>` / `FormDataDto` | Same polymorphic slot models + `ReportContracts` + `AgentContracts`, **but** polluted `Air.Data.Page<TRow>` with UI fields, mixed `Dto`/`Model` suffixes (`OverlayModelDto`, `AgentChatRequestDto`), and flattened TS inheritance | Pure `Air.Data.Page<TRow>` + `GridPageDto<TRow>`, strict `*Model` / `*Dto` / `*Request` naming rules, unified `MutationResultDto`, full TS inheritance & app-level contract augmentation |
| **TypeScript Contract Exporter** | Scattered `[ExportTsClass]` / NSwag | Reflection-based `UiContractsExport.Platform()` + `Application(modules)`, but required full ASP.NET Core host boot | Millisecond zero-boot CLI (`Air.UiContracts`), **but** hardcodes TS strings for `GridPageDto`, `ActionInputDto`, `*ServiceEndpoints` and dropped app-module contract generation | Millisecond zero-boot CLI + full reflection of base classes, generic contracts, service endpoint maps, and app-level `PanelKinds`/`CellKinds`/`FormElementKinds` augmentations |
| **Service & Routing Universality** | `IEndpoints` + `EndpointsBase` + `[MapPost]` discovery | Universal `IEndpoints` + `IFeatureService` + `EndpointDiscovery` + `RequestDelegateFactory` compilation at startup | Regressed to a 4-branch `if/else` in `ServiceEndpoints.cs` (`IGridService`, `IFormService`, `IReportService`, `IScreenService`); custom services & custom methods on screens/reports return `404` | Universal `IPlatformService` / `PlatformService` base for Grids, Forms, Reports, Screens, and Custom RPC/Domain Services with automatic method routing & parameter binding |
| **Permissions & RBAC (`Feature` & `RightsTree`)** | Manual `ZenEntity.WithAccessObjects(...)` numeric IDs + runtime `Can(Module, Entity, Method)` | Automatic `CrudConvention` + `ModuleBinding.RequireActions` scanning all endpoints & `StatusFlowService` transitions into `ModuleCatalog.RightsOf(feature)` + `.rights.lock` | Static hardcoded 5 rights in `RightsTree.cs` (`View`, `MenuView`, `Edit`, `Delete`, `Export`); custom actions & `StatusFlowService` transitions missing from Role Editor; `!IsAuthenticated` bypass bug | Fail-closed auth + `CrudConvention` + automatic `RightsTree` discovery of all service endpoints, custom actions, `StatusFlowService` transitions, and `AccessTo` sub-rights |
| **Multi-Tenancy & Security Guards** | Ad-hoc `UnitId` checks | Fail-closed `RequiredTenantId`, `TenantTree` ancestor inheritance (`IInheritedByChildTenants`), `TenantWriteGuard`, `RightsGuard` (root lockout protection) | Fail-open `TenantId == null` filter bug, broken `IInheritedByChildTenants`, incomplete `Added` write guard when `TenantId != 0`, no `RightsGuard` | Fail-closed tenant filters, full `TenantTree` ancestor inheritance, strict `TenantWriteGuard`, `RightsGuard`, and `ISystemDbContextFactory` / `SystemActor` for background jobs |
| **Transactional Mutations & Audit Journal** | Manual `BeginTransactionAsync` in `FormService_Save` | Unified `MutationRunner` with transaction, `Idempotency-Key`, advisory locks, `AfterCommit`, atomic `MutationJournal`, and recursive `ChangeRestore` | No transaction around `FormService.SaveAsync` or `GridService.DeleteAsync`; non-atomic 2-phase `SaveChangesAsync` for `AppPlatformChange` with `catch {}`; broken `ChangeGridService.Restore` on `[KeepsVersion]` | Unified transactional `MutationScope` wrapping `Before*` + entity save + `AppPlatformChange` + `After*` atomically, `AfterCommit` hooks, post-commit warnings, and `[KeepsVersion]`-aware `Restore` |
| **Grid & Form Engine Internals & Performance** | Event-list (`OnSetDto`) + `dynamic` | 95+ files with heavy `GridAddOn` / `GridBesideJoin` ceremony, but single-query `GridBesideRow` & `GridSumsAddOn`, and isolated `GridRun` / `FormRun` | Clean `SetColumns`/`SetElements`/`SetQuery`/`SetNav` developer API, **but** 3,500-line god-classes, N+1 availability/sum/color/multi-ref queries, unauthenticated badge count leak, and stateful `_lastElements` | Keep the simple declarative API while decomposing internal engines into focused ~250-line helpers, batching sums/colors/availability into single queries, and isolating per-request state |
| **Reports & Built-In AI Assistant** | ClickHouse `PivotGridService` (`zen-workspace`); no built-in UI agent | None | First-class `ReportService` (`ReportDataDto`, KPIs, ECharts, tables) and in-process metadata-driven `Air.Agent` (`AllowAgent()`, photo/receipt OCR, duplicate detection, `<air_proposal>` cards) | Add server-side report query/pivot/export helpers, route `AgentService` through `HttpClient` interceptors, and connect `ReportComponent` & `AgentService` to `ScreenMutations` |

---

## Phase 1: Contracts, DTOs & Naming Unification (✅ Completed)

### 1.1 Strict Platform Naming Convention
To eliminate ambiguity across C# and TypeScript:
1. **`*Model` — Polymorphic UI Slot Records Only**:
   - Used **exclusively** for `[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]` slot hierarchies that map to a `UI_COMPONENTS` `kind` on the frontend:
     - `PanelModel` (`GridModel`, `FormModel`, `ReportModel`, `NavModel`, `FormFilesModel`, `FormAskModel`, `FormChildScreenModel`, `GridFiltersOverlayModel`, `OverlayConfirmModel`, `OverlayDirtyModel`)
     - `CellModel` (`GridCell*Model`)
     - `HeaderModel` (`GridHeader*Model`)
     - `FormElementModel` (`Form*Model`)
     - `NavElementModel` (`Nav*Model`, `GridNav*Model`, `FormNav*Model`)
2. **`*Dto` — Non-Polymorphic Wire Data Containers & Responses**:
   - Used for all non-polymorphic data structures returned or embedded on the wire (`ScreenDto`, `OverlayDto`, `OverlayScreenDto`, `PanelLayoutDto`, `PanelNodeDto`, `GridPageDto<TRow>`, `GridColumnDto`, `FormElementDto`, `FormDataDto`, `ReportDataDto`, `MutationResultDto`, etc.).
   - **Never** combine `Model` and `Dto` in the same name (rename `OverlayModelDto` $\to$ `OverlayDto`).
3. **`*Request` — All Wire Request Payloads**:
   - Every endpoint input contract ends with `Request` (`ListRequest`, `GridRequest`, `FormRequest`, `ReportRequest`, `LoginRequest`, `AgentChatRequest`, `AgentResetRequest`).
   - **Never** use `*RequestDto`.
4. **Core `Air.Data.Contracts` Primitives**:
   - Pure data-layer value types in `Air.Data.Contracts` carry domain names without `Dto`/`Model` (`Page<T>`, `Slice<T>`, `Reference`, `PickedValue`, `FilterNode`, `FilterRule`, `FilterGroup`, `SortRule`).
   - `Air.Data.Contracts` must contain **zero** UI/Overlay properties.

### 1.2 Concrete DTO & Contract Refactoring Checklist

| # | Current Contract / Symbol | Target Contract / Symbol | Files Affected | Reason & Impact |
| :--- | :--- | :--- | :--- | :--- |
| **1.1** | `Air.Data.Contracts.Page<TRow>` containing `TopRows`, `BottomRows`, `Dictionary<string, object?>? Forms` | Restore pure `Air.Data.Contracts.Page<TRow>` (`Rows`, `Total`, `Number`, `Size`, `HasMore`) + restore `Air.App.Grid.GridPageDto<TRow> : Page<TRow>` (`TopRows`, `BottomRows`, `Dictionary<string, OverlayDto>? Forms`) | `src/Air.Data/Contracts/Page.cs`, `src/Air.App/Grid/GridContracts.cs`, `src/Air.App/Grid/GridService*.cs` | Removes UI pollution from `Air.Data`, strongly types `Forms` as `Dictionary<string, OverlayDto>?`, and matches `GridPageDto` in `contracts.ts`. |
| **1.2** | `OverlayModelDto` (`src/Air.App/Overlay/OverlayContracts.cs:40`) | **`OverlayDto`** (built by static `OverlayModel` factory) | `src/Air.App/Overlay/OverlayContracts.cs`, `src/Air.App/Contracts/AgentContracts.cs`, `air-ui/src/overlay/*` | Removes the only `ModelDto` double-suffix in the platform (`PanelLayout` produces `PanelLayoutDto`). |
| **1.3** | `AgentChatRequestDto` & `AgentResetRequestDto` (`src/Air.App/Contracts/AgentContracts.cs:126, 149`) | **`AgentChatRequest`** & **`AgentResetRequest`** | `src/Air.App/Contracts/AgentContracts.cs`, `src/Air.Agent/AgentEndpoints.cs`, `air-ui/src/layout/agent.service.ts` | Aligns with all 14 other `*Request` types across `Air.Data` and `Air.App`. |
| **1.4** | Duplicate `MetadataDto : AppMetadataDto` (`src/Air.App/Metadata/MetadataContracts.cs:70`) | Delete `MetadataDto` alias; use **`AppMetadataDto`** everywhere | `src/Air.App/Metadata/MetadataContracts.cs`, `src/Air.App/Host/MetadataEndpoints.cs`, `src/Air.App/TypeScript/ContractExporter.cs` | Eliminates duplicate identical interfaces in C# and `contracts.ts`. |
| **1.5** | `CheckboxOptionDto` (`long Id, string Label`) vs `OptionDto` (`long Id, string Name, string? Color, string? Title`) | Delete `CheckboxOptionDto` and use **`OptionDto`**; change `CheckboxTreeNodeDto.Label` $\to$ **`Name`** | `src/Air.App/Form/FormContracts.cs`, `src/Air.App/Form/Elements.cs`, `air-ui/src/form/element/form-checkbox.component.ts` | Unifies option items across selects, radios, checkboxes, and multi-selects on `{ id, name, color?, title? }`. |
| **1.6** | Hardcoded TS strings for `ActionInputDto` and `GridActionInputRequest` in `ContractExporter.cs:145-154` | Add real `[UiContract]` C# classes **`ActionInputDto`** and **`GridActionInputRequest : GridRequest`** | `src/Air.App/Grid/GridContracts.cs`, `src/Air.App/Host/ServiceEndpoints.cs`, `src/Air.App/TypeScript/ContractExporter.cs` | Replaces anonymous `new { value = ... }` and magic `req.Data["method"] ?? req.Column` in `ServiceEndpoints.cs` with typed C# contracts. |
| **1.7** | Duplicated `Outcome` / `Warnings` on `GridDeleteDto`, `GridCellDto`, `GridActionDto`, `FormActionDto`, `FormSaveDto`, `FormDeleteDto` + anonymous `{ outcome, title, messages }` on 404/409/422 | Introduce base **`MutationResultDto`** (`Outcome`, `Title`, `Warnings`) inherited by all mutation DTOs and returned consistently on both `200 OK` and `404`/`409`/`422` | `src/Air.App/Contracts/MessageContracts.cs`, `src/Air.App/Grid/GridContracts.cs`, `src/Air.App/Form/FormContracts.cs`, `src/Air.App/Host/PlatformEndpoints.cs`, `src/Air.App/Host/ServiceEndpoints.cs` | Guarantees a single predictable mutation response contract for both success (including post-commit warnings) and refusal/conflict errors. |
| **1.8** | Flattened base class properties & hardcoded `*ServiceEndpoints` in `ContractExporter.cs` | Teach `ContractExporter.cs` to emit `extends <BaseType>` for `[UiContract]` base classes, reflect generic `[UiContract]` classes (`Page<TRow>`, `GridPageDto<TRow>`), and support `--app-assembly` module scanning | `src/Air.App/TypeScript/ContractExporter.cs`, `src/Air.UiContracts/Program.cs` | Eliminates ~250 lines of duplicated fields in `contracts.ts`, exports `ChangeGridServiceEndpoints` + `AgentEndpoints`, and generates app-specific `declare module '@air-ui/core/generated/contracts'` augmentations for `my-port` and `ak-bcc`. |

---

## Phase 2: Universal Service Architecture & Declarative Rights (✅ Completed)

### 2.1 Unified `IPlatformService` & `PlatformService` Base Class
Currently, `ServiceEndpoints.cs` hardcodes a 4-branch `if/else` (`IGridService`, `IFormService`, `IReportService`, `IScreenService`), returning `404 NotFound` for any custom service and any custom method on `IScreenService` or `IReportService`.

1. **Introduce `IPlatformService` and `PlatformService` (`src/Air.App/Modules/PlatformService.cs`)**:
   - `IPlatformService` exposes `Feature Feature { get; }` and `void AttachPlatformApp(PlatformAppService app);`.
   - `PlatformService` provides `Feature`, `PlatformApp`, `User` (`IUserContext`), `L` (`Localizer.Instance.For(Feature.Module)`), and `Services` (`IServiceProvider`).
   - `ScreenService`, `ScreenService<TData>`, and `ReportService` inherit from `PlatformService`.
   - `GridService<TEntity, TRow>` and `FormService<TEntity, TInput>` implement `IPlatformService` with **`public abstract Feature Feature { get; }`** (eliminating the virtual empty `new Feature()` security hole).
2. **Add `ScreenService<TData>` (`src/Air.App/Screens/ScreenServiceOfT.cs`)**:
   - Strongly types screen payload `protected abstract Task<TData> LoadAsync(CancellationToken ct);` (or `LoadAsync(TRequest req, CancellationToken ct)`) and exports `TData` via `ContractExporter`.
3. **Universal Custom Method Dispatch in `ServiceEndpoints.cs`**:
   - Any public instance method on **any** `IPlatformService` (`PlatformService`, `ScreenService`, `ReportService`, `GridService`, `FormService`) is automatically routable at `POST /api/{module}/{service}/{method-kebab}`.
   - Unified parameter binder supports `CancellationToken`, `HttpContext`, `IFormFile` / `IFormFileCollection`, any JSON request DTO, or `long id`.
4. **Form Custom Actions with `.Ask<TInput>()` (`FormService.cs` & `ServiceEndpoints.cs`)**:
   - Add `nav.AddAction<TRequest>(...).Ask(input => ...).Default(...)` to `FormService.Nav` (matching `GridService.Nav`) and support `get-action-input/{subMethod}` on `IFormService`.
5. **Migrate `FinBankSyncEndpoints.cs` in `my-port` to `FinBankSyncService : PlatformService`**:
   - Convert raw Minimal API routes in `my-port` into a first-class module service with declarative `Feature` rights (`Fin.BankSync`).

### 2.2 Universal Rights Convention & Automatic `RightsTree` Discovery
1. **Expand `[RequireAction]` & `[RequireRight<TService>]` (`src/Air.App/Security/RequireRightAttribute.cs`)**:
   - Support both standard `EndpointAction` (`View`, `Edit`, `Delete`, `Export`) and custom action names (`[RequireAction("Sync")]`, `[RequireRight<FinBankSyncService>("Sync")]`), plus `[AllowAnyAuthenticatedUser]` and `[AllowAnonymous]`.
2. **Standard `CrudConvention` for All `IPlatformService` Methods**:
   - `GetData`, `GetOne`, `GetCount`, `FileMetadata`, `GetInfo`, `DownloadAll` $\to$ `{Module}.{Feature}.view`
   - `Save`, `SaveCell`, `Create`, `Update`, `Upload`, `SetCover`, `Move`, `SetInfo` $\to$ `{Module}.{Feature}.edit`
   - `Delete` $\to$ `{Module}.{Feature}.delete`
   - `Export`, `GetExport` $\to$ `{Module}.{Feature}.export`
   - Any other public endpoint method `MethodName` $\to$ `{Module}.{Feature}.{MethodName}` (unless overridden by `[RequireAction]` / `[RequireRight]`).
3. **Automatic `RightsTree.Build` Discovery (`src/Air.App/Auth/RightsTree.cs`)**:
   - Inspect all services and `StatusFlowService` classes bound to each `Feature`:
     - Only include `MenuView` if the feature is in `module.Menu`.
     - Only include `Export` if the feature has an export-capable `GridService` or `Export` endpoint.
     - Automatically include all custom action methods declared on the feature's `GridService`, `FormService`, `ScreenService`, `ReportService`, or `PlatformService` (`Restore`, `Duplicate`, `SyncNow`, etc.).
     - Automatically include all `StatusFlowService` transitions (`Confirm`, `TakeToOrder`, `Cancel`, etc.) so administrators can grant/revoke individual workflow transitions in the Role Editor UI.

---

## Phase 3: Security, Multi-Tenancy & Transactional Data Integrity (✅ Completed)

1. **Close Unauthenticated Access Bypass (`Priority 0`)**:
   - In `ServiceEndpoints.cs` (`CanView`, `CanExport`, `IsCustomActionPermitted`), `GridService.cs` (`IsReadOnly`, `CanDelete`), `FormService.cs` (`IsReadOnly`, `CanDelete`), `FileGridService.cs`, and `ChangeGridService.cs`:
     - Replace `User == null || !User.IsAuthenticated` returning `true` with strict authentication enforcement: unauthenticated HTTP requests to protected endpoints must return **`401 Unauthorized`**, and `IsReadOnly` / `CanDelete` must return `true` (read-only) / `false` (cannot delete) when unauthenticated.
   - In `HttpTenantContext.cs:25`, remove the `return 1;` fallback for unauthenticated requests when no tenant claim is present.
2. **Restore Fail-Closed Multi-Tenancy, `TenantTree` Inheritance & `TenantWriteGuard` (`PlatformDbContext.cs`)**:
   - Fix `OnModelCreating` tenant query filters so `TenantContext.TenantId == null` fails closed unless an explicit system scope (`AsyncLocalTenantContext.BeginSystemScope()`) is active.
   - Restore `TenantTree` / `TenantReach` so `IInheritedByChildTenants` entities owned by parent/ancestor tenants are visible to child tenants.
   - Fix `ApplyPlatformConventions` on `EntityState.Added` so if `tenantEntity.TenantId != 0 && tenantEntity.TenantId != currentTenantId` (and not in child-tenant reach), it throws `InvalidOperationException` instead of silently allowing cross-tenant inserts; validate `OriginalValue` on `Modified` and `Deleted`.
   - Restore `RightsGuard` on role/assignment mutations (preventing a user from granting rights they do not hold or locking out the last root administrator).
3. **Atomic Transactional `MutationScope` & Audit Journal (`PlatformDbContext.cs`, `FormService.Save.cs`, `GridService.Mutations.cs`)**:
   - Wrap `FormService.SaveAsync`, `GridService.DeleteAsync`, and `GridService.ExecuteBatchAsync` in a single database transaction (`MutationScope` with EF Core `CreateExecutionStrategy()`).
   - If any `AfterSave` or `AfterDelete` hook adds a refusal error or throws, **roll back the entire transaction** so partial entity writes never persist on a `422 Refused` response.
   - Commit `AppPlatformChange` audit rows inside the same transaction, caching `[Sensitive]` and `[KeepsVersion]` property metadata once per entity CLR type.
   - Fix `ChangeGridService.Restore` so `[KeepsVersion]` properties (`StatusId`) only block restoration if a newer journal entry modified that record after the target change.

---

## Phase 4: Core Engine Performance, Decomposition & `air-ui` Cleanup

### 4.1 Backend Query & Performance Fixes (`GridService`, `FormService`, `StatusFlowService`, `Air.Data`)
1. **Fix Badge Count Leak & Duplicate Counting (`GridService.Screen.cs:147-218`)**:
   - Apply `RestrictedQuery()` (`SetUserRestrictions`) to all `NamedFilter` badge counts and eliminate duplicate badge queries between `BuildScreenAsync` and `GetCountAsync`.
2. **Eliminate N+1 Availability, Sum, Color & Multi-Reference Queries**:
   - **Row Availability (`GridService.Query.cs:266-348`, `GridService.Mutations.cs:454`, `StatusFlowService.cs:550`)**: Batch availability checks over `rowIds` in a single query per condition (instead of N×M per-record `AnyAsync` queries).
   - **Column Sums (`GridService.Query.cs:481-552`)**: Combine all `.Sum()` columns into a single `GroupBy(_ => 1)` aggregate query, and add `.Sum(predicate)` + dynamic sum color support so `FinTransactionGridService` in `my-port` does not need a second `base.GetDataAsync` query.
   - **Multi-Select References (`FormService.Screen.cs:1033-1047`)**: Replace the per-ID `foreach` `FindAsync` loop with a single `Where(e => keys.Contains(e.Id))` batch query.
   - **Async `ParentRecordAsync<TParent>` (`GridService.cs:126`, `FormService.cs:52`)**: Remove the synchronous `.FirstOrDefault()` and the eager `.Include()` loop over all navigation properties.
3. **Cache Compiled Expressions & Metadata**:
   - Cache `Columns` / `Query` per request on `GridService` (instead of calling `SetColumns`/`SetQuery` 4 times per request).
   - Cache compiled delegates (`LabelSelector`, `ColorSelector`, `StatusMember`, `FormRules`) per service/entity type.
   - Replace `new Task ExecuteAsync` reflection in `StatusFlowService.cs:358` with `virtual`/`override`, and use the declared `parentKey` expression instead of string-suffix guessing (`StatusFlowService.cs:634`).
   - Fix `FormService.Screen.cs:252` so only the declared `statusFlow.StatusMember` is bound as the status field, never arbitrary fields ending in `"statusId"`.
4. **Remove All Hardcoded App/Demo Strings & Silent `catch {}` Blocks**:
   - Remove `"Ecom: "` (`GridService.Screen.cs:534`, `FormService.Screen.cs:790`), `"demo/"` (`PlatformModule.cs:148, 162`), `["App", "Org", "Ecom", "Doc"]` (`ServicePathResolver.cs:15`), and `"Легасі UI: ..."` (`PlatformResource` localization key instead).
   - Decompose `GridService` (3,571 lines) and `FormService` (3,418 lines) into clean, single-responsibility partial/internal files (~200–350 lines each).

### 4.2 Frontend (`air-ui`) Architecture & Hygiene Fixes
1. **Route `AgentService` Through `HttpClient` / `Api` & `ScreenMutations` (`air-ui/src/layout/agent.service.ts`)**:
   - Replace raw `fetch()` for `status`, `upload`, `confirm`, `reset` with `Api` / `HttpClient` so `credentialsInterceptor` (`X-Platform-Time-Zone`) and `sessionRefreshInterceptor` (auto-refreshing expired JWTs on `401`) work automatically.
   - Add `X-Platform-Time-Zone` and `401` token refresh retry to the SSE `chat` stream.
   - Remove static `GridService.liveGrids` (`grid.service.ts:95`) and connect `AgentService` to the reactive mutation bus so closing an unedited record overlay never reloads all grids.
2. **Make `ReportComponent` 100% Metadata-Driven (`air-ui/src/report/report.component.ts`)**:
   - Remove hardcoded `'year'`, `'all'`, `'from'`, `'to'` filter keys; declare date-range and filter reset behavior in `ReportFilterGroupDto`.
   - Honor `kpi.currency`, `kpi.isCount`, and `col.kind === 'money'` / `col.currency` in `ReportComponent` and `AgentKpiBlockComponent` / `AgentTableBlockComponent`.
   - Subscribe `ReportComponent` to `ScreenMutations` and `SCREEN_LIFETIME`.
3. **Complete or Clean Up `GridCellPicker` & Multi-Field Custom Form Elements**:
   - Fix `canonicalJson` in `form-value.ts` to track dirty state on secondary paths patched via `form.setFieldValue(path, val)`.
   - Convert `Columns.PickedBy` method name to kebab-case and wire `GridCellPicker` into `GridCellEdits` (or remove the stub until needed).

---

## Phase 5: High-Productivity Enterprise Primitives for `my-port` & `ak-bcc`

1. **Declarative Many-to-Many Join Binding (`.AsReferences<TGrid, TJoin>(...)`)**:
   - Automatically load, diff, insert, delete, and clone join-table rows (e.g., `FinTransactionTag` in `my-port`) inside `FormService` without `[NotMapped]` properties or manual `AfterLoad`/`AfterSave`/`BeforeDelete` boilerplate.
2. **Declarative Form Dynamics (`VisibleWhen`, `DisabledWhen`, `RequiredWhen`, Server `OnChange`, Draft Child Lines `AddLines`)**:
   - Client-evaluated visibility/disabled/required predicates on `Elements` (needed for delivery mode fields in `ak-bcc` `EcomOrderFormService` and transfer/expense fields in `my-port`).
   - Server-side `OnChange` field recomputation (extending `patch-draft` to arbitrary fields like VAT/currency/contract in `ak-bcc`).
   - Creating a new parent document together with its child line items in a single save (`AddLines`).
3. **Cross-Feature `StatusFlowService` Binding & Post-Commit Warnings**:
   - Allow secondary operational grids (`EcomLogisticsGridService` in `ak-bcc`) to bind transitions directly from `EcomOrderLineFlowService` without wrapper boilerplate.
   - Allow `MutationContext.Warn(...)` / `Info(...)` to return toast warnings alongside `MutationOutcome.Committed` (needed by `FinBankSyncGridService` and `ak-bcc` order workflows).
4. **File Upload Hooks & Structured Excel/CSV Import Pipeline**:
   - Add `AfterUploadAsync(TFile entity, ReadOnlyMemory<byte> content, CancellationToken ct)` to `FileGridService<TFile>` (eliminating double stream buffering in `my-port`'s `FinBankSyncFileGridService`).
   - Add declarative Excel/CSV import mapping & preview for `ak-bcc` supplier invoices (`ImportXLS`), price feeds, and carrier registries.
5. **Built-In Background Job / Cron Scheduler + `System` Actor & Webhook Endpoints**:
   - Built-in recurring job registration on `PlatformModule` running under a scoped `System` tenant/user context (replacing custom `FinBackgroundWorker` in `my-port` and powering `ak-bcc`'s 20 scheduled jobs).
   - Declarative `[WebhookEndpoint]` / API-key guard on `PlatformService` methods for external callbacks (Monobank/Privat24 push, NovaPay, website orders, telephony).
6. **Printable HTML/PDF Document Templates & `Air.NovaPoshtaUa`**:
   - Server-side printable document rendering (`FormService` / `GridService` print actions) for `ak-bcc`'s 33 printable documents (invoices, waybills, warranty cards, receipts).
   - Port `Zen.NovaPoshtaUa` to `Air.NovaPoshtaUa` for `ak-bcc` waybill (TTN) lifecycle.
