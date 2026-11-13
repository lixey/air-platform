# Архітектура ядра: Дані (`Air.Data`) та API/Безпека (`Air.Api`)

Цей документ описує реальну внутрішню будову двох фундаментальних шарів платформи: рівня даних (`Air.Data`, `Air.Data.Postgres`, `Air.Data.SqlServer`) та рівня модульного API-хоста (`Air.Api`).

---

## Частина 1. Рівень даних (`Air.Data`)

Проєкт `Air.Data` (простір імен `Platform.Data.*`) побудований на **EF Core 10** і свідомо не має жодної залежності від ASP.NET Core або вебшару. Завдяки цьому його можна використовувати в CLI-утилітах міграцій (`Demo.DevMigrations`), фонових процесах, інструментах перенесення даних та модульних тестах.

### 1.1. Ієрархія базових сутностей та інтерфейсів (`Platform.Data.Entities`)

Усі сутності в платформі та прикладних модулях будуються з комбінації простих інтерфейсів і базових класів:

| Базовий тип / інтерфейс                             | Ключові властивості                                                                                                                   | Коли використовувати                                                                                                                                                                                                                                                                                                   |
| :-------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------ | :--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **`IEntity` / `BaseEntity`**                        | `long Id`, `int Ver` (`IHasVersion`), `DateTimeOffset CreatedAt`, `DateTimeOffset? UpdatedAt` (`IHasTimestamps`)                      | Стандартні бізнес-сутності з автоінкрементним ключем `Id`, оптимістичною конкуренцією (`Ver`) та штампами часу.                                                                                                                                                                                                        |
| **`BaseExplicitIdEntity`**                          | Успадковує `BaseEntity`, але `Id` задається явно (`DatabaseGeneratedOption.None`)                                                     | Системні переліки, мови, кольори, типи/статуси документів (синхронізуються через `CodeRows.SyncAsync`) та імпортовані записи з фіксованими `Id`.                                                                                                                                                                       |
| **`BaseDictionaryEntity`**                          | Успадковує `BaseExplicitIdEntity`, `IHasIsActive`: `Handle` (`PascalCase`), `Name`, `Description`, `IsActive`, `SortOrder`, `Comment` | Кодифіковані та користувацькі довідники з ідентифікатором `Handle` і сортуванням.                                                                                                                                                                                                                                      |
| **`BaseAppFile`**                                   | `StorageKey`, `ThumbnailKey`, `Store`, `Name`, `ContentType`, `Size`, `Sha256`, `Width`, `Height`, `UploadedById`                     | Таблиці прикріплених файлів і зображень (обслуговуються через `FileGridService`). Сутність-власник із обкладинкою реалізує `IHasCoverFile` (`long? CoverFileId`).                                                                                                                                                      |
| **`IHasTenant`**                                    | `long TenantId { get; set; }` (non-nullable)                                                                                          | Сутності, які суворо належать конкретному тенанту (організації).                                                                                                                                                                                                                                                       |
| **`IHasOptionalTenant`**                            | `long? TenantId { get; set; }` (окремий інтерфейс із nullable `TenantId`)                                                             | Довідники та налаштування, де рядки з `TenantId == null` є глобальними (видимими всім тенантам), а рядки з `TenantId != null` належать конкретному тенанту. Звичайний тенант не може змінювати глобальні рядки.                                                                                                        |
| **`IReadOnlyEntity` / `[NoAudit]` / `[Sensitive]`** | Маркери поведінки запису та аудиту                                                                                                    | `IReadOnlyEntity` блокує зміни через EF Core; `[NoAudit]` виключає технічну таблицю/поле з `AppPlatformChange` (ніколи не вживається для бізнес-статусів чи сум — для похідних перерахунків використовується `RunDerivedRecalculationAsync`); `[Sensitive]` блокує проєкції/фільтри та маскує значення в журналі змін. |

### 1.2. `PlatformDbContext`: fail-closed `ITenantContext`, `DbScope` та асинхронний конвеєр запису

`PlatformDbContext` автоматизує наскрізні інваріанти роботи з БД:

1. **Тільки асинхронний запис (`SaveChangesAsync`):**
   Синхронні перевантаження `SaveChanges(...)` у `PlatformDbContext` кидають `NotSupportedException`. Усі збереження виконуються виключно через `SaveChangesAsync`, що гарантує коректну роботу стратегій повтору (`IExecutionStrategy`) та атомарного запису журналу змін.
