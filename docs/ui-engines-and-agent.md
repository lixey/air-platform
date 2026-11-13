# Декларативні рушії інтерфейсу (`Air.App`), `@air/ui` та ШІ-асистент (`Air.Agent`)

Цей посібник пояснює реальні будівельні блоки прикладного розробника в **`Air.App`**, як вони рендеряться у фронтенді **`@air/ui`** та як взаємодіють із вбудованим ШІ-асистентом **`Air.Agent`** (усі приклади відповідають золотому еталону в `demo/`).

---

## 1. Таблиці даних (`GridService`)

У платформі є два варіанти базового сервісу таблиці:

- **`GridService<TEntity>`** (де рядок таблиці `TRow == TEntity`) — стандартний варіант для більшості реєстрів, документів і довідників.
- **`GridService<TEntity, TRow>`** — використовується, коли потрібна кастомна SQL-проєкція `query.Select(...)` з обчислюваними полями або агрегатами з кількох таблиць.

### 1.1. Оголошення колонок (`SetColumns`)

```csharp
protected override void SetColumns(Columns columns)
{
  columns.UseStatusOf(flow);

  columns.Add(d => d.Number).Width(120);
  columns.Add(d => d.Date).Width(115);
  columns.Add(d => d.Customer).AsReference<OrgCustomerGridService>().Width(220);
  columns.Add(d => d.Amount).AsMoney().Sum().SelectionSum().Width(130);
  columns.Add(d => d.Note).Hidden();
}
```

Ключові можливості колонок:

- **Типи та форматування:** `.AsDate()`, `.AsDateTime()`, `.AsNumber()`, `.AsMoney()`, `.AsPercent()`, `.AsCheckbox()`, `.AsSelect(options)`, `.AsReference<TGridService>()`, `.AsCover<TFileGrid>()`, `.AsImage()`.
- **Перехід до картки з комірки:** `.AsOpenForm<TFormService>()` або `.AsOpenForm<TFormService>(x => x.TargetId)` (якщо `TargetId == null`, клік безпечно ігнорується).
- **Редагування в комірці та пікери:** `.Editable()`, `.PickedBy<TSourceGrid>(AttachMethod)`.
- **Підсумки:** `.Sum()` (агрегат на рівні СУБД у нижньому рядку таблиці), `.SelectionSum()` та `.SelectionSummary(SummaryKind.Avg)` (агрегація виділених рядків у клієнті: `Sum`, `Avg`, `Count`, `Min`, `Max`).
- **Підказки (`Title`):** `.Title(tooltip)` або двоскладова підказка джерела `.Title(uiSource, dbSource)`.

### 1.2. Навігація, іменовані фільтри з живими лічильниками та захист видалення (`SetNav`, `SetQuery`, `SetHooks`)

- **Іменовані вкладки та фільтри (`SetQuery`):**
  ```csharp
  protected override void SetQuery(Query query)
  {
    query.Where(d => d.DocTypeId == EcomDocTypeData.CustomerOrderId);
    query.SortByDescending(d => d.Date).ThenByDescending(d => d.Id);

    query.NamedFilter("Stage", "Stage")
      .Inline()
      .ShowCounts()
      .Item("Open", "Open", d => d.DocStatusId != CompletedId && d.DocStatusId != CanceledId)
      .Default()
      .Color(UiColor.Blue)
      .Item("Completed", "Completed", d => d.DocStatusId == CompletedId)
      .Color(UiColor.Green)
      .All("All");

    query.NamedFilter("Unpaid", "Unpaid", d => d.PaidAmount < d.Amount)
      .Color(UiColor.Amber)
      .ShowCount();
  }
  ```
  - Усі ідентифікатори груп і елементів `NamedFilter` обов'язково задаються в `PascalCase` (`"Stage"`, `"Open"`, `"Unpaid"`).
  - `.Inline()` відображає групу як компактну смугу вкладок безпосередньо в панелі навігації грида (без випадаючого списку).
  - `.ShowCounts()` (на групі) та `.ShowCount()` (на окремому фільтрі чи елементі) автоматично повертають живі лічильники в `GridPageDto.NamedFilterCounts` (з нульовими витратами, якщо лічильники не увімкнені або запит іде з reference-пікера) і миттєво оновлюють бейджі після мутацій.
