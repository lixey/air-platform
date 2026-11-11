# Посібник зі створення нового застосунку та модуля

Використовуйте цей чекліст під час розгортання нового застосунку на базі `air-platform` або під час додавання нового модуля до існуючого застосунку (як золотий еталон див. `demo/`).

---

## 1. Кореневі файли репозиторію застосунку

У корені репозиторію створюються:

1. **`Directory.Build.props`** — задає `.NET 10`, `LangVersion=preview`, `Nullable=enable`, `<GenerateDocumentationFile>true</GenerateDocumentationFile>` (для автоматичного очищення зайвих `using` через `IDE0005`) та глобальні імпорти просторів імен платформи (`Platform.Data.*`, `Platform.Api.*`, `Platform.App.*`):
   ```xml
   <Project>
     <PropertyGroup>
       <TargetFramework>net10.0</TargetFramework>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
       <LangVersion>preview</LangVersion>
       <GenerateDocumentationFile>true</GenerateDocumentationFile>
       <NoWarn>$(NoWarn);CS1591;CS9107</NoWarn>
     </PropertyGroup>
     <ItemGroup>
       <Using Include="Platform.Data" />
       <Using Include="Platform.Data.Entities" />
       <Using Include="Platform.Data.Schema" />
     </ItemGroup>
   </Project>
   ```
2. **`.editorconfig`** та **`.githooks/`** — копіюються з `air-platform` і активуються командою `git config core.hooksPath .githooks`.
3. **`Makefile`** — містить стандартні цілі: `build`, `format`, `format-check`, `check-paths`, `check-arch`, `contracts`, `check-ts`, `gate`, `audit`, `agent-up`, `agent-restart`, `agent-down`.
4. **`AGENTS.md`** — інструкції для ШІ-агентів із виділеними портами розробки.

---

## 2. Структура бекенд-модуля (`src/<Mod>/`)

Кожен бізнес-модуль `<Mod>` (наприклад, `DemoApp`, `DemoOrg`, `DemoEcom`) складається з **трьох окремих проєктів**:

### 2.1. `<Mod>.Data` (Схема даних та `DbContext`)

- Не залежить від ASP.NET Core чи `Air.Api`.
- Клас контексту позначається атрибутом схеми модуля в `PascalCase`:
  ```csharp
  [ModuleSchema("Ecom")]
  public class EcomDbContext : OrgDbContext
  ```
- `<Mod>DbContext` успадковує контекст базового модуля (`PlatformDbContext` → `AppDbContext` → `OrgDbContext` → `EcomDbContext`).
- Кожен клас сутності розміщується в окремому файлі (1 тип = 1 файл) відповідно до структури папок.

### 2.2. `<Mod>.Migrations` (Міграції EF Core та початкові дані `Seeds`)

- Містить файли міграцій, `*ModelSnapshot.cs` та класи початкового наповнення (`<Mod>Seed.cs`, `<Mod>Catalog.cs`).
- Відокремлений від `<Mod>.Data` згідно з **ADR #5**, щоб багатотисячні файли знімків міграцій не сповільнювали компіляцію доменних проєктів.

### 2.3. `<Mod>.Api` (Фічі, сервіси таблиць, форм, звітів, екранів і потоків статусів)

- **`<Mod>.cs`**: спадкоємець `ModuleDefinition<<Mod>>` із атрибутом `[Module("<Mod>")]`, який оголошує всі фічі (`Feature<TService>()`), права та доступ ШІ (`.AllowAgent()`):
  ```csharp
  [Module(EcomData.ModuleHandle)]
  public sealed class Ecom : ModuleDefinition<Ecom>
  {
    public static readonly Feature CustomerOrder = Feature<EcomCustomerOrderGridService>().AllowAgent();
    public static readonly Feature SalesWorkspace = Feature<EcomSalesWorkspaceScreenService>().ReadOnly().WithoutExport();
    public static readonly Feature SalesReport = Feature<EcomSalesReportService>().ReadOnly().WithoutExport().AllowAgent(AgentAccess.Read);
  }
  ```
