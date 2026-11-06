# Посібник зі створення нового застосунку та модуля

Використовуйте цей чекліст під час розгортання нового застосунку поруч із `air-platform` (наприклад, `../my-port` або `../ak-bcc`) або під час додавання нового модуля до існуючого застосунку.

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
3. **`Makefile`** — містить стандартні цілі: `build`, `format`, `format-check`, `check-paths`, `check-arch`, `check-ts`, `contracts`, `gate`, `audit`, `agent-up`, `agent-restart`, `agent-down`.
4. **`AGENTS.md`** — інструкції для ШІ-агентів із виділеними портами розробки.

---

## 2. Структура бекенд-модуля (`src/<Mod>/`)

Кожен бізнес-модуль `<Mod>` (наприклад, `BccApp`, `BccOrg`, `BccEcom`) складається з **трьох окремих проєктів**:

### 2.1. `<Mod>.Data` (Схема даних та `DbContext`)

- Не залежить від ASP.NET Core чи `Air.Api`.
- У `GlobalUsings.cs` або на класі контексту обов'язково оголошується атрибут схеми модуля:
  ```csharp
  [assembly: ModuleSchema("ecom")]
  ```
- `<Mod>DbContext` успадковує контекст базового модуля (`PlatformDbContext` → `AppDbContext` → `OrgDbContext` → `EcomDbContext`).
- Кожен клас сутності розміщується в окремому файлі (1 тип = 1 файл) відповідно до структури папок.

### 2.2. `<Mod>.Migrations` (Міграції EF Core та початкові дані `Seeds`)

- Містить файли міграцій, `*ModelSnapshot.cs` та класи початкового наповнення (`<Mod>Seed.cs`, `<Mod>CodeRows.cs`).
- Відокремлений від `<Mod>.Data` згідно з **ADR #5**, щоб багатотисячні файли знімків міграцій не сповільнювали компіляцію доменних проєктів.

### 2.3. `<Mod>.Api` (Фічі, сервіси таблиць, форм, звітів і потоків статусів)

- **`<Mod>.cs`**: оголошує `Handle` модуля (у `PascalCase`) та всі фічі (`public static readonly Feature ...`).
- **`<Mod>Module.cs`**: спадкоємець `PlatformModule<<Mod>DbContext>`, який реєструє модуль, ресурси локалізації (`<Mod>Resource`) та `DbContext`.
- **Базові сервіси модуля**: `<Mod>AppService`, `<Mod>GridService<TEntity>`, `<Mod>FormService<TEntity>`, `<Mod>StatusFlowService<TEntity>`.
- **Папки фіч (`Orders/`, `Products/` тощо)**:
  - Дотримуйтесь правила **«1 набір сервісів на кожну `Feature`» (ADR #4)**: кожна фіча має власний конкретний `*GridService`, `*FormService`, `*FlowService` в окремому файлі з явною властивістю:
    ```csharp
    public override Feature Feature => Ecom.Order;
    ```
  - Усі DTO та моделі запитів лежать у папці `Contracts/`, а внутрішні допоміжні класи — у `Internals/` (простори імен `*.Internals` підключаються локальним `using`, а не в `GlobalUsings.cs`).
  - Методи розширення розміщуються у класах та файлах із суфіксом `*Extensions.cs`.
- **`Resources/<Mod>Resource.resx` та `<Mod>Resource.uk.resx`**: усі назви полів, колонок, кнопок, помилок і підказок зберігаються в ресурсах (нуль захардкоджених рядків у C#).

---

## 3. Хост застосунку (`src/<Host>.Api/Program.cs`)

Вхідна точка хоста використовує єдиний лаконічний виклик `PlatformHost.RunAsync`:

```csharp
await PlatformHost.RunAsync(
  args,
  options =>
  {
    options.AddModule<AppModule>();
    options.AddModule<OrgModule>();
    options.AddModule<EcomModule>();
    options.Services.AddAirAgent();
  },
  app =>
  {
    app.MapAirAgent();
  });
```

> [!IMPORTANT]
> Не додавайте у `Program.cs` ручні виклики `app.UseDefaultFiles()`, `app.UseStaticFiles()` або `app.MapFallbackToFile("index.html")` — `PlatformHost` (`app.MapPlatform()`) вже автоматично налаштовує роздачу статики та безпечний SPA-fallback із захистом `/api/* -> 404 NotFound`.

---

## 4. Фронтенд-воркспейс (`<web-dir>/`)

1. Створюється Nx + Angular 22+ воркспейс із симлінком `.air-ui -> ../../air-platform/air-ui/src`.
2. У `tsconfig.base.json` вмикається `"strict": true` та прописується шлях `"@air/ui": ["./.air-ui/index.ts"]` (жодних абсолютних шляхів у конфігураціях).
3. Усі згенеровані контракти (`contracts.ts`, `endpoints.ts`, `ui-texts.json`) імпортуються безпосередньо з `@air/ui` — не створюйте дублюючих папок `generated/` усередині окремих застосунків.

---

## 5. Перевірка перед першим комітом

1. `make prepare` (або `make reset`) — перевірка створення таблиць, схем модулів (`__PlatformMigrations`) та сидів.
2. `make contracts` — синхронізація TypeScript-контрактів і текстів локалізації.
3. `make format` — форматування C# (`dotnet format`) та фронтенду (`prettier`).
4. `make gate` — повна перевірка форматування, відсутності абсолютних шляхів, архітектурних правил, типів TypeScript (`ngc --noEmit` зі `strict: true`), збірки та тестів.
5. `make agent-restart` — перезапуск виділених агентських серверів.
