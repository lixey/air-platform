# Декларативні рушії інтерфейсу (`Air.App`), `@air/ui` та ШІ-асистент (`Air.Agent`)

Цей посібник пояснює, як працюють основні будівельні блоки прикладного розробника в **`Air.App`**, як вони відображаються у фронтенді **`@air/ui`** та як взаємодіють із вбудованим ШІ-асистентом **`Air.Agent`**.

---

## 1. Таблиці даних (`GridService`)

У платформі є два варіанти базового сервісу таблиці:

- **`GridService<TEntity>`** (де рядок таблиці `TRow == TEntity`) — використовується у 90% випадків для стандартних реєстрів і довідників.
- **`GridService<TEntity, TRow>`** — використовується у складних випадках, коли потрібна кастомна SQL-проєкція `SelectSelector` (наприклад, розрахунок агрегатів або специфічних полів рядка з кількох таблиць).

### 1.1. Оголошення колонок (`SetColumns`)

```csharp
protected override void SetColumns(Columns columns)
{
  columns.UseForm<EcomCustomerOrderFormService>();
  columns.StatusFlow<EcomCustomerOrderFlowService>();

  columns.Add(x => x.Date).AsDate().Width(110);
  columns.Add(x => x.CounterpartyId)
    .AsReference<OrgCounterpartyGridService>()
    .Label(x => x.Counterparty!.Name)
    .Width(180);
  columns.Add(x => x.Amount).AsMoney().Sum().Width(130);
  columns.Add(x => x.Description).Width(240);
}
```

Ключові можливості колонок:

- **Типи відображення:** `.AsDate()`, `.AsDatetime()`, `.AsNumber()`, `.AsMoney()`, `.AsPercent()`, `.AsCheckbox()`, `.AsSelect(options)`, `.AsReference<TGridService>()`, `.AsCover<TFileGrid>()`, `.AsImage()`.
- **Навігація до форми з комірки:** `.AsOpenForm<TFormService>()` або `.AsOpenForm<TFormService>(x => x.TargetId)` (якщо `TargetId` у рядку дорівнює `null`, клік безпечно ігнорується й ніколи не відкриває чужий запис за ID рядка).
- **Inline-редагування в таблиці:** `.Editable()` (або `.Editable(() => ...)`), а також `.PickedBy<TSource>(method)` для швидкого вибору значення в комірці через серверний пікер.
- **Підсумки:** `.Sum()` (рахує загальну суму в БД) та `.SelectionSum(unit)` (рахує суму виділених галочками рядків у клієнті).
- **Керування сортуванням і фільтрацією:** `.Sortable(false)`, `.Filterable(false)`, `.Hidden()`.
- **Підказки (`Title`):** `.Title(tooltip)` або двоскладова підказка джерела `.Title(uiSource, dbSource)` при наведенні курсора.

### 1.2. Навігація, дії та іменовані фільтри (`SetNav`, `SetQuery`)

- У `SetNav(Nav nav)` додаються кнопки створення (`nav.AddCreate<TForm>()`), пакетні або одиночні дії (`nav.AddAction(HandleAction).Confirm(...).Ask(...)`), пікери масового підбору (`nav.AddPicker<TSource>(...)`).
- У `SetQuery(Query query)` задаються базові обмеження, сортування за замовчуванням (`query.DefaultSort(...)`), швидкі вкладки-фільтри (`query.AddFilter(handle, label, predicate)`), підсвічування рядків кольором (`query.Color(...)`) та серверні прапорці доступності кнопок для кожного рядка (`OfferBatchEvaluator`).

### 1.3. Дочірні таблиці та інваріант батьківського скоупу (`SetParent`)

Якщо таблиця є підлеглою (наприклад, рядки замовлення всередині картки замовлення):

```csharp
protected override void SetParent(Parent parent)
{
  parent.Bind<EcomCustomerOrderFormService, EcomDoc>(x => x.DocId);
}
```

- **Інваріант безпеки:** У всіх точках входу (`GetDataAsync`, `GetCountAsync`, `ExportAsync`, `SaveCellAsync`, `DeleteAsync`, `ExecuteBatchAsync`, `MoveStatusAsync`, `FileGridService.Upload/Delete`) платформа **першим кроком** викликає `EnsureParentScopeAsync`. Якщо користувач не має доступу до батьківського запису, будь-яке читання, експорт або мутація дочірніх рядків блокується.

