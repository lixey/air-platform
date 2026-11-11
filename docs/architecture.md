# Архітектура ядра: Дані (`Air.Data`) та API/Безпека (`Air.Api`)

Цей документ описує реальну внутрішню будову двох фундаментальних шарів платформи: рівня даних (`Air.Data`, `Air.Data.Postgres`, `Air.Data.SqlServer`) та рівня модульного API-хоста (`Air.Api`).

---

## Частина 1. Рівень даних (`Air.Data`)

Проєкт `Air.Data` (простір імен `Platform.Data.*`) побудований на **EF Core 10** і свідомо не має жодної залежності від ASP.NET Core або вебшару. Завдяки цьому його можна використовувати в CLI-утилітах міграцій (`Demo.DevMigrations`), фонових процесах, інструментах перенесення даних та модульних тестах.

### 1.1. Ієрархія базових сутностей та інтерфейсів (`Platform.Data.Entities`)

Усі сутності в платформі та прикладних модулях будуються з комбінації простих інтерфейсів і базових класів:

| Базовий тип / інтерфейс                             | Ключові властивості                                                                                                                   | Коли використовувати                                                                                                                                                                                            |
| :-------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------ | :-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **`IEntity` / `BaseEntity`**                        | `long Id`, `int Ver` (`IHasVersion`), `DateTimeOffset CreatedAt`, `DateTimeOffset? UpdatedAt` (`IHasTimestamps`)                      | Стандартні бізнес-сутності з автоінкрементним ключем `Id`, оптимістичною конкуренцією (`Ver`) та штампами часу.                                                                                                 |
| **`BaseExplicitIdEntity`**                          | Успадковує `BaseEntity`, але `Id` задається явно (`DatabaseGeneratedOption.None`)                                                     | Системні переліки, мови, кольори, типи/статуси документів (синхронізуються через `CodeRows.SyncAsync`) та імпортовані записи з фіксованими `Id`.                                                                |
| **`BaseDictionaryEntity`**                          | Успадковує `BaseExplicitIdEntity`, `IHasIsActive`: `Handle` (`PascalCase`), `Name`, `Description`, `IsActive`, `SortOrder`, `Comment` | Кодифіковані та користувацькі довідники з ідентифікатором `Handle` і сортуванням.                                                                                                                               |
| **`BaseAppFile`**                                   | `StorageKey`, `ThumbnailKey`, `Store`, `Name`, `ContentType`, `Size`, `Sha256`, `Width`, `Height`, `UploadedById`                     | Таблиці прикріплених файлів і зображень (обслуговуються через `FileGridService`). Сутність-власник із обкладинкою реалізує `IHasCoverFile` (`long? CoverFileId`).                                               |
| **`IHasTenant`**                                    | `long TenantId { get; set; }` (non-nullable)                                                                                          | Сутності, які суворо належать конкретному тенанту (організації).                                                                                                                                                |
| **`IInheritedByChildTenants`**                      | `long? TenantId { get; set; }` (окремий інтерфейс із nullable `TenantId`)                                                             | Довідники та налаштування, де рядки з `TenantId == null` є глобальними (видимими всім тенантам), а рядки з `TenantId != null` належать конкретному тенанту. Звичайний тенант не може змінювати глобальні рядки. |
| **`IReadOnlyEntity` / `[NoAudit]` / `[Sensitive]`** | Маркери поведінки запису та аудиту                                                                                                    | `IReadOnlyEntity` блокує зміни через EF Core; `[NoAudit]` виключає таблицю з `AppPlatformChange`; `[Sensitive]` маскує значення поля в журналі змін.                                                            |

### 1.2. `PlatformDbContext`: асинхронний конвеєр запису, мульти-тенантність та `DbScope`

`PlatformDbContext` автоматизує наскрізні інваріанти роботи з БД:

1. **Тільки асинхронний запис (`SaveChangesAsync`):**
   Синхронні перевантаження `SaveChanges(...)` у `PlatformDbContext` кидають `NotSupportedException`. Усі збереження виконуються виключно через `SaveChangesAsync`, що гарантує коректну роботу стратегій повтору (`IExecutionStrategy`) та асинхронного журналу змін.
2. **Автоматичні глобальні фільтри тенанта та конвенції запису (`EntityWriteConventions`):**
   - Для кожної сутності `IHasTenant` автоматично накладається фільтр `e.TenantId == CurrentTenantId`.
   - Для сутностей `IInheritedByChildTenants` запит повертає рядки поточного тенанта (`TenantId == CurrentTenantId`) **або** глобальні рядки (`TenantId == null`).
   - Для таблиці пам'яті ШІ `AppAgentMemory` фільтр і конвенції запису додатково ізолюють рядки за кортежем `(TenantId, UserId)`: глобальні правила/зерна (`TenantId == null && UserId == null`), спільні правила тенанта (`Kind == AgentMemoryKinds.Rule && UserId == null`) та особисті вподобання/факти користувача (`UserId == CurrentUserId`).
   - При збереженні `EntityWriteConventions` проставляє `TenantId`, оновлює `CreatedAt` / `UpdatedAt`, інкрементує `Ver` (рівно на `+1` у межах одного `MutationRun`) і відхиляє будь-які спроби змінити чужий або глобальний рядок без системного скоупу (`AsyncLocalTenantContext.UseSystemScope()`).