- **Декларативні дії таблиці та форми (`SetActions`):**
  ```csharp
  protected override void SetActions(Actions actions) =>
    actions.Add("CreateSalesInvoice")
      .Allow(Ecom.SalesInvoice.Edit)
      .Label("Create sales invoice")
      .Icon(UiIcon.Plus)
      .InMenu()
      .Do((ctx, ct) => ...);
  ```
  `SetActions` дозволяє декларативно оголошувати групові та одиничні (`.Single()`) операції, діалоги підтвердження (`.Confirm(...)`), типізовані форми параметрів (`.Ask<TInput>(...)`) та права доступу (`.AllowEdit()`, `.Allow(Right)`, `.AllowReadOnly()`) без створення кастомних HTTP-ендпоінтів.
- **Декларативний захист видалення за FK (`SetHooks`):**
  ```csharp
  protected override void SetHooks(Hooks hooks)
  {
    hooks.AllowMultiDelete();
    hooks.PreventDeleteWhenUsedBy<OrgProduct>(p => p.BrandId, "Cannot delete brand: it is used by products.");
  }
  ```
  Підтримуються як обов'язкові (`long`), так і необов'язкові (`long?`) зовнішні ключі: платформа перевіряє всі виділені ID одним пакетним `Distinct`-запитом і повертає локалізовану відмову для зайнятих записів.

### 1.3. Дочірні таблиці та інваріант батьківського скоупу (`SetParent`)

```csharp
protected override void SetParent(Parent parent) =>
  parent.Bind<EcomCustomerOrderFormService>(line => line.DocId);
```

У всіх точках входу (`GetDataAsync`, `ExportAsync`, `SaveCellAsync`, `DeleteAsync`, кастомних діях, `MoveStatusAsync`, `FileGridService`) платформа **першим кроком** викликає `EnsureParentScopeAsync`. Якщо батьківський запис недоступний поточному користувачеві, будь-яке читання або мутація дочірніх рядків блокується.

---

## 2. Форми, правила та неблокуючі попередження (`FormService<TEntity>`)

`FormService<TEntity>` автоматично обирає оптимальне подання картки (`OverlayModel.Popover` для компактних автономних форм із `<= 8` видимими скалярними полями без дерева прав, `0` вкладок, `0` таблиць звіту та `0` нефайлових підлеглих таблиць даних, або `OverlayModel.Drawer` в усіх інших випадках; перевизначати `Presentation` вручну потрібно лише для свідомих винятків), керує підготовкою чернетки (`PrepareDraftAsync`), правилами валідації та збереженням.

### 2.1. Елементи форми, `PatchOnSelect` та декларативне `SumTo` (`SetElements`)

```csharp
protected override void SetElements(Elements elements)
{
  elements.Add(d => d.Customer)
    .AsReference<OrgCustomerGridService>()
    .PatchOnSelect(d => d.DeliveryTypeId, c => c.DeliveryTypeId)
    .PatchOnSelect(d => d.PaymentMethodId, c => c.PaymentMethodId)
    .Required()
    .Span(6);

  elements.Add(d => d.Amount).AsMoney().ServerOwned().Span(3);

  elements.AddChild<EcomCustomerOrderLineGridService, EcomDocLine>()
    .Label("Lines")
    .SumTo(d => d.Qty, l => l.Qty)
    .SumTo(d => d.Amount, l => l.Amount)
    .ReadOnlyWhen(d => d.DocStatusId != EcomDocStatusData.CustomerOrderNewId);
}
```

- **`PatchOnSelect`:** під час зміни поля-довідника у формі оновлює залежні поля лише для зміненого тригера (`triggerField`), не перезатираючи інші поля чернетки.
- **`DisabledWhen` та `VisibleWhen`:** оцінюються на сервері алгоритмом нерухомої точки (fixed-point) — зміна типу чи прапорця в одному збереженні коректно розблоковує або ховає залежні поля, але поле ніколи не може розблокувати саме себе.
- **`.SumTo(parentField, childColumn)`:** суворо типізовано (з підтримкою ланцюжка для кількох полів) вказує зв'язок суми дочірніх рядків із полями заголовка для серверного перерахунку `ChildSumEvaluator`, UI та ШІ-пропозицій `<ui_proposal>`.