---

## 2. Форми та картки записів (`FormService<TEntity>`)

`FormService<TEntity>` керує відображенням форми (оверлеєм `Drawer` або `Popover`), підготовкою чернетки (`PrepareDraftAsync`), валідацією та збереженням запису.

### 2.1. Побудова елементів форми (`SetElements`)

```csharp
protected override void SetElements(Elements elements)
{
  elements.AddRow(row =>
  {
    row.Add(x => x.Date).AsDate().Required();
    row.Add(x => x.CounterpartyId)
      .AsReference<OrgCounterpartyGridService>()
      .PatchOnSelect(s => s.Map(c => c.CurrencyId, x => x.CurrencyId))
      .Required();
  });

  elements.Add(x => x.Description)
    .DisabledWhen(x => x.StatusId == EcomDocStatus.Completed);

  elements.AddChild<EcomCustomerOrderLineGridService>();
}
```

### 2.2. Як працює `PrepareDraftAsync` та `PatchOnSelect`

Коли користувач відкриває форму створення/редагування або змінює значення довідника з `.PatchOnSelect(...)`:

1. Форма спершу встановлює `Request`, завантажує батьківський запис і поточну сутність `CurrentEntity` (при редагуванні), і лише потім будує список елементів `EnsureElementsAsync`, щоб умовна логіка (`IsNew`, `CurrentEntity`) працювала зі справжніми даними.
2. Якщо користувач змінив конкретне поле (`triggerField`), `PatchOnSelect` виконує патч лише для цього поля (включно з легітимним значенням `0`), не перезатираючи інші поля, які користувач уже встиг відредагувати вручну.

### 2.3. Канонічна семантика `DisabledWhen` та `VisibleWhen` при збереженні

Платформа використовує **алгоритм нерухомої точки (fixed-point)** для оцінки `DisabledWhen` на сервері під час збереження (`FormService.Save.cs`):

- **При створенні (`IsNew`):** доступність полів обчислюється на ефективному стані `draft + input`.
- **При редагуванні (`!IsNew`):** поле `F` вважається захищеним від зміни, якщо воно заблоковане **і** в збереженому стані в БД (`entity`), **і** в ефективному стані після застосування вже прийнятих полів із `input`. При цьому для перевірки самого поля `F` його власне значення завжди береться з БД (`entity`), щоб поле не могло розблокувати саме себе (наприклад, `IsPosted.DisabledWhen(x => x.IsPosted)`).
- Завдяки цьому легітимні сценарії працюють безвідмовно:
  1. Якщо зміна `Type: A -> B` в одному збереженні розблоковує обов'язкове поле `ExtraField`, сервер приймає `ExtraField` і валідує його як `Required`.
  2. Якщо користувач заповнює `Amount` і одночасно ставить галочку `IsLocked = true`, сервер приймає `Amount`, а замок починає діяти з наступного збереження.
- **Приховані поля (`Hidden` / `VisibleWhen == false`):** автоматично виключаються з перевірки `Required` та обов'язковості посилань на ефективному стані, але їхні збережені в БД значення не затираються.

---

## 3. Статусні потоки (`StatusFlowService<TEntity>`)

`StatusFlowService<TEntity>` описує життєвий цикл документа або рядка:

```csharp
protected override void Configure()
{
  Status(x => x.StatusId);

  Transition("Confirm", from: [OrderStatus.New], to: OrderStatus.Confirmed)
    .Color(UiColor.Emerald)
    .Guard((order, ct) => order.Total > 0 ? null : L["OrderTotalMustBePositive"])
    .Effect(async (ctx, order, ct) => { ... });
}
```

- **Двофазне пакетне виконання (`MoveInternalAsync`):** При переведенні пакета рядків спочатку перевіряються всі `Guards` по всьому пакету. Якщо хоча б один запис не проходить гард, жоден запис у пакеті не змінюється.
- **Каскадна синхронізація (`ChildFlowRuleBuilder` та `AddFollower`):** Дозволяє автоматично перераховувати статус і суму замовлення при зміні статусів його рядків. Перед завантаженням дочірніх рядків і розрахунком агрегатів (`Sum`, `Cost`, `All`, `Any`) автоматично береться песимістичне блокування батьківського запису (`LockAsync`).
- **Дерево прав (`RightsTree`):** Використовує `AllTransitions` (повний список переходів сервісу фічі), щоб адміністратор бачив чекбокси прав на кожен перехід статусу в налаштуваннях ролі.