- **`<Mod>Module.cs`**: спадкоємець `PlatformModule<<Mod>>`, який у `ConfigureServices` реєструє `DbContext`, сервіс модуля (`EcomService`) та предметні знання ШІ (`AgentMemoryCatalog.RegisterDomainKnowledge(...)`), а у властивості `Menu` описує дерево `ModuleMenu`.
- **Базові сервіси модуля**: `<Mod>Service`, `<Mod>GridService<TEntity>`, `<Mod>FormService<TEntity>`, `<Mod>FlowService`.
- **Папки фіч (`CustomerOrder/`, `SalesReport/`, `SalesWorkspace/` тощо)**:
  - Дотримуйтесь правила **«1 набір сервісів на кожну `Feature`» (ADR #4)**: кожна фіча має власний конкретний `*GridService`, `*FormService`, `*FlowService` в окремому файлі з явною властивістю:
    ```csharp
    public override Feature Feature => Ecom.CustomerOrder;
    ```
  - Усі DTO та моделі запитів лежать у папці `Contracts/`, а внутрішні допоміжні класи — у `Internals/` (простори імен `*.Internals` підключаються локальним `using`, а не в `GlobalUsings.cs`).
  - Методи розширення розміщуються у класах та файлах із суфіксом `*Extensions.cs`.
- **`Resources/<Mod>Resource.cs` та `<Mod>Resource.uk.resx`**: усі назви полів, колонок, кнопок, помилок і підказок перекладаються через ресурси без захардкоджених рядків.

---

## 3. Хост застосунку (`src/<Host>.Api/Program.cs`)

Вхідна точка хоста використовує єдиний лаконічний виклик `PlatformHost.RunAsync` (як у `demo/src/Demo.Api/Program.cs`):

```csharp
await PlatformHost.RunAsync(
  args,
  options =>
    options
      .AddModule<EcomModule>()
      .AddModule<OrgModule>()
      .AddModule<AppModule>()
      .AddAirAgent()
);
```

> [!IMPORTANT]
> Не додавайте у `Program.cs` ручні виклики `app.UseDefaultFiles()`, `app.UseStaticFiles()` або `app.MapFallbackToFile("index.html")` — `PlatformHost` автоматично монтує API модулів, `/api/agent/*`, `/api/mcp`, статику та безпечний SPA-fallback із захистом `/api/* -> 404 NotFound`.

---

## 4. Фронтенд-воркспейс (`<web-dir>/`)

1. Створюється Nx + Angular 22+ воркспейс зі суворою типізацією (`"strict": true`).
2. У `tsconfig.base.json` прописується відносний шлях `"@air/ui": ["../../air-ui/src/index.ts"]` (або `["../../air-platform/air-ui/src/index.ts"]` для зовнішнього репозиторію поруч із платформою — нуль абсолютних шляхів у конфігураціях).
3. Усі згенеровані контракти (`contracts.ts` та `ui-texts.json`) живуть у `air-ui/src/core/generated/` і імпортуються через `@air/ui` — не створюйте дублюючих папок `generated/` усередині окремих застосунків.

---

## 5. Перевірка перед першим комітом

1. `make prepare` (або `make reset` у локальному демо) — перевірка створення таблиць, схем модулів (`__PlatformMigrations`) та сидів.
2. `make contracts` — синхронізація TypeScript-контрактів (`contracts.ts`) і текстів локалізації (`ui-texts.json`).
3. `make format` — форматування C# (`dotnet format`) та фронтенду (`prettier`).
4. `make audit` — незалежний контрольований аудит дифу через `Claude Fable 5.2 (High)`.
5. `make gate` — повний шлюз якості (`format-check`, `check-paths`, `check-arch`, `contracts-check`, `check-ts`, `build`, `test`).
6. `make agent-restart` — перезапуск виділених агентських серверів.