3. **Керування контекстом операції (`DbScope`):**
   `db.Scope` надає легковагові області керування конвеєром збереження:
   - область консолідованої мутації (об'єднує кілька проміжних `SaveChangesAsync` у єдиний запис аудиту та єдиний крок `Ver + 1`);
   - область похідного перерахунку (`IsDerived = true` для каскадного оновлення підсумків чи статусів батьківського документа);
   - тимчасове приглушення аудиту (наприклад, під час ідемнотентного стартового сидингу `CodeRows.SyncAsync`).
4. **Канонічне правило зовнішніх ключів (`DeleteBehavior.Restrict`):**
   За замовчуванням для зовнішніх ключів встановлюється `DeleteBehavior.Restrict` (крім явно налаштованих композицій `Cascade` / `SetNull`), що захищає бізнес-дані від випадкового каскадного видалення.

### 1.3. Модульна ізоляція схем та міграцій (`[ModuleSchema]`, `ModuleOwnershipConvention`)

Згідно з **ADR #8**, кожен модуль має власний самодостатній `*DbContext`, який успадковує контекст базового модуля:
`PlatformDbContext` → `AppDbContext` → `OrgDbContext` → `EcomDbContext`.

- Кожен модуль позначає свій `DbContext` (та/або збірку `*.Data`) атрибутом `[ModuleSchema("Ecom")]` із назвою модуля в `PascalCase`.
- **`ModuleOwnershipConvention`** автоматично визначає модуль-власник кожної сутності за її збіркою (`TEntity.Assembly`). Якщо сутність належить іншому модулю (успадкована від базового `DbContext` або позичена з паралельного модуля через `DbSet<TEntity>`), конвенція автоматично викликає `SetIsTableExcludedFromMigrations(true)` для поточного модуля.
- Історія міграцій зберігається в таблиці `__PlatformMigrations` (`PlatformMigrationHistoryRepository`) із розрізом за модулем, тому міграції кожного модуля генеруються і застосовуються незалежно.

### 1.4. Автоматичний журнал аудиту (`AppPlatformChange`)

`PlatformDbContext` автоматично фіксує всі створення, зміни та видалення сутностей у таблиці `AppPlatformChange`:

- Разом зі знімками полів (`Before` / `After`) записуються `Schema`, `Feature` (визначається через `ModuleCatalog`), `EntityId`, `Operation`, `UserId`, `TenantId`, `ChangedAt` та прапорець похідної зміни `IsDerived`.
- Поля з атрибутом `[Sensitive]` автоматично виключаються/маскуються зі знімків змін.
- Дані бізнес-таблиць та записи `AppPlatformChange` комітяться атомарно в одній транзакції з відкладеним `AcceptAllChanges()`, що забезпечує безпечні повтори при транзієнтних збоях СУБД.

### 1.5. Песимістичні блокування та рушій запитів (`DataProvider<TEntity, TRow>`, `QueryMap`)

- **Песимістичні блокування (`LockAsync`):** `Air.Data.Postgres` використовує `SET LOCAL lock_timeout` та `FOR UPDATE` / транзакційні advisory locks; `Air.Data.SqlServer` — `SET LOCK_TIMEOUT` та `WITH (UPDLOCK, ROWLOCK)` / `sp_getapplock`.
- **`DataProvider<TEntity, TRow>` та `QueryMap<TEntity>`:** транслюють клієнтські `ListRequest` / `GridRequest` (пошук, складні дерева фільтрів `FilterNode`, сортування, пагінацію, серверні `CountAsync` та підсумки колонок) безпосередньо у типізовані вирази EF Core з суворою перевіркою дозволених шляхів сортування та фільтрації.

---

## Частина 2. Безголовий модульний API-хост (`Air.Api`)

Проєкт `Air.Api` (`Platform.Api.*`) відповідає за реєстрацію модулів, стартову валідацію конфігурації, автентифікацію, RBAC, мутації, файли, локалізацію та HTTP-диспетчеризацію сервісів.

### 2.1. Оголошення модуля (`ModuleDefinition<TSelf>`, `Feature`, `PlatformModule<TDefinition>`)

Кожен бізнес-модуль у `<Mod>.Api` описується двома компактними класами (див. еталон у `demo/src/DemoEcom/DemoEcom.Api/`):

1. **Декларація фіч і прав (`<Mod>.cs`)**:
   ```csharp
   [Module(EcomData.ModuleHandle)]
   public sealed class Ecom : ModuleDefinition<Ecom>
   {
     public static readonly Feature CustomerOrder = Feature<EcomCustomerOrderGridService>().AllowAgent();
     public static readonly Feature SalesWorkspace = Feature<EcomSalesWorkspaceScreenService>().ReadOnly().WithoutExport();
     public static readonly Feature SalesReport = Feature<EcomSalesReportService>().ReadOnly().WithoutExport().AllowAgent(AgentAccess.Read);
   }
   ```
   Кожна стандартна `Feature` автоматично формує набір прав (`View`, `MenuView`, `Edit`, `Delete`, `Export`, `All`), а кастомні дії сервісів (`[RequireAction("...")]`) та переходи `StatusFlowService` автоматично додаються до фічі під час реєстрації модуля, після чого фіча заморожується (`Freeze()`). Для тонких обмежень записів використовується `Feature<TService, TAccess>()` разом із `FeatureAccessEvaluator`.
2. **Клас життєвого циклу модуля (`<Mod>Module : PlatformModule<<Mod>>`)**:
   реєструє `DbContext` (`builder.AddPlatformPostgres<EcomDbContext>()` або `AddPlatformSqlServer`), сервіс контексту модуля (`EcomService`), предметні знання ШІ (`AgentMemoryCatalog.RegisterDomainKnowledge(...)`) та навігаційне меню `ModuleMenu`.

### 2.2. Стартова перевірка (`PlatformValidator`) та канонічний RBAC

Під час запуску хоста **`PlatformValidator.Validate`** перевіряє весь `ModuleCatalog` ще до прийому першого HTTP-запиту:

- відсутність дублікатів маршрутів сервісів і ключів фіч, валідність `PascalCase` у всіх `Handle`;
- наявність явних атрибутів прав (`[RequireAction]`, `[RequireRight]`, `[ServiceAction]`) на всіх публічних екшенах і пікерах;
- цілісність переходів і статусів у `StatusFlowService`;
- суворе дотримання вертикальної ізоляції модулів (`ModuleCatalog.EnsureModuleServiceAccessAllowed`) для всіх посилань між сервісами гридів, форм, потоків та мультипанельних екранів (`ScreenService`).

Усі перевірки прав під час виконання працюють за принципом **fail-closed**:

- **`CanView(feature, user)`**: `user.IsAuthenticated && (user.Can(feature.View) || user.Can(feature.Edit))` (маски `*`, `Module.*`, `Module.Feature.*` розпізнаються в `HeldRights.Covers`).
- **Видимість у меню**: `CanView(feature, user) && user.Can(feature.MenuView)`.
- **`CanEdit` / `CanDelete` / `CanExport`**: перевіряють автентифікацію, прапорці фічі (`!IsReadOnly`, `HasDelete`, `HasExport`) та відповідне право користувача; у коді сервісів перевірка завжди записується через `app.Can(Ecom.CustomerOrder.Edit)`.
- **Захист від ескалації привілеїв**: користувач ніколи не може надати іншому користувачеві чи ролі права або тенантний скоуп ширші за власні, змінити `IsRoot`-користувача без `IsRoot`, або видалити останню активну адмін-роль у тенанті.

### 2.3. Автентифікація, сесії, мутації (`MutationRunner`) та JSON-конвертери

- **Двотокенна сесія в `HttpOnly` cookies**: короткоживучий `AccessCookie` + ротований `RefreshCookie` із вікном захисту від конкурентних вкладок, lockout при переборі паролів, захистом від таймінг-атак, миттєвою інвалідацією `IUserAuthCache` та обов'язковим заголовком `X-Platform-Client` на всіх мутуючих запитах.
- **`MutationRunner.RunIdempotentMutationAsync`**: виконує кожну мутацію в транзакції та `DbScope`, забезпечує ідемпотентність за `IdempotencyKey`, перехоплює конфлікти версій (`DbUpdateConcurrencyException` -> `MutationOutcome.Conflict`) і бізнес-відмови (`MutationOutcome.Refused`).
- **Стійкі JSON-конвертери (`PlatformJson`)**:
  - `ResilientNumberConverterFactory` (`ResilientNumberConverter<T>`) безпечно перетворює порожній рядок `""` або `null` у `null` для nullable чисел (`long?`, `int?`, `decimal?`, щоб необов'язкові FK ніколи не ставали `0`) та у `0` для non-nullable числових полів;
  - `FlexibleDateTimeConverter` та `FlexibleDateTimeOffsetConverter` коректно розбирають ISO-дати та локальні рядки дат без зсуву календарного дня й повертають контрольовану помилку формату замість HTTP 500.