2. **Fail-closed контекст тенанта (`ITenantContext`, `AsyncLocalTenantContext`, `HttpTenantContext`) та `EntityWriteConventions`:**
   - За замовчуванням `ITenantContext.IsSystemScope == false` завжди — у тому числі коли `TenantId == null` (неавтентифікований запит або фонова задача `IPlatformJob`). Без активного тенанта глобальні фільтри `IHasTenant` повертають порожній результат, а крос-тенантне читання або запис глобальних рядків дозволяються лише всередині явного `using var _ = AsyncLocalTenantContext.UseSystemScope(...)`.
   - Для кожної сутності `IHasTenant` автоматично накладається фільтр `TenantContext.IsSystemScope || e.TenantId == TenantContext.TenantId`.
   - Для сутностей `IHasOptionalTenant` запит повертає рядки поточного тенанта (`TenantId == TenantContext.TenantId`) **або** глобальні рядки (`TenantId == null`), при цьому звичайний тенант ніколи не може змінити чи видалити глобальний рядок (`TenantId == null`).
   - Для таблиці пам'яті ШІ `AppAgentMemory` фільтр і конвенції запису додатково ізолюють рядки за кортежем `(TenantId, UserId)`: глобальні правила/зерна (`TenantId == null && UserId == null`), спільні правила тенанта (`Kind == AgentMemoryKinds.Rule && UserId == null`) та особисті вподобання/факти користувача (`UserId == CurrentUserId`).
   - При збереженні `EntityWriteConventions` проставляє `TenantId`, оновлює `CreatedAt` / `UpdatedAt`, інкрементує `Ver` (рівно на `+1` у межах одного `MutationRun`) і відхиляє будь-які спроби змінити чужий або глобальний рядок без явного `UseSystemScope()`.
3. **Резолвер контексту запиту (`DbScope`) та області мутацій на `PlatformDbContext`:**
   - **`DbScope` (`Platform.Data.DbScope`)** — request-scoped резолвер (`Bind(contextType)` / `Resolve<TContext>(services)`), який прив'язує HTTP-запит або мутацію до листового `PlatformDbContext` цільового модуля. Завдяки цьому базові та листові сервіси працюють з єдиним екземпляром `DbContext` і спільним `ChangeTracker`, тоді як паралельні листові модулі в одному хості залишаються повністю ізольованими.
   - **`db.BeginMutationRun()` + `db.FlushMutationJournalAsync()`** — консолідують кілька проміжних `SaveChangesAsync` у межах однієї бізнес-операції в єдиний підсумковий запис `AppPlatformChange` та єдиний крок оптимістичної версії `Ver + 1`.
   - **`db.RunDerivedRecalculationAsync(...)`** — виконує каскадний похідний перерахунок (наприклад, підсумків чи статусу батьківського документа) зі збереженням інкременту `Ver++`, але без зайвого дублювання проміжних технічних записів у журналі аудиту.
   - **`db.SuppressAuditScope()`** — тимчасово вимикає запис у `AppPlatformChange` під час ідемпотентної стартової синхронізації кодифікованих довідників (`CodeRows.SyncAsync`).
4. **Канонічне правило зовнішніх ключів (`DeleteBehavior.Restrict`):**
   За замовчуванням для зовнішніх ключів встановлюється `DeleteBehavior.Restrict` (крім явно налаштованих композицій `Cascade` / `SetNull`), що захищає бізнес-дані від випадкового каскадного видалення.

### 1.3. Модульна ізоляція схем та міграцій (`[ModuleSchema]`, `PlatformMigrator`, `PlatformDesignTimeFactory<T>`)

Згідно з **ADR #8**, кожен модуль має власний самодостатній `*DbContext`, який успадковує контекст базового модуля:
`PlatformDbContext` → `AppDbContext` → `OrgDbContext` → `EcomDbContext`.

- Кожен модуль позначає свій `DbContext` (та/або збірку `*.Data`) атрибутом `[ModuleSchema("Ecom")]` із назвою модуля в `PascalCase`.
- **`ModuleOwnershipConvention`** автоматично визначає модуль-власник кожної сутності за її збіркою (`TEntity.Assembly`). Якщо сутність належить іншому модулю (успадкована від базового `DbContext` або позичена з паралельного модуля через `DbSet<TEntity>`), конвенція автоматично викликає `SetIsTableExcludedFromMigrations(true)` для поточного модуля.
- Історія міграцій зберігається в таблиці `__PlatformMigrations` (`PlatformMigrationHistory`) із розрізом за модулем, тому міграції кожного модуля генеруються і застосовуються незалежно.
- **`PlatformDesignTimeFactory<TDbContext>`** (наявна і в `Air.Data.Postgres`, і в `Air.Data.SqlServer`) забезпечує генерацію міграцій `dotnet ef migrations add` у збірках `*.Migrations` навіть без запущеної СУБД.
- **`PlatformMigrator`** (`Check`, `MigrateAsync`, `BaselineAsync` у `Platform.Data.Postgres` та `Platform.Data.SqlServer`) перевіряє відсутність незбережених змін моделі (`HasPendingModelChanges`), послідовно накатує міграції в порядку спадкування модулів під глобальним блокуванням СУБД (сесійний advisory lock у PostgreSQL або `sp_getapplock` у SQL Server) та верифікує цілісність колонок через `ModuleSchema.VerifyAsync`.

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
   Кожна стандартна `Feature` автоматично формує набір прав (`View`, `MenuView`, `Edit`, `Delete`, `Export`, `All`), а декларативні дії гридів і форм (`SetActions`), методи сервісів із `[RequireAction("...")]` та переходи `StatusFlowService` автоматично додаються до фічі під час реєстрації модуля, після чого фіча заморожується (`Freeze()`). Для тонких обмежень записів використовується `Feature<TService, TAccess>()` разом із `FeatureAccessEvaluator`.