### 2.2. Правила валідації (`SetRules`) та неблокуючі попередження (`AsWarning`, `Warn`)

Поруч із помилками, що блокують збереження (`FormRefusedException`), платформа підтримує **неблокуючі попередження (`MessageType.Warning`)**, які дозволяють зберегти запис і автоматично показують жовтий тост користувачеві (як при збереженні з форми, так і при підтвердженні `<ui_proposal>` у чаті ШІ):

```csharp
protected override void SetRules(Rules rules) =>
  rules.For(e => e.DiscountPercent)
    .Must(d => d <= 25m)
    .WithMessage(L["Discount exceeds 25% — verify manager approval."])
    .AsWarning();

protected override void SetHooks(Hooks hooks) =>
  hooks.BeforeSave(c =>
  {
    if (c.Entity.DeliveryDate is { } dd && dd < c.Entity.Date)
    {
      c.Warn(L["Delivery date is earlier than the order date."], path: "deliveryDate");
    }
  });
```

Всередині `rules.For(...).Custom((val, ctx) => ...)` також доступний метод `ctx.Warn(...)`.

---

## 3. Статусні потоки (`StatusFlowService`)

`StatusFlowService` декларативно описує життєвий цикл документа або рядка, права на переходи та діалоги підтвердження/опитування:

```csharp
protected override void SetFlow(Flow flow)
{
  flow.State(doc => doc.DocStatusId);
  flow.Status(New, "New", UiColor.Sky);
  flow.Status(Confirmed, "Confirmed", UiColor.Blue);
  flow.FinalStatus(Completed, "Completed", UiColor.Green);
  flow.FinalStatus(Canceled, "Canceled", UiColor.Red);
  flow.Initial(New);
}

public Transition Confirm => FromOpen().To(Confirmed).When(HasLines, NoLines).Do(PostAsync);

public Transition Cancel => FromOpen().To(Canceled)
  .Confirm(Texts.CancelConfirm)
  .Ask<EcomDocCancelInput>(input => EcomDocActions.AskReason(input, r => r.Reason), note: r => r.Reason)
  .Do(PostAsync);
```

- **Атомарність пакета:** якщо хоча б один виділений запис не проходить умову `.When(...)` або додає помилку в `Move.Context`, жоден запис у пакеті не змінює статус.
- **Каскадні правила та підписники (`ChildRule`, `AddFollower`):** автоматично синхронізують статус і суми батьківського документа під песимістичним блокуванням `LockAsync`.
- **Інтеграція з `RightsTree`:** кожен публічний перехід автоматично стає окремим правом фічі в дереві налаштування ролей.

---

## 4. Аналітичні звіти (`ReportService`), Комбіновані екрани (`ScreenService`), Файли (`FileGridService`) та Аудит (`ChangeGridService`)