---

## 4. Звіти (`ReportService`), Екрани (`ScreenService`), Файли (`FileGridService`) та Аудит (`ChangeGridService`)

- **`ReportService<TFilter, TRow>`**: будує аналітичні звіти з панеллю параметрів фільтрації, групуванням, підсумковими рядками, форматуванням дат/грошей та клікабельними рядками для переходу до первинних документів.
- **`ScreenService`**: дозволяє будувати комбіновані дашборди з KPI-картками, графіками (`ChartService`) та панелями (`PanelLayout`).
- **`FileGridService<TFile, TParent>`**: готовий сервіс прикріплених файлів із перевіркою MIME-типів за сигнатурами байтів (`FileTypeTable`), генерацією WebP/JPEG прев'ю, обкладинками (`FileCover.Auto`), лімітами розміру/кількості та підписаними посиланнями `FileLinks`.
- **`ChangeGridService`**: глобальний переглядач журналу аудиту `AppPlatformChange`, який будує покроковий Diff «До / Після» з розшифруванням назв довідників та форматуванням часу у часовому поясі користувача (`User.TimeZone`).

---

## 5. Фронтенд `@air/ui` та генератор контрактів (`Air.UiContracts`)

Бібліотека `air-ui/` побудована на **Angular 22+ Standalone Components**, **Signals (`signal`, `computed`, `linkedSignal`)** та **Tailwind CSS v4** зі суворою типізацією (`"strict": true`).

- **Динамічна диспетчеризація (`UI_COMPONENTS`):** Бекенд повертає дерево панелей і моделей (`GridModel`, `FormModel`, `ReportModel`, `NavModel`), а `@air/ui` автоматично резолвить відповідні компоненти за полем `kind`.
- **Синхронізація контрактів (`make contracts`):** Усі DTO в `Air.Api` та `Air.App`, позначені атрибутом `[UiContract]`, автоматично експортуються у `air-ui/src/core/generated/contracts.ts`, `endpoints.ts` та `ui-texts.json`. Жодні інтерфейси бекенду чи тексти локалізації не дублюються вручну на фронтенді.

---

## 6. Вбудований ШІ-асистент (`Air.Agent` та `Air.Mcp`)

ШІ-асистент працює повністю на основі метаданих платформи й не містить жодної специфіки конкретних застосунків:

1. **Як увімкнути:** Додайте `.AllowAgent()` до фічі в описі модуля:
   ```csharp
   public static readonly Feature Order = new Feature(Handle, nameof(Order)).AllowAgent();
   ```
2. **Як формується контекст (`AgentContextQueryService`):**
   - Реєстр `AgentFeatureRegistry` знаходить усі фічі з `.AllowAgent()`, до яких поточний користувач має право `CanView` / `CanEdit` (включно з формами над `ReadOnly`-таблицями та першою редагованою дочірньою таблицею рядків).
   - Невеликі довідники (`<= 50` записів) підвантажуються в контекст одразу, а великі (`> 50` записів) шукаються на льоту через серверний пошук.
3. **Картки пропозицій `<air_proposal>` (`AgentProposalBuilder` / `AgentProposalExecutor`):**
   - Коли користувач просить створити або змінити запис (чи розпізнати прикріплене зображення або документ), ШІ повертає структурований блок `<air_proposal>`.
   - Бекенд проганяє його через `PrepareDraftAsync` цільової `FormService` (при редагуванні спочатку завантажує запис, щоб бачити edit-only поля), резолвить посилання за `ReferenceLabelPaths` і показує користувачу інтерактивну картку підтвердження в чаті.
   - Запис у БД відбувається **виключно після натискання кнопки підтвердження користувачем** через стандартний `FormService.SaveAsync`.
4. **Сувора ізоляція (`TenantId`, `UserId`, `SessionId`):**
   - Робоча директорія CLI-провайдера (`--add-dir`), сесії `McpExecutionBridge`, завантажені вкладення та пам'ять `AppAgentMemory` суворо ізольовані за `(TenantId, UserId, SessionId)`. Жоден тенант чи користувач не може прочитати контекст або сесію іншого.