2. **Клас життєвого циклу модуля (`<Mod>Module : PlatformModule<<Mod>>`)**:
   реєструє `DbContext` (`builder.AddPlatformPostgres<EcomDbContext>()` або `AddPlatformSqlServer`), сервіс контексту модуля (`EcomService`), декларативну документацію модуля `Docs` (`IReadOnlyList<ModuleDoc>`) та навігаційне меню `ModuleMenu`.

### 2.2. Стартова перевірка (`PlatformValidator`) та канонічний RBAC

Під час запуску хоста **`PlatformValidator.Validate`** перевіряє весь `ModuleCatalog` ще до прийому першого HTTP-запиту:

- відсутність дублікатів маршрутів сервісів і ключів фіч, валідність `PascalCase` у всіх `Handle` (включно з іменованими фільтрами гридів та діями `SetActions`);
- наявність явних декларацій прав (`SetActions`, `[ServiceAction]` із `[RequireAction]` або `[RequireRight]`) на всіх публічних екшенах і пікерах;
- цілісність переходів і статусів у `StatusFlowService`;
- суворе дотримання вертикальної ізоляції модулів (`ModuleCatalog.EnsureModuleServiceAccessAllowed`) для всіх посилань між сервісами гридів, форм, потоків та мультипанельних екранів (`ScreenService`).

Усі перевірки прав під час виконання працюють за принципом **fail-closed**:

- **`CanView(feature, user)`**: `user.IsAuthenticated && (user.Can(feature.View) || user.Can(feature.Edit))` (маски `*`, `Module.*`, `Module.Feature.*` розпізнаються в `HeldRights.Covers`).
- **Видимість у меню**: `CanView(feature, user) && user.Can(feature.MenuView)`.
- **`CanEdit` / `CanDelete` / `CanExport`**: перевіряють автентифікацію, прапорці фічі (`!IsReadOnly`, `HasDelete`, `HasExport`) та відповідне право користувача; у коді сервісів перевірка завжди записується через `app.Can(Ecom.CustomerOrder.Edit)`.
- **Захист від ескалації привілеїв**: користувач ніколи не може надати іншому користувачеві чи ролі права або тенантний скоуп ширші за власні (`app != null && app.Covers(...)`), змінити `IsRoot`-користувача без `IsRoot`, або видалити останню активну адмін-роль у тенанті.

### 2.3. Автентифікація, `PLATFORM_TOKEN_SECRET`, сесії, `MutationRunner` та JSON-конвертери

- **Секрет підпису токенів (`PLATFORM_TOKEN_SECRET`)**: у середовищі `Production` хост вимагає явного секрету довжиною щонайменше **32 символи** через змінну середовища `PLATFORM_TOKEN_SECRET` (або `Auth:TokenSecret`) і негайно зупиняє старт при його відсутності чи недостатній довжині.
- **Двотокенна сесія в `HttpOnly` cookies та захист `X-Platform-Client`**: короткоживучий `AccessCookie` + ротований ковзний `RefreshCookie` (`RefreshTokenLifetime`) із 30-секундним grace-вікном повторної видачі cookie для конкурентних вкладок, композитний rate-limiting за `(IP, login)`, захист від таймінг-атак, миттєва інвалідація `IUserAuthCache` та обов'язковий CSRF-заголовок `X-Platform-Client` на всіх мутуючих запитах — включно з `POST /api/auth/login` (для CLI/скриптів передавайте `-H "X-Platform-Client: cli"`):
  ```bash
  curl -X POST http://localhost:5091/api/auth/login \
    -H "Content-Type: application/json" \
    -H "X-Platform-Client: cli" \
    -d '{"login":"admin","password":"change-me"}'
  ```
- **`MutationRunner.RunIdempotentMutationAsync`**: виконує кожну мутацію в транзакції та `DbScope`, забезпечує ідемпотентність за `IdempotencyKey`, перехоплює конфлікти версій (`DbUpdateConcurrencyException` -> `MutationOutcome.Conflict`) і бізнес-відмови (`MutationOutcome.Refused`).
- **Суворі числові та гнучкі часові JSON-конвертери (`PlatformJsonOptions`)**:
  - **`StrictNumberParser` (`StrictNumberConverterFactory`)** перетворює порожній рядок `""` або `null` у `null` **виключно для nullable числових типів** (`int?`, `long?`, `decimal?`, щоб необов'язкові FK ніколи не ставали `0`), а для non-nullable числових полів (`int`, `long`, `decimal`) відхиляє `""`, `null` та некоректні рядки з `JsonException` (`400 Bad Request`), ніколи не підміняючи невалідне введення тихим нулем;
  - **`FlexibleDateTimeParser` (`FlexibleDateTimeConverter`, `FlexibleNullableDateTimeConverter`)** розбирає ISO-штампи з явним часовим зміщенням у UTC, а локальні рядки без зміщення зберігає як `DateTimeKind.Unspecified` без зсуву календарного дня та повертає `400 Bad Request` замість `500` на пошкоджених датах.