- **`ReportService`:** базовий сервіс інтерактивних аналітичних дашбордів (`GetReportAsync(ReportRequest, CancellationToken)` -> `ReportDataDto`), який підтримує групи швидких фільтрів (`FilterGroups`), картки KPI (`Kpis`), стовпчикові/лінійні/кругові діаграми (`Charts`) та деталізовану таблицю (`Table`) з клікабельним відкриттям картки документа (`RowDto.Open`) і семантичними кольорами бейджів через `RowDto.Colors` (у `Cells` завжди передається скалярний текст `L[statusName]`, а колір — у `Colors["status"]`). Див. еталон `EcomSalesReportService` у `demo/`.
- **`ScreenService` та мультипанельні робочі місця (`PanelLayout.Screen<TService>`):** дозволяє компонувати кілька повноцінних екранів платформи (наприклад, реєстр замовлень поруч із залишками складу або звітом) без жодного рядка кастомного фронтенд-коду:
  ```csharp
  public override Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default) =>
    Task.FromResult(new ScreenDto
    {
      Layout = PanelLayout.Row(servicePath)
        .Screen<EcomCustomerOrderGridService>("orders", p => p.Label(L["Customer Orders"]).Fill().Min(520))
        .Screen<EcomStockBalanceGridService>("stock", p => p.Label(L["Stock Balances"]).Size(480).Resizable().Collapsible())
        .Build()
    });
  ```
  Кожна вбудована панель рендериться компонентом `PanelScreenComponent` із незалежною навігацією та реактивним оновленням, а `PlatformValidator` на старті перевіряє всі вкладені сервіси на вертикальну ізоляцію модулів (ADR #8).
- **`FileGridService<TFile>` / `FileGridService<TDbContext, TFile>`:** вбудований сервіс файлів із перевіркою сигнатур байтів, генерацією прев'ю, обкладинками (`IHasCoverFile`) та підписаними URL `FileLinks`.
- **`ChangeGridService`:** глобальний переглядач історії `AppPlatformChange` із розшифруванням назв довідників і часовим поясом користувача.

---

## 5. Фронтенд `@air/ui` та генератор контрактів (`Air.UiContracts`)

Бібліотека `air-ui/` побудована на **Angular 22+ Standalone Components**, **Signals (`signal`, `computed`, `linkedSignal`)** та **Tailwind CSS v4** зі суворою типізацією (`"strict": true`).

- **Динамічне дерево компонентів (`UI_COMPONENTS`):** рендерить `Grid`, `Form`, `Report`, `PanelScreen`, `Nav` та елементи керування виключно за серверними моделями `kind` на семантичних CSS-токенах теми (`bg-surface`, `bg-field`, `border-line`, `text-ink`) без жодних сирих палітрових чи `dark:*` класів.
- **Єдине джерело контрактів (`make contracts`):** CLI `Air.UiContracts` експортує всі `[UiContract]` DTO та ключі локалізації у `air-ui/src/core/generated/contracts.ts` та `ui-texts.json` (перевіряється в `make contracts-check` під час `make gate`).

---

## 6. Вбудований ШІ-асистент (`Air.Agent` та `Air.Mcp`)

ШІ-асистент підключається одним рядком `options.AddPlatformAgent()` у `PlatformOptions` (або низькорівневою парою `services.AddPlatformAgent()` + `app.MapPlatformAgent()`) і керується виключно метаданими платформи:

1. **Гранулярний доступ (`AgentAccess`):** `.AllowAgent()` вмикає читання й підготовку змін (`AgentAccess.ReadWrite`), а `.AllowAgent(AgentAccess.Read)` відкриває таблицю або `ReportService` лише для читання й аналітики з урахуванням прав поточного користувача (`CanView` / `CanEdit`).
2. **Декларативна база знань модуля (`PlatformModule.Docs`):** кожен модуль описує свої предметні правила у властивості `Docs` (`ModuleDoc`), які `Air.Agent` автоматично збирає з `ModuleCatalog`, сідіть у `AppAgentMemory` й додає в системний контекст без обрізання, а нові факти пропонує зберегти тегом `<agent_memory>`.
3. **Картки `<ui_proposal>`, візуальні блоки (`<ui_kpi>`, `<ui_chart>`, `<ui_table>`, `<ui_html>`), довідники та попередження:** малі довідники (`<= 50` записів) підвантажуються одразу, більші опитуються через серверний пошук та вбудовані MCP-інструменти (`POST /api/mcp`: `platform_catalog_list`, `platform_catalog_describe`, `platform_records_query`, `platform_proposal_stage`). При підтвердженні `<ui_proposal>` `AgentProposalExecutor` виконує стандартний `FormService.SaveAsync` в атомарній транзакції й прокидає всі неблокуючі попередження (`AgentCommitResultDto.Warnings`) у клієнтський `AlertService`.
4. **Безпека та ізоляція:** цілісність підготовлених пропозицій `<ui_proposal>` у пам'яті процесу перевіряється HMAC-печаткою (`ProposalSeal`), теги `<agent_memory>` спочатку стають чернетками очікування (`PendingMemoryStore` із лімітом на власника) і записуються в БД лише після явного підтвердження власником (`POST /api/agent/memory-confirm`), локальні CLI-провайдери за замовчуванням вимкнені в `Production` (`AgentOptions.AllowLocalCliInProduction = false`), а у dev-середовищі отримують промпт через `stdin` у тимчасовій директорії `0700` з очищеним оточенням і суворою ізоляцією за `(TenantId, UserId)`.
