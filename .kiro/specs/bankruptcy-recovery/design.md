# Design: bankruptcy-recovery

## Document Information

- **Feature Name**: bankruptcy-recovery (I02)
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Reviewers**: владелец проекта; автор `mod-framework` (сигнатуры Core, `ModCredit`, `ModMail`); автор `rival-acquisitions` (общий `CapitalLedger`)
- **Related Documents**: `requirements.md`, `tasks.md` этой спеки; `.kiro/steering/context.md`; `.kiro/steering/owner-decisions.md`; `.kiro/specs/mod-framework/design.md` (контракт Core); решения `0002` (Harmony), `0003` (тулчейн и тесты), `0004` (боковой файл), `0005` (UI из шаблонов), `0006` (контент-JSON); `pt-modding/CODEMAP.md` (разделы Bank, CompanySystem, Production)

## Overview

Фича живёт в `PTMod.Features.BankruptcyRecovery` и работает только с компанией игрока (`Player.Instance.Company`). Она не меняет ни один ванильный порог и ни одну формулу банка. Вместо этого она:

1. **Добавляет деньги как «деньги + обязательство».** Займы зачисляются через `Company.AddMoney` (капитальная операция, мимо отчёта о прибылях и налога), проценты и комиссии списываются через `Company.SpendMoney(…, ExpenseOrigin.Interest)`. Так кредитная линия и бридж-кредит сдвигают баланс игрока от порога дефолта, не трогая сам порог.
2. **Перехватывает один момент ванильного цикла банкротства:** день, когда игрок должен стать банкротом. Префикс `BankruptcyHandler.TickBankruptcies` удерживает счётчик дефолта игрока и открывает предложение спасения.
3. **Строит меню спасения** поверх ванильных механизмов продажи (`ProductionMath.SaleCost`, `StorageMath.SellStock`, `Cpu.TriggerRetirement`, `ManageLinesWindow`) без новых цен.
4. **Безопасно выключается.** Обязательства мода не замораживаются: при выключении фичи остаток переводится в обычный долг игры (после подтверждения в хабе).

Все числа — в `Content/bankruptcy-recovery/balance.json`, состояние — в секции `bankruptcy-recovery` бокового файла `ModSave` (`IModSaveParticipant`; версию секции `_schema` пишет и читает ядро).

> Сервисы Core используются по контракту `mod-framework/design.md`: `FeatureContext`, `TickPhase`, `ModTicks`, `ModSave`/`IModSaveParticipant`, `ModUI`, `ModMail`, `ModNotify`, `ModModifiers`, `ModReputation`, `ModCredit`, `ModHooks`, `ModContent`, `ModSettings`, `ModL10n`, `ModLog`. Свои счётчики ошибок, флаги `Enabled`, блоки ID писем и собственные подписки на события `DateController` фича не ведёт.

### Design Goals

- **Нулевое влияние на ИИ:** ни один патч не меняет состояние компаний ИИ; `CentralBank.DebtLimit` и `BankruptcyHandler.defaultTimeLimit` только читаются.
- **Ванильные цены и окна** там, где они уже есть: мод не изобретает своих цен на линии и склад.
- **Цена каждого решения видна до подтверждения** (превью, `ModUI.Confirm`).
- **Безопасный отказ:** при любой ошибке банкротство идёт ванильным путём.
- **Без бесплатных денег:** ни выключение фичи, ни удаление записи не делают кредит беспроцентным.
- **Чистая логика отделена от игры:** формулы и правила — публичные статические классы `Logic` без Unity, покрыты тестами мини-раннера.

### Key Design Decisions

- **D1. Займ = деньги + обязательство, порог не трогаем.** Тело займа — `Company.AddMoney(+A)`, возврат тела — `Company.AddMoney(−p)`, проценты и комиссии — `SpendMoney(Interest)`. Порог дефолта читается из `ModCredit` (`ThresholdOf(игрок)`, `PlayerDebtLimit()`): то, что применяет игра с учётом P9/P10 ядра.
  - *Отвергнуто:* собственный порог дефолта для игрока через transpiler `BankruptcyHandler.TickBankruptcies` (ядро уже держит P10; два transpiler'а конфликтуют; UI игры — `Money.UpdateMoneyText`, `MoneyBalance.UpdateUI` — продолжал бы рисовать другой порог).
  - *Отвергнуто:* изменение `CentralBank.DebtLimit` — одно глобальное значение, сдвинуло бы порог дефолта, резервы и поведение всех ИИ (`AIBehaviourResearch.Reserves`, `AIBehaviourFinance.OnTick`, `AIBehaviourProduction.Expand`).
  - *Отвергнуто:* чтение `CentralBank.Instance.DebtLimit` напрямую: при активном P10 порог игрока другой (испытание «Жёсткий кредит»), предупреждение и автозаём считались бы от неверного значения.
- **D2. Капитальные операции мимо отчёта о прибылях.** Тело займа через `EarnMoney` попало бы в выручку месяца и облагалось бы налогом (`TaxationHandler.HandleTaxes` берёт 25 % от прибыли прошлого месяца). `Company.AddMoney` двигает только баланс. Проценты — расход (`ExpenseOrigin.Interest`) и уменьшают налог, как в ванили.
  - *Отвергнуто:* `EarnMoney(…, RevenueOrigin.Interest)` для тела займа (ложная прибыль и налог).
- **D3. Последний шанс — префикс `TickBankruptcies` с удержанием счётчика.** Префикс ставит `DaysInDefault` игрока в `defaultTimeLimit − 2`; ванильный цикл увеличивает его до `defaultTimeLimit − 1` и не проверяет банкротство. Окно предложения модальное (пауза), поэтому следующего тика до решения нет. Во время пакетного прогона (`IsBatchRun`) счётчик удерживается на каждом дне, а окно открывается после `BatchEnded`.
  - *Отвергнуто:* префикс `BankruptcyHandler.Bankrupt`, возвращающий `false`: ванильный `TickBankruptcies` всё равно вызвал бы `OnCompanyWentBankrupt` для игрока сразу после `Bankrupt`, и подписчики (в том числе других фич) получили бы ложное событие банкротства.
  - *Отвергнуто:* «откат» после банкротства: `IsBankrupt` имеет `private set`, а `BankruptcyWindow` уже открыто.
- **D4. Досрочный выход из дефолта — постфикс + ванильный `CancelDefault`** (через рефлексию). Мод не переписывает ванильное правило, а только вызывает штатный метод отмены, когда серия восстановления достигла `streakDays`. Отключается настройкой `EarlyExit`.
  - *Отвергнуто:* transpiler, исправляющий ветку `DaysInDefault < defaultTimeLimit` (меняет поведение ИИ тоже, конфликтует с P10 ядра).
- **D5. Вход в меню — кнопка в ванильном окне предупреждения + действие письма + хаб.** Ванильное `BankruptcyWarningWindow` не заменяется, только дополняется кнопкой. Письмо несёт многоразовое действие `ModMailAction` «Меню спасения» (письмо не закрывается).
  - *Отвергнуто:* своё окно вместо ванильного (ломает привычный поток и звук, конфликт с `BankruptcyWarningWindow.OnDisable`, который ставит паузу).
- **D6. Продажа активов — ванильная математика и окна.** Линии — как `ManageLinesWindow.SellLines`, склад — `StorageMath.SellStock`, снятие — `Cpu.TriggerRetirement`, выход в fabless — ванильное окно `ManageLinesWindow` → `FablessConfirmationWindow`.
  - *Отвергнуто:* собственные «аварийные» цены (новые числа без опоры в игре, расхождение с ванильными окнами).
- **D7. Лимиты привязаны к `ModCredit` и выручке `R_3`, с полом.** Лимиты растут с инфляцией вместе с банком (`D = ModCredit.PlayerDebtLimit()`), коэффициенты зависят от сложности (контент). База лимитов `D_L = max(D, floorBase × Inflation)` страхует от случая, когда `CentralBank.DebtLimit` держится на полу 1 000 000 $ всю игру.
  - *Отвергнуто:* фиксированные суммы в долларах (обесцениваются к 1990-м: `DebtLimit` растёт с ~8 млн до ~650 млн к 2000 году).
- **D8. `CapitalLedger` — общий компонент.** Он без патчей и работает даже при выключенной фиче. `rival-acquisitions` использует его для оплаты сделок, чтобы правила денежных операций были одни. Журнал `CapitalLedger` принадлежит `bankruptcy-recovery` и хранится в её секции; операции других фич пишутся только в `ModLog` (у `rival-acquisitions` свой журнал сделок).
- **D9. Выключение фичи переводит обязательства в обычный долг.** `OnDisabled` списывает остаток (`Σ` остатков кредитов + выборка по линии + остаток договора с инвестором) через `CapitalLedger.CapitalOut`, закрывает кредиты, линию и договор. Подтверждение — `FeatureContext.ConfirmBeforeDisable` (хаб вызывает провайдер и, если он вернул не `null`, показывает `ModUI.Confirm`); программное выключение подтверждения не спрашивает, но перевод выполняет. Для договора с инвестором переводится остаток потолка `cap − paid` (договорный максимум), чтобы выключение никогда не было дешевле исполнения договора. Счётчик спасений и дата последнего бридж-кредита сохраняются, линия повторно не открывается — переключатель не даёт новых денег.
  - *Отвергнуто:* заморозка обязательств (нет процентов и платежей) — беспроцентные деньги: взять кредит и выключить фичу.
  - *Отвергнуто:* запрет `SupportsHotToggle` при долге — игрок застрянет с включённой фичей, а проверка нужна всё равно.
  - *Отвергнуто:* пересчёт процентов «за пропущенный период» при включении — сложно и не защищает от удаления мода.
- **D10. Лимиты мода не умножаются на `bank.creditLimit` повторно.** Множитель канала уже учтён в `ModCredit.PlayerDebtLimit()` один раз (решение владельца: без двойного применения). Пороги ИИ в `AIBehaviour*` этим множителем не затрагиваются.
- **D11. Проценты по линии — раз в месяц.** На `TickPhase.LateMonthPassed` списывается `drawn × r × M / 12` одной строкой в статье «Проценты» (решение владельца: месячные проценты проще читать в отчёте). Дневной тик нужен только для лимита, автозайма и предупреждения.
  - *Отвергнуто:* дневное начисление с накоплением в счётчике (лишнее поле состояния, выгода манёвра внутри месяца мала).
- **D12. Точка входа — `[ModFeature]`.** `BankruptcyRecoveryFeature : IModFeature` объявляет патчи, тики, участника сейва, параметры подфич, страницу хаба и подтверждение выключения в `Register(FeatureContext)`. Новая игра и загрузка — `ModTicks.GameReady(NewGame | Loaded)`, а не `Player.OnNewGame` и `SaveHandler.OnLoaded`.

## Architecture

### System Context

```mermaid
graph TB
    subgraph Game["Игра (Assembly-CSharp)"]
        DC[DateController<br/>LateTick / DayPassed / LateMonthPassed]
        BH[BankruptcyHandler<br/>TickBankruptcies / TriggerDefault / CancelDefault]
        CB[CentralBank.DebtLimit]
        INF[InflationHandler.Inflation]
        BWW[BankruptcyWarningWindow]
        CO[Company игрока<br/>AddMoney / EarnMoney / SpendMoney]
        PM[ProductionMath / StorageMath / Cpu]
        MLW[ManageLinesWindow]
    end
    subgraph Core["PTMod.Core (mod-framework)"]
        MT[ModTicks<br/>FeatureContext.OnTick / GameReady]
        MH[ModHooks]
        MS[ModSave<br/>IModSaveParticipant]
        MU[ModUI + хаб Моды<br/>Confirm / ConfirmBeforeDisable]
        MM[ModMail / ModNotify]
        MMod[ModModifiers bank.interest]
        MR[ModReputation]
        MCr[ModCredit]
        MC[ModContent / ModSettings / ModL10n / ModLog]
    end
    BR[PTMod.Features.BankruptcyRecovery]
    RA[rival-acquisitions]
    DC --> MT --> BR
    MH -->|патчи prefix / postfix| BH
    MH -->|патч| BWW
    MCr -.->|читает| CB
    BR -->|порог дефолта| MCr
    BR -->|читает| INF
    BR -->|деньги| CO
    BR -->|продажа| PM
    BR -->|открывает| MLW
    BR --> MS
    BR --> MU
    BR --> MM
    BR --> MMod
    BR --> MR
    BR --> MC
    RA -->|CapitalLedger| BR
```

### High-Level Architecture

```mermaid
graph LR
    subgraph Pure["Чистая логика Logic (без Unity, мини-раннер)"]
        LM[LoanMath]
        CLM[CreditLineMath]
        BM[BailoutMath]
        RR[RecoveryRules]
        DS[DisableSettlement]
        BAL[RecoveryBalance + Validate]
    end
    subgraph Adapters["Адаптеры к игре"]
        PF[PlayerFinanceAdapter : IPlayerFinance]
        CL[CapitalLedger]
    end
    subgraph Services["Сервисы фичи"]
        LS[LoanService]
        SCL[StartupCreditLineService]
        BLS[BridgeLoanService]
        BS[BailoutService]
        AS[AssetSaleService]
        MON[RecoveryMonitor]
        DSS[DisableSettlementService]
    end
    subgraph Hooks["Патчи"]
        P[BankruptcyPatches P1–P4]
    end
    subgraph UI["UI"]
        RW[RecoveryWindow]
        BOW[BailoutOfferWindow]
    end
    P --> BS
    P --> MON
    MON --> RW
    RW --> AS
    RW --> BLS
    RW --> SCL
    RW --> LS
    BOW --> BS
    LS --> LM
    SCL --> CLM
    BLS --> LM
    BS --> BM
    MON --> RR
    BS --> RR
    DSS --> DS
    DSS --> LS
    DSS --> SCL
    DSS --> BS
    LS --> CL
    SCL --> CL
    BLS --> CL
    BS --> CL
    DSS --> CL
    CL --> PF
    LS --> PF
```

### Используемые сервисы PTMod.Core

| Сервис | Для чего |
|---|---|
| `FeatureContext` / `ModFeatures` | Точка входа `IModFeature`: `ctx.Patch`, `ctx.OnTick`, `ctx.SaveParticipant`, `ctx.Param`, `ctx.HubPage`, `ctx.ConfirmBeforeDisable`; состояние фичи (`ModFeatures.IsActive`) |
| `ModHooks` | Регистрация патчей P1–P4, самопроверка целей (в том числе приватных членов для рефлексии), `ModHooks.Guard` на телах патчей, счётчик ошибок и `Faulted` — на стороне ядра |
| `ModTicks` | `ctx.OnTick(TickPhase.LateTick, …)` (автозаём, предупреждение, лимит линии), `ctx.OnTick(TickPhase.LateMonthPassed, …)` (проценты, платежи); `ModTicks.GameReady` (новая игра, загрузка); `ModTicks.IsBatchRun` / `BatchEnded`; защиты перемотки и загрузки — по умолчанию |
| `ModSave` | Секция `bankruptcy-recovery` через `IModSaveParticipant` (состояние кредитов, эпизода, журнала); поле `_schema` ведёт ядро |
| `ModContent` | `Content/bankruptcy-recovery/balance.json` (`LoadConfig`) |
| `ModSettings` | Параметры подфич (`ctx.Param`): `preWarning`, `startupCredit`, `bridgeLoan`, `bailout`, `earlyExit`; вкл/выкл фичи и флаг отладки — на стороне ядра |
| `ModUI` | Меню спасения, окно предложения, `ModUI.Confirm` (подтверждения), `ModUI.Tooltip` (причины недоступности), кнопка в `BankruptcyWarningWindow`, страница хаба «Финансы» |
| `ModMail` / `ModNotify` | Письма «Риск дефолта» и «Дефолт» (`Critical`, теги `rec:prewarning`, `rec:default`, действие «Меню спасения»), уведомления о погашении, автозайме и переводе в обычный долг |
| `ModModifiers` | Чтение канала `bank.interest` для игрока — множитель ставок кредитов мода (записей и провайдеров фича не регистрирует) |
| `ModReputation` | `Add(игрок, −10, "rec.reputation.bailout")` при спасении |
| `ModCredit` | `PlayerDebtLimit()`, `ThresholdOf(игрок)`, `IsPerCompanyActive` — единственный источник порога дефолта |
| `ModL10n` / `ModLog` | Строки ru/en (префикс `rec.`); лог `[PTMod] [bankruptcy-recovery] …` |

### Technology Stack

| Слой | Технология | Обоснование |
|---|---|---|
| Логика | C# latest, Roslyn `csc` из VS Build Tools (решение 0003); `Logic` — общее подмножество API .NET Framework 4.8 и `mscorlib` Unity (без `init`, `record`, `Index`, `Range`) | Сборка `mod/build/PTMod.dll` через `python mod/build.py` |
| Патчи | Harmony 2.3.6 (решение 0002) | Приватные методы `BankruptcyHandler` и `BankruptcyWarningWindow.Open` |
| Рефлексия | `HarmonyLib.AccessTools` | Вызов `CancelDefault`, чтение `defaultTimeLimit` |
| UI | `ModUI` на шаблонах игры (решение 0005) | Родной вид, темы, пауза |
| Хранилище | `ModSave` (решение 0004) | Сейв игры остаётся ванильным |
| Контент | `ModContent` JSON (решение 0006) | Баланс без пересборки |
| Тесты | Свой мини-раннер на .NET Framework 4.8 (`[Test]`, `[Covers]`, `Assert.*`), `python mod/build.py --tests` | Чистая логика без Unity; xUnit не используется |

## Components and Interfaces

Пространство имён: `PTMod.Features.BankruptcyRecovery` (чистая логика — `PTMod.Features.BankruptcyRecovery.Logic`). Каталог: `mod/src/PTMod/Features/BankruptcyRecovery/` (логика — `…/Logic/`). Тесты — `mod/tests/PTMod.Tests/BankruptcyRecovery/`.

### BankruptcyRecoveryFeature

**Purpose**: точка входа фичи по контракту ядра: объявляет патчи, тики, участника сейва, параметры, страницу хаба и подтверждение выключения; владеет состоянием.

```csharp
[ModFeature]
public sealed class BankruptcyRecoveryFeature : IModFeature
{
    public const string FeatureId = "bankruptcy-recovery";
    public string Id => FeatureId;
    public string NameKey => "rec.feature.name";
    public string DescriptionKey => "rec.feature.description";
    public string[] DependsOn => new string[0];     // жёстких зависимостей нет
    public bool EnabledByDefault => true;
    public bool SupportsHotToggle => true;

    internal static BankruptcyRecoveryFeature Instance { get; private set; }
    internal RecoveryBalance Balance { get; private set; }   // ModContent.LoadConfig, после Validate()
    internal RecoveryState State { get; private set; }       // секция ModSave
    internal IPlayerFinance Finance { get; private set; }    // PlayerFinanceAdapter

    public void Register(FeatureContext ctx)
    {
        ctx.Patch(typeof(BankruptcyPatches));                              // P1–P4
        ctx.OnTick(TickPhase.LateTick, RecoveryTicks.OnDay);               // автозаём, лимит, предупреждение
        ctx.OnTick(TickPhase.LateMonthPassed, RecoveryTicks.OnMonth);      // проценты, платежи, гашение, срок линии, инвестор
        ctx.SaveParticipant(new RecoverySaveParticipant(this));
        ctx.Param(ParamSpecs.PreWarning);  ctx.Param(ParamSpecs.StartupCredit);   // ParamType.Bool, по умолчанию true
        ctx.Param(ParamSpecs.BridgeLoan);  ctx.Param(ParamSpecs.Bailout);
        ctx.Param(ParamSpecs.EarlyExit);
        ctx.HubPage("rec.hub.title", RecoveryHubPage.Build);               // раздел «Финансы»
        ctx.ConfirmBeforeDisable(DisableSettlementService.BuildConfirmation);
        ModTicks.GameReady += OnGameReady;                                 // NewGame: открыть линию; Loaded: перевод при выключенной фиче
    }

    public void OnEnabled();      // линия, если состояние «не открыта» и срок не вышел; сбросить сессионные флаги
    public void OnDisabled();     // DisableSettlementService.Settle(): остаток обязательств → обычный долг; снять удержание счётчика
    void OnGameReady(GameReadyReason reason);
}

public enum SubFeature { PreWarning, StartupCredit, BridgeLoan, Bailout, EarlyExit }
```

**Implementation Notes**:
- Первый шаг каждого обработчика тика и патча — проверка `ModFeatures.IsActive(FeatureId)` (горячее выключение действует сразу) и, для подфичи, `ModSettings.GetBool(FeatureId, key)`.
- Патчи и обработчики вызываются через `ModHooks.Guard(FeatureId, siteId, …)`: исключение не попадает в игру, счётчик ошибок ведёт ядро.
- `IsEnabled(SubFeature)`: фича активна и параметр подфичи `true`. Подфича управляет только **новыми** операциями; обслуживание уже взятых кредитов идёт всегда, пока фича включена (FR-12 п. 2).
- Обработчик `ModTicks.GameReady` подписан в `Register` и вызывается независимо от состояния фичи: при `Disabled`/`Unavailable` он только переводит обязательства из секции в обычный долг (FR-12 п. 5). Допущение: ядро вызывает `Register` для всех найденных фич и читает секции всех участников сейва, в том числе выключенных; TSK-022 проверяет это, при расхождении перевод при загрузке не работает и остаётся только путь через хаб (README требует выключать фичу в хабе).

### RecoveryApi (публичный, для UI и других спек)

```csharp
public static class RecoveryApi
{
    public static bool IsActive { get; }                         // ModFeatures.IsActive("bankruptcy-recovery")
    public static RecoverySummary GetSummary();                  // баланс, порог, дефицит, дни, долг, ближайший платёж
    public static IReadOnlyList<LoanView> GetLoans();
    public static CreditLineView GetCreditLine();
    public static int BailoutsUsed { get; }
    public static void OpenMenu(RecoveryTab tab = RecoveryTab.Summary);

    public static event Action<LoanView> LoanOpened;
    public static event Action<LoanView> LoanClosed;
    public static event Action<BailoutKind> BailoutAccepted;     // для статистики; флаг assisted спасение не ставит
}

public enum RecoveryTab { Summary, Assets, Loans, Journal }
public enum BailoutKind { RescueLoan, InvestorShare }
public enum LoanKind { Bridge, Term, RescueLoan }

public struct RecoverySummary
{
    public double Money, DebtLimit, DeficitToThreshold, LoansOutstanding, NextPayment;
    public bool InDefault; public int DaysToBankruptcy; public string NextPaymentDate;
}
public struct LoanView { public string Id; public LoanKind Kind; public double Outstanding, AnnualRate, Payment; public int MonthsLeft; }
public struct CreditLineView { public bool Open; public double Limit, Drawn, Available; public string ClosesOn; public bool AutoDraw; }
```

### CapitalLedger (общий с rival-acquisitions)

**Purpose**: единые правила денежных операций мода. Без патчей; работает даже при выключенной фиче.

```csharp
public static class CapitalLedger
{
    // Капитальные операции: мимо отчёта о прибылях и налога (Company.AddMoney)
    public static void CapitalIn(string featureId, double amount, string op, string details);   // +баланс
    public static void CapitalOut(string featureId, double amount, string op, string details);  // −баланс
    // Расходы через отчёт (Company.SpendMoney): проценты, комиссии, доля инвестора
    public static void Expense(string featureId, double amount, ExpenseOrigin origin, string op, string details);
    // Доходы через отчёт (Company.EarnMoney): например, выручка от продажи линий
    public static void Revenue(string featureId, double amount, RevenueOrigin origin, string op, string details);

    // Журнал bankruptcy-recovery (записи с featureId == "bankruptcy-recovery"), последние 200
    public static IReadOnlyList<JournalEntryData> Journal { get; }
    public const int JournalCapacity = 200;
}
```

**Implementation Notes**:
- Только компания игрока: `Player.Instance.Company` приводится к `Company` (для `AddMoney`). Для ИИ методы не предназначены; ИИ-деньги в `rival-acquisitions` меняются через `ICompany.set_MoneyAmount` и в журнал не пишутся.
- `featureId` — вызывающая фича: операции `bankruptcy-recovery` попадают в её журнал (кольцевой буфер на 200 записей, FR-10), операции других фич — только в `ModLog` (`[PTMod] [<featureId>] INFO capital: …`).
- Суммы — `double`, в игру уходит `(float)`; суммы `≤ 0`, `NaN`, `∞` отклоняются с записью в `ModLog`.

### IPlayerFinance / PlayerFinanceAdapter

```csharp
internal interface IPlayerFinance
{
    double Money { get; }                 // Player.Instance.Company.MoneyAmount
    double DebtLimit { get; }             // ModCredit.PlayerDebtLimit() — порог дефолта игрока без знака
    double Threshold { get; }             // ModCredit.ThresholdOf(игрок): отрицательное число
    double Inflation { get; }             // InflationHandler.Instance.Inflation
    bool IsInDefault { get; }             // ICompany.IsInDefault
    bool IsBankrupt { get; }
    int DaysInDefault { get; set; }       // ICompany.DaysInDefault (публичный сеттер)
    int DefaultTimeLimit { get; }         // BankruptcyHandler.defaultTimeLimit (рефлексия, 120)
    int Difficulty { get; }               // Player.Instance.DifficultyLevel
    DateTime Today { get; }               // ModTicks.Today
    DateTime FoundationDate { get; }      // Company.FoundationDate (Date → DateTime)
    int ProductionCapacity { get; }       // Company.Factory.ProductionCapacity
    double SalesRevenueLastFullMonths(int months);  // Σ RevenueBySales + RevenueByContracts
    double NetProfitOfMonth(int year, int month);   // TotalRevenues − TotalExpenses отчёта месяца
    double InterestMultiplier { get; }    // ModModifiers.Get(ModChannels.BankInterest, {Company = игрок})
    void CancelDefault();                 // BankruptcyHandler.CancelDefault(игрок) через AccessTools
}
```

**Implementation Notes**:
- Отчёты ищутся в `CompanyFinance.Reports` по паре `Year`/`Month` (а не по индексу), потому что порядок обработчиков `LateMonthPassed` (наш и `CompanyFinance.NewReport`) не определён.
- Порог и `DebtLimit` читаются при каждом обращении (без кэша дольше тика); `CentralBank.Instance.DebtLimit` напрямую не читается.
- В тестах подменяется `FakePlayerFinance`.

### Чистая логика

Все типы `Logic` публичные (мини-раннер видит их без `InternalsVisibleTo`) и перечислены в `[Covers]` соответствующих тестов.

```csharp
namespace PTMod.Features.BankruptcyRecovery.Logic
{
    public static class LoanMath
    {
        public static double AnnuityPayment(double principal, double annualRate, int months);
        public static double MonthlyInterest(double outstanding, double annualRate);   // outstanding × annualRate / 12
        public static LoanStep MonthStep(double outstanding, double payment, double annualRate, int monthsLeft);
    }
    public struct LoanStep { public double Interest; public double Principal; public double OutstandingAfter; }

    public static class CreditLineMath
    {
        public static double LimitBase(double debtLimit, double inflation, double floorBase);          // max(D, floorBase × I) = D_L
        public static double Limit(StartupCreditConfig c, int difficulty, double limitBase, double r3);
        public static double AutoDraw(StartupCreditConfig c, double money, double debtLimit, double available);
        public static double Sweep(StartupCreditConfig c, double money, double drawn);
        public static bool IsWindowOpen(StartupCreditConfig c, DateTime foundation, DateTime today);   // foundation ≤ today < foundation + years
    }

    public static class BailoutMath
    {
        public static double Amount(BailoutConfig c, double money, double debtLimit);
        public static int Allowed(BailoutConfig c, int difficulty);                                    // perGameByDifficulty[d]
        public static double InvestorPayment(InvestorConfig c, double monthProfit, double paid, double cap);
    }

    public static class RecoveryRules
    {
        public static bool MustHoldForBailout(in BankruptcyView v, int bailoutsUsed, int bailoutsAllowed, bool declined);
        public static PreWarningDecision PreWarning(in BankruptcyView v, bool alreadySent, PreWarningConfig c);
        public static int NextRecoveryStreak(in BankruptcyView v, int streak);
        public static Eligibility CanTakeBridge(in BankruptcyView v, bool hasActiveBridge, DateTime? lastClosed, BridgeConfig c);
    }

    public static class DisableSettlement
    {
        // Сумма, которая становится обычным долгом при выключении фичи (D9)
        public static double Total(IReadOnlyList<LoanData> loans, double lineDrawn, InvestorShareData investor);
        public static DisableBreakdown Breakdown(IReadOnlyList<LoanData> loans, double lineDrawn, InvestorShareData investor);
    }
    public struct DisableBreakdown { public double Loans, LineDrawn, InvestorRemainder, Total; }

    public readonly struct BankruptcyView
    {
        public readonly double Money, DebtLimit;          // DebtLimit — порог без знака (ModCredit); порог дефолта = −DebtLimit
        public readonly bool InDefault, IsBankrupt;
        public readonly int DaysInDefault, DefaultTimeLimit; public readonly DateTime Today;
    }
    public enum PreWarningDecision { None, Send, Reset }
    public struct Eligibility { public bool Ok; public string ReasonKey; }
}
```

### Сервисы

```csharp
internal sealed class LoanService
{
    public LoanView Open(LoanKind kind, double principal, double annualRate, int months, string op);
    public void OnMonth();                  // LateMonthPassed: платёж по каждому кредиту
    public void Prepay(string loanId, double amount);
    public double Outstanding { get; }
}

internal sealed class StartupCreditLineService
{
    public void EnsureOpened();             // GameReady(NewGame|Loaded) и OnEnabled: состояние «не открыта» + срок не вышел + подфича включена
    public void OnDay();                    // LateTick: лимит, автозаём
    public void OnMonth();                  // LateMonthPassed: проценты, гашение sweep, закрытие по сроку → срочный кредит
    public void Draw(double amount);
    public void Repay(double amount);
    public double Limit { get; } public double Available { get; }
}

internal sealed class BridgeLoanService
{
    public Eligibility Check();
    public double MaxAmount();
    public void Take(double amount);        // CapitalIn(A), Expense(fee), LoanService.Open(Bridge)
}

internal sealed class BailoutService
{
    public bool IsOfferOpen { get; }
    public void OnHoldRequested();          // из P1: открыть BailoutOfferWindow один раз (при IsBatchRun — после BatchEnded)
    public void Accept(BailoutKind kind);   // CapitalIn(B), CancelDefault, счётчик, репутация
    public void Decline();                  // флаг эпизода bailoutDeclined = true
    public void OnMonth();                  // доля инвестора
}

internal sealed class AssetSaleService
{
    public IReadOnlyList<WaferLinesView> ListWafers();               // Company.WaferSizes + Factory.AmountOfLines
    public LineSaleQuote QuoteLines(WaferSize wafer, int lines);     // ProductionMath.SaleCost, предупреждения
    public void SellLines(LineSaleQuote q);                          // RemoveProductionLines + EarnMoney(Sales)
    public IReadOnlyList<CpuAssetView> ListCpus();                   // не снятые CPU игрока, сортировка по марже
    public StockQuote QuoteStock(Cpu cpu);                           // StorageMath.Income
    public void SellStock(Cpu cpu);                                  // StorageMath.SellStock
    public RetireQuote QuoteRetire(Cpu cpu);                         // ContractHelper.FinesFromContracts + StorageMath.Income
    public void Retire(Cpu cpu);                                     // Cpu.TriggerRetirement
    public DivisionQuote QuoteDivision();                            // ProductionMath.ManufacturingDivisionValue
    public void OpenManageLines();                                   // активирует окно ManageLinesWindow.Instance
}

internal sealed class RecoveryMonitor
{
    public void OnDay();                    // LateTick: предупреждение FR-1
    public void OnPlayerDefault();          // из P3: письмо rec:default
}

internal static class DisableSettlementService
{
    public static DisableConfirmation BuildConfirmation();   // null, если обязательств нет (ConfirmBeforeDisable)
    public static void Settle(string reason);                // OnDisabled и GameReady(Loaded) при Disabled/Unavailable: CapitalOut(Total), закрыть кредиты/линию/договор, журнал
}
```

**Implementation Notes**:
- Продажа линий повторяет приватный `ManageLinesWindow.SellLines`: `ProductionMath.SaleCost(N, W)`, `Company.RemoveProductionLines(N, W)`, `Company.EarnMoney(…, RevenueOrigin.Sales)`, затем `ManageLinesWindow.UpdateWafers()`. Верхняя граница `N`: `AmountOfLines(W)`, минус 1, если это вся мощность (как `ManageLinesWindow.UpdateSlider`).
- Предупреждение о контрактах: сравнить мощность после продажи с `ContractHelper.ContractedProductionLines(Player.Instance)`; если меньше — показать риск штрафов.
- Опция «Продать производство» неактивна при `IsFoundry`, `ProductionCapacity < 1` или `DifficultyLevel == 5` (условия кнопки `sellDivisionButton` в `ManageLinesWindow.UpdateUI`).
- `BuildConfirmation` возвращает `DisableConfirmation { MessageKey = "rec.disable.confirm", Args = [остаток, баланс после, предупреждение о дефолте], ConfirmLabelKey = "rec.disable.confirmLabel" }`; суммы уже отформатированы `StringFormatter.FloatToMoney`.
- `Settle` идемпотентна: после выполнения обязательств нет, повторный вызов ничего не делает. Порядок: пересчёт `Total` → `CapitalOut(Total)` одной операцией → очистка `loans`, `creditLine` (`state = "closed"`, `closedReason = "disabled"`), `investorShare.active = false` → `lastBridgeClosedOn = сегодня`, если закрывался бридж-кредит → запись журнала `disable.convert` → `ModNotify`.

### Патчи Harmony

| # | Цель `Class.Method` | Тип | Что меняет | Порядок / конфликты |
|---|---|---|---|---|
| P1 | `BankruptcyHandler.TickBankruptcies` | prefix (всегда возвращает `true`) | Только игрок: если `RecoveryRules.MustHoldForBailout` — ставит `DaysInDefault = defaultTimeLimit − 2` и просит `BailoutService` открыть предложение (один раз). Пока предложение открыто — удерживает счётчик каждый вызов | `Priority.High`. Ядро держит на этом же методе transpiler P10 (порог дефолта по компании): они совместимы, потому что prefix выполняется до оригинала, а порог берётся из `ModCredit.ThresholdOf`; резерв ядра касается только transpiler'а. Другие фичи этот метод не патчат; `rival-acquisitions` использует только событие `OnCompanyWentBankrupt` |
| P2 | `BankruptcyHandler.TickBankruptcies` | postfix | Только игрок: серия досрочного выхода (FR-9); при `streak ≥ streakDays` → `CancelDefault(игрок)` через рефлексию | Выполняется после оригинала; не трогает ИИ |
| P3 | `BankruptcyHandler.TriggerDefault` | postfix | Если `company.IsPlayer`: `RecoveryMonitor.OnPlayerDefault()` (письмо) | `rival-acquisitions` ставит свой postfix на ветку `!IsPlayer`; независимы, порядок не важен |
| P4 | `BankruptcyWarningWindow.Open` | postfix | Один раз за сцену добавляет в окно кнопку «Меню спасения» (клон `ButtonUI` через `ModUI`) | Флаг `hasShownWarningToThePlayer` в коде не сбрасывается после первого показа, поэтому ванильное окно появляется только в первом эпизоде после загрузки сцены. Если окно не показано, постфикс ничего не делает; основной вход в меню — действие письма (FR-2) и хаб |

Все четыре патча: первой строкой `ModFeatures.IsActive`, тело в `ModHooks.Guard`; prefix при сбое возвращает `true` (ванильный путь).

**Члены игры, используемые через рефлексию** (проверяются самопроверкой `ModHooks`):

| Член | Вид | Зачем |
|---|---|---|
| `BankruptcyHandler.CancelDefault(ICompany)` | private method | Снять дефолт игрока при спасении (FR-8) и досрочном выходе (FR-9) — ванильный баннер «успех» и звук |
| `BankruptcyHandler.defaultTimeLimit` | private field `int` | Порог дней дефолта (120) для правил FR-8 и сводки |

### Подписки и тики

| Источник | Обработчик | Защиты |
|---|---|---|
| `ctx.OnTick(TickPhase.LateTick, …)` | `StartupCreditLineService.OnDay` (лимит, автозаём), `RecoveryMonitor.OnDay` (предупреждение) | Перемотка и загрузка пропускаются ядром (`TickOptions.None`); фича/подфича выключена → выход; игрок банкрот → выход |
| `ctx.OnTick(TickPhase.LateMonthPassed, …)` | `LoanService.OnMonth`, `StartupCreditLineService.OnMonth`, `BailoutService.OnMonth` | Те же; отчёты ищутся по `Year`/`Month` |
| `ModTicks.GameReady` (`NewGame`) | `StartupCreditLineService.EnsureOpened` | Приходит после перемотки; подписка статическая (в `Register`), переустанавливать её на сцене не нужно |
| `ModTicks.GameReady` (`Loaded`) | сброс сессионного флага «предложение открыто»; `EnsureOpened` для сейва без секции; `DisableSettlementService.Settle`, если фича `Disabled`/`Unavailable` и есть обязательства (FR-12 п. 5) | Нет файла/секции → значения по умолчанию |
| `IModSaveParticipant.Read` / `Write` | `RecoverySaveParticipant` | Версию секции (`_schema`) ведёт ядро |
| `ModTicks.BatchEnded` | `BailoutService`: открыть отложенное окно предложения | Только если удержание было запрошено в пакетном прогоне |

Порядок дня важен: `LateTick` (автозаём) идёт до `DayPassed` (`BankruptcyHandler.TickBankruptcies`), поэтому автозаём успевает поднять баланс до проверки дефолта.

## Data Models

### Состояние (секция ModSave)

Участник `RecoverySaveParticipant : IModSaveParticipant` (`SectionId = "bankruptcy-recovery"`, `SchemaVersion = 1`) переводит DTO в `JsonObject` и обратно через `ModJson.From`/`ModJson.Bind`. Поля версии (`schema`) в DTO нет: `_schema` пишет и читает ядро.

```csharp
[Serializable] public sealed class RecoveryState      // PTMod.Features.BankruptcyRecovery.Logic
{
    public CreditLineData creditLine = new CreditLineData();
    public List<LoanData> loans = new List<LoanData>();
    public int bailoutsUsed;
    public InvestorShareData investorShare = new InvestorShareData();
    public EpisodeData episode = new EpisodeData();
    public string lastBridgeClosedOn = "";          // "yyyy-MM-dd" или ""
    public int nextLoanSeq = 1;
    public List<JournalEntryData> journal = new List<JournalEntryData>();
}

[Serializable] public sealed class CreditLineData
{
    public string state = "none";                   // "none" (не открывалась) | "open" | "closed"
    public string opensOn = ""; public string closesOn = "";
    public double drawn; public bool autoDraw = true;
    public string closedReason = "";                // "expired" (остаток → срочный кредит) | "disabled" (остаток → обычный долг)
}

[Serializable] public sealed class LoanData
{
    public string id; public string kind;            // "Bridge" | "Term" | "RescueLoan"
    public double principal; public double outstanding; public double annualRate;
    public int monthsTotal; public int monthsLeft; public double payment; public string openedOn;
}

[Serializable] public sealed class InvestorShareData
{
    public bool active; public double amount; public double share;
    public int monthsLeft; public double paid; public double cap;
}

[Serializable] public sealed class EpisodeData
{
    public bool preWarningSent; public bool defaultMailSent;
    public int recoveryStreak; public bool bailoutDeclined;
    // флаг «предложение открыто» НЕ сохраняется (сессионный)
}

[Serializable] public sealed class JournalEntryData
{
    public string date; public string op; public double amount; public string details;
}
```

**Validation Rules** (при чтении `ModSave`): `drawn ≥ 0`; `state` ∈ {`none`, `open`, `closed`}; у кредита `0 ≤ outstanding ≤ principal × 1,01`, `0 ≤ annualRate ≤ 2`, `0 ≤ monthsLeft ≤ monthsTotal ≤ 120`; `0 ≤ share ≤ 1`; даты разбираются `yyyy-MM-dd`, иначе запись отбрасывается с `ModLog`. Секцию новее мода ядро отдаёт как значения по умолчанию и сохраняет исходную без изменений.

**Relationships**: состояние принадлежит одному сейву (боковой файл `<save>.ptmod.json`). Ссылок на объекты игры нет: только деньги и даты. ID писем не хранится: письма ищутся по тегу (`ModMail.Find`).

### Пример секции бокового файла

Ядро добавляет в каждую секцию служебное поле версии (`_schema`); в DTO фичи его нет.

```json
{
  "schema": 1,
  "modVersion": "0.1.0",
  "features": {
    "bankruptcy-recovery": {
      "creditLine": { "state": "open", "opensOn": "1975-01-01", "closesOn": "1978-01-01",
                      "drawn": 2660000.0, "autoDraw": true, "closedReason": "" },
      "loans": [
        { "id": "bridge-1", "kind": "Bridge", "principal": 8400000.0, "outstanding": 6527277.0,
          "annualRate": 0.30, "monthsTotal": 12, "monthsLeft": 9, "payment": 818892.0,
          "openedOn": "1976-03-01" }
      ],
      "bailoutsUsed": 0,
      "investorShare": { "active": false, "amount": 0.0, "share": 0.0, "monthsLeft": 0, "paid": 0.0, "cap": 0.0 },
      "episode": { "preWarningSent": true, "defaultMailSent": true, "recoveryStreak": 0, "bailoutDeclined": false },
      "lastBridgeClosedOn": "",
      "nextLoanSeq": 2,
      "journal": [
        { "date": "1976-03-01", "op": "bridge.open", "amount": 8400000.0, "details": "12m @ 30%" },
        { "date": "1976-03-01", "op": "bridge.fee", "amount": -420000.0, "details": "" },
        { "date": "1976-04-01", "op": "loan.payment", "amount": -818892.0, "details": "bridge-1" }
      ]
    }
  }
}
```

### Контент: `Content/bankruptcy-recovery/balance.json`

Версия `schema` здесь — версия файла контента (`ModContent.LoadConfig(…, supportedSchema)`), а не секции сейва.

```json
{
  "schema": 1,
  "limits": { "floorBase": 2000000.0 },
  "preWarning": { "thresholdDebtLimitShare": 0.75, "resetDebtLimitShare": 0.5 },
  "earlyExit": { "streakDays": 7 },
  "startupCredit": {
    "years": 3,
    "baseDebtLimitShareByDifficulty": [1.5, 1.25, 1.0, 0.75, 0.5, 0.25],
    "revenueShare": 0.5,
    "capDebtLimitShare": 3.0,
    "annualRate": 0.10,
    "autoDrawTriggerShare": 0.9,
    "autoDrawTargetShare": 0.6,
    "sweepShare": 0.25,
    "termMonths": 24,
    "termAnnualRate": 0.15
  },
  "bridgeLoan": {
    "eligibleDebtLimitShare": 0.75,
    "baseDebtLimitShare": 1.0,
    "revenueShare": 0.25,
    "fee": 0.05,
    "annualRate": 0.30,
    "months": 12,
    "cooldownDays": 180
  },
  "bailout": {
    "perGameByDifficulty": [2, 1, 1, 1, 1, 0],
    "targetDebtLimitShare": 0.25,
    "loan": { "annualRate": 0.40, "months": 36 },
    "investor": { "profitShare": 0.30, "months": 60, "capMultiple": 3.0, "expenseOrigin": "Royalties" },
    "reputationDelta": -10
  },
  "assets": { "minLinesLeft": 1 }
}
```

| Поле | Допустимо | По умолчанию | Что задаёт |
|---|---|---|---|
| `limits.floorBase` | 0…1e9 | 2 000 000 | Пол базы лимитов: `D_L = max(D, floorBase × Inflation)` |
| `*.annualRate` | 0…2 | см. выше | Годовая ставка до множителя `bank.interest` |
| `*DebtLimitShare` | 0…10 | см. выше | Доля от `D` (порога дефолта игрока) |
| `baseDebtLimitShare`, `baseDebtLimitShareByDifficulty` | 0…10 | см. выше | Доля от базы лимитов `D_L` |
| `*ByDifficulty` | массив из 6 значений ≥ 0 | см. выше | Индекс — `Player.DifficultyLevel` (0…5); `perGameByDifficulty` — целые |
| `months`, `termMonths` | 1…120 | 12 / 24 / 36 / 60 | Срок аннуитета |
| `fee`, `revenueShare`, `sweepShare`, `profitShare` | 0…1 | см. выше | Доли |
| `streakDays` | 1…120 | 7 | Дней подряд выше порога |
| `cooldownDays` | 0…3650 | 180 | Пауза между бридж-кредитами |
| `expenseOrigin` | `Royalties` \| `Interest` | `Royalties` | Статья расхода доли инвестора (решение владельца: `Royalties`) |

Битая запись (вне диапазона, не тот тип) → значение по умолчанию поля + строка в `ModLog`.

## Формулы и баланс

Обозначения: `D = ModCredit.PlayerDebtLimit()` (порог дефолта игрока без знака, живое значение; ровно то, что применяет игра), `I = InflationHandler.Instance.Inflation`, `D_L = max(D, floorBase × I)` — база лимитов мода, `M = ModModifiers`-канал `bank.interest` для игрока (по умолчанию 1), `money = Company.MoneyAmount` игрока, `T = BankruptcyHandler.defaultTimeLimit = 120`, `R_3` — выручка от продаж и контрактов (`RevenueBySales + RevenueByContracts`) за 3 последних полных месяца.

### Ванильная основа (не меняется модом)

- **Порог дефолта** (`CentralBank.UpdateCreditLimit`): `DebtLimit = max(1 000 000, baseCreditLimit × I² × (1,5 + clamp(год − 1971, 0, 60) / 25) − 17 500 000)`, `I = InflationHandler.Inflation` (десятилетия: 1970-е 0,5; 1980-е 2,0; 1990-е 3,0; 2000-е 5,0; 2010-е 6,5). Ядро может умножить его на `bank.creditLimit` глобально (P9) и по компании (P10); `ModCredit.PlayerDebtLimit()` отдаёт итог.
- **Ориентиры `D` при `baseCreditLimit = 1e7`**: 1971 — 1,0 млн; 1975 — 8,4 млн; 1977 — 24,3 млн; 1980 — 56,9 млн; 1985 — 111,3 млн; 1990 — 185,9 млн; 2000 — 647,5 млн; 2010 — 1 275 млн. Если `baseCreditLimit = 0` (не восстановлен из сцены), `DebtLimit = 1 000 000` всю игру — проверяет спайк TSK-003; тогда лимиты держит пол `floorBase × I` (1980-е: 4 млн, 1990-е: 6 млн).
- **Проценты банка на минус** (`CentralBank.TaxInterest`): `|money| × 0,25 / 365 × Modifiers.GetModifier(6)` в день — 25 % годовых. Мод это не меняет; кредиты мода сравниваются с этой ставкой.
- **Дефолт и банкротство** (`BankruptcyHandler.TickBankruptcies`): `money < порог` → дефолт; на `DaysInDefault ≥ 120` → банкротство, если `money < порог`, иначе `CancelDefault`.

### Предупреждение (FR-1)

- Отправить, если `¬InDefault ∧ money < −0,75·D ∧ ¬preWarningSent`.
- Сбросить эпизод, если `money ≥ −0,5·D`.

### Стартовая кредитная линия (FR-7)

- Окно: `FoundationDate ≤ today < FoundationDate + 3 года`; срок закрытия `closesOn`.
- Лимит: `L = clamp(a_d·D_L + 0,5·R_3, 0, 3·D_L)`, `a_d = [1,5; 1,25; 1,0; 0,75; 0,5; 0,25][DifficultyLevel]`.
- Свободно: `avail = max(0, L − drawn)`.
- Автозаём (в `LateTick`): если `money < −0,9·D` → `Δ = min(avail, −0,6·D − money)`, `CapitalIn(Δ)`, `drawn += Δ`.
- Проценты раз в месяц (`LateMonthPassed`): `drawn × 0,10·M / 12` → `Expense(Interest)`.
- Гашение раз в месяц после процентов: `rep = min(drawn, 0,25·max(0, money))` → `CapitalOut(rep)`.
- После срока (`today ≥ closesOn`): новые выборки закрыты; на `LateMonthPassed` после процентов `drawn` → срочный кредит, аннуитет 24 мес. под `0,15·M`.
- **Пример (нормально, 1975):** `D = 8,4 млн`, `D_L = 8,4 млн`, `R_3 = 0` → `L = 8,4 млн`. Через полгода CPU продаёт 1,5 млн/мес → `R_3 = 4,5 млн` → `L = 10,65 млн`. Баланс −7,7 млн < −7,56 млн → автозаём 2,66 млн → баланс −5,04 млн. Проценты при `drawn = 2,66 млн`: ≈ 22 167 $ в месяц (одна строка в отчёте) против ≈ 103 600 $ в месяц, которые банк взял бы с −5,04 млн.
- **Пример пола:** `DebtLimit = 1 млн` (пол банка), 1985 год (`I = 2,0`) → `D_L = max(1, 2 × 2,0) = 4 млн`, `L = 4 млн`; автозаём срабатывает от `−0,9 млн`.

### Аннуитет (FR-6, FR-7, FR-8)

- `i = r·M / 12`; `P = A·i / (1 − (1 + i)^(−n))`; при `i = 0` → `P = A / n`.
- Месячный шаг: `interest = outstanding·i` → `Expense(Interest)`; `principal = min(outstanding, P − interest)` (в последнем месяце — весь остаток) → `CapitalOut(principal)`.

### Бридж-кредит (FR-6)

- Доступен, если `(money < −0,75·D ∨ InDefault) ∧ нет активного ∧ (today − lastBridgeClosedOn ≥ 180 дн.) ∧ подфича включена`.
- `A_max = 1,0·D_L + 0,25·R_3`; игрок выбирает 25/50/75/100 % (`SliderUI`).
- Комиссия `0,05·A` → `Expense(Interest)`; кредит: `r = 0,30·M`, `n = 12`.
- **Пример:** `D = D_L = 8,4 млн`, `A = 8,4 млн`, `i = 0,025` → `P ≈ 818 892 $`; всего выплат ≈ 9,83 млн + комиссия 0,42 млн.

### Последний шанс (FR-8)

- Условие удержания (P1): `IsPlayer ∧ InDefault ∧ DaysInDefault + 1 ≥ T ∧ money < ThresholdOf(игрок) ∧ bailoutsUsed < Allowed[d] ∧ ¬bailoutDeclined` → `DaysInDefault := T − 2`. `Allowed = [2, 1, 1, 1, 1, 0]`.
- Сумма: `B = 0,25·D − money` (баланс после — `+0,25·D`, выше порога).
- «Кредит спасения»: аннуитет `n = 36`, `r = 0,40·M`.
- «Доля инвестора»: каждый месяц `pay = min(cap − paid, 0,30·max(0, profit_prev))`, `cap = 3·B`, 60 месяцев → `Expense(Royalties)`.
- После принятия: `CapitalIn(B)`, `CancelDefault(игрок)`, `bailoutsUsed += 1`, `ModReputation.Add(игрок, −10, "rec.reputation.bailout")`.
- **Пример (1977):** `D = 24,3 млн`, `money = −30 млн` → `B = 36,075 млн`; кредит спасения: `P ≈ 1 735 600 $/мес`, всего ≈ 62,5 млн; доля инвестора: при прибыли 4 млн/мес платёж 1,2 млн/мес, потолок 108,2 млн.

### Досрочный выход (FR-9)

- `streak = (InDefault ∧ money ≥ ThresholdOf(игрок)) ? streak + 1 : 0`; при `streak ≥ 7` → `CancelDefault(игрок)`, `streak = 0`.

### Выключение фичи (FR-12, D9)

- `Total = Σ outstanding(loans) + drawn(creditLine) + max(0, cap − paid)(investorShare)`.
- `OnDisabled`: `CapitalOut(Total)`; баланс после = `money − Total`; если оно ниже порога — подтверждение предупреждает о риске дефолта.
- **Пример:** баланс +2 млн, бридж-кредит с остатком 5 млн → баланс −3 млн, дальше ванильные 25 % годовых банка на минус; кредит закрыт, `lastBridgeClosedOn = сегодня`.

### Что из ванили мод меняет

Ничего из формул и констант. Мод меняет только **поток денег игрока** (новые операции через `AddMoney`/`SpendMoney`/`EarnMoney`) и **один момент** цикла банкротства игрока (удержание счётчика в P1, вызов `CancelDefault` в P2).

## UI

Все окна строит `ModUI` клоном шаблона окна игры (рамка, `ThemeableTopBar`, `ButtonClose`, `WindowDrag`); тема — через `ThemeManager.Instance`; модальность — `PauseTrigger`. Подтверждения — `ModUI.Confirm`, подсказки — `ModUI.Tooltip` (собственных мостов к `TooltipHandler` и собственных диалогов подтверждения нет). Деньги форматируются `StringFormatter.FloatToMoney`.

### Меню спасения (`RecoveryWindow`)

- **Вкладки** (`ButtonUI` как переключатели): «Сводка», «Активы», «Кредиты», «Журнал».
- **Сводка:** строки `InfoText` — баланс, порог дефолта, дефицит, «До банкротства: N дн.» / «Не в дефолте», долг по кредитам мода, ближайший платёж (дата и сумма). Кнопки быстрых действий: «Взять из линии», «Бридж-кредит», «Продать линии».
- **Активы:**
  - «Линии»: `DropdownUI` (пластина и число линий), `SliderUI` (N), `InfoText` (выручка, мощность «X → Y», предупреждение о контрактах), `ButtonUI` «Продать».
  - «Склад и CPU»: строки CPU (имя, склад, выручка склада, штраф при снятии) с кнопками «Распродать склад» и «Снять с производства».
  - «Производство»: `InfoText` со стоимостью подразделения, `ButtonUI` «Открыть управление линиями».
- **Кредиты:** таблица кредитов (вид, остаток, ставка, платёж, месяцев осталось, «Погасить досрочно»); блок линии (лимит / выбрано / свободно / закрытие, `SliderUI` суммы, «Взять», «Погасить», `ToggleUI` «Автозаём»); блок бридж-кредита (`SliderUI` 25–100 %, условия, «Взять»).
- **Журнал:** последние 50 записей.
- **Подтверждение** (`ModUI.Confirm`, двухшаговое): операция, сумма, баланс после, побочные эффекты; «Да» / «Нет». Недоступные кнопки неактивны, причина — в подсказке и под кнопкой.

### Предложение спасения (`BailoutOfferWindow`)

Модальное окно с паузой: сумма `B`, баланс после, два варианта (`ButtonUI` с условиями под каждым), «Отказаться — объявить банкротство». Закрыть окно крестиком = отказ не засчитывается: окно откроется снова на следующем тике удержания (защита от случайного закрытия).

### Прочее

- Кнопка «Меню спасения» в ванильном `BankruptcyWarningWindow` (P4).
- Страница фичи в хабе «Моды» (`ctx.HubPage`, раздел «Финансы») → «Меню спасения»; при флаге отладки — отладочные действия (TSK-019).
- Письма `ModMail` (`Critical`): «Риск дефолта» (FR-1, тег `rec:prewarning`), «Компания в дефолте» (FR-2, тег `rec:default`) — с многоразовым действием «Меню спасения» (`ModMailAction`, обработчик `ModMail.RegisterActionHandler`).
- `ModNotify`: «Кредит погашен», «Взято из кредитной линии: X» (автозаём), «Обязательства переведены в обычный долг: X».
- Подтверждение выключения фичи в хабе: `ModUI.Confirm` с текстом `rec.disable.confirm`.

### Ключи строк (`mod/src/PTMod/L10n/ru/bankruptcy-recovery.json`, `…/en/bankruptcy-recovery.json`; префикс `rec.`)

| Ключ | ru | en |
|---|---|---|
| `feature.name` | Спасение от банкротства | Bankruptcy recovery |
| `feature.description` | Меню спасения, кредитная линия, бридж-кредит и последний шанс | Recovery menu, credit line, bridge loan and last resort |
| `hub.title` | Финансы | Finance |
| `param.preWarning` / `param.startupCredit` / `param.bridgeLoan` / `param.bailout` / `param.earlyExit` | Предупреждение о риске дефолта / Стартовая кредитная линия / Бридж-кредит / Последний шанс / Досрочный выход из дефолта | Default risk warning / Startup credit line / Bridge loan / Last resort / Early exit from default |
| `menu.title` | Меню спасения | Recovery options |
| `menu.open` | Меню спасения | Recovery options |
| `tab.summary` / `tab.assets` / `tab.loans` / `tab.journal` | Сводка / Активы / Кредиты / Журнал | Summary / Assets / Loans / Journal |
| `summary.threshold` | Порог дефолта: {0} | Default threshold: {0} |
| `summary.deficit` | До порога: {0} | To threshold: {0} |
| `summary.daysLeft` | До банкротства: {0} дн. | Bankruptcy in {0} days |
| `summary.notInDefault` | Не в дефолте | Not in default |
| `lines.sell` | Продать {0} лин. ({1}) за {2} | Sell {0} lines ({1}) for {2} |
| `lines.contractsWarning` | Мощность меньше линий под контракты: возможны штрафы | Capacity below contracted lines: fines possible |
| `lines.minOne` | Нужна минимум 1 линия | At least 1 line must remain |
| `cpu.sellStock` | Распродать склад: +{0} | Sell stock: +{0} |
| `cpu.retire` | Снять с производства (штраф {0}) | Retire (fine {0}) |
| `bridge.title` | Бридж-кредит | Bridge loan |
| `bridge.terms` | {0} на {1} мес. под {2}% годовых, платёж {3}/мес. | {0} for {1} months at {2}% APR, {3}/month |
| `bridge.unavailable.active` | Бридж-кредит уже взят | A bridge loan is already active |
| `credit.title` | Стартовая кредитная линия | Startup credit line |
| `credit.state` | Лимит {0}, выбрано {1}, свободно {2}, до {3} | Limit {0}, drawn {1}, available {2}, until {3} |
| `credit.autodraw` | Автозаём у порога дефолта | Auto-draw near default threshold |
| `bailout.title` | Последний шанс | Last resort |
| `bailout.loan` | Кредит спасения: {0} на {1} мес. под {2}% | Rescue loan: {0} for {1} months at {2}% |
| `bailout.investor` | Доля инвестора: {0}% прибыли {1} мес. (не больше {2}) | Investor share: {0}% of profit for {1} months (max {2}) |
| `bailout.decline` | Отказаться — объявить банкротство | Decline — declare bankruptcy |
| `reputation.bailout` | Спасение от банкротства | Bankruptcy bailout |
| `mail.prewarning.subject` | Риск дефолта | Default risk |
| `mail.prewarning.body` | Баланс {0} близок к порогу дефолта {1}. Откройте меню спасения. | Balance {0} is close to the default threshold {1}. Open recovery options. |
| `mail.default.subject` | Компания в дефолте | Company in default |
| `mail.action.openMenu` | Меню спасения | Recovery options |
| `confirm.title` | Подтвердите операцию | Confirm operation |
| `confirm.after` | Баланс после: {0} | Balance after: {0} |
| `disable.confirm` | Непогашенные обязательства ({0}) станут обычным долгом банка. Баланс после: {1}.{2} | Outstanding obligations ({0}) will become ordinary bank debt. Balance after: {1}.{2} |
| `disable.defaultRisk` | Баланс окажется ниже порога дефолта. | The balance will fall below the default threshold. |
| `disable.confirmLabel` | Выключить и перевести в долг | Disable and convert to debt |
| `notify.converted` | Обязательства переведены в обычный долг: {0} | Obligations converted to ordinary debt: {0} |

### Sequence: предупреждение → дефолт → меню

```mermaid
sequenceDiagram
    participant DC as DateController
    participant MT as ModTicks
    participant MON as RecoveryMonitor
    participant BH as BankruptcyHandler
    participant P3 as P3 postfix TriggerDefault
    participant P4 as P4 postfix Warning.Open
    participant MM as ModMail
    participant RW as RecoveryWindow
    DC->>MT: LateTick
    MT->>MON: OnDay()
    MON->>MON: money < −0,75·D и письмо не отправлено
    MON->>MM: письмо «Риск дефолта» [действие Меню спасения]
    DC->>BH: OnDayPassed → TickBankruptcies
    BH->>BH: money < порог → TriggerDefault(игрок)
    BH->>P3: postfix
    P3->>MON: OnPlayerDefault()
    MON->>MM: письмо «Дефолт» [действие Меню спасения]
    BH->>P4: BankruptcyWarningWindow.Open → postfix
    P4->>P4: добавить кнопку «Меню спасения»
    Note over RW: игрок жмёт кнопку
    P4->>RW: Open(Summary) + PauseTrigger
```

### Sequence: автозаём до проверки банкротства

```mermaid
sequenceDiagram
    participant DC as DateController
    participant SCL as StartupCreditLineService
    participant CL as CapitalLedger
    participant BH as BankruptcyHandler
    DC->>SCL: LateTick (ctx.OnTick)
    SCL->>SCL: L = clamp(a_d·D_L + 0,5·R_3, 0, 3·D_L)
    SCL->>SCL: money < −0,9·D → Δ = min(avail, −0,6·D − money)
    SCL->>CL: CapitalIn(Δ) → Company.AddMoney(Δ)
    DC->>BH: OnDayPassed → TickBankruptcies
    BH->>BH: money ≥ порог → дефолт не начинается
    Note over SCL,CL: проценты по линии списываются раз в месяц на LateMonthPassed
```

### Sequence: последний шанс

```mermaid
sequenceDiagram
    participant BH as BankruptcyHandler
    participant P1 as P1 prefix TickBankruptcies
    participant BS as BailoutService
    participant BOW as BailoutOfferWindow
    participant CL as CapitalLedger
    participant PF as PlayerFinanceAdapter
    BH->>P1: TickBankruptcies()
    P1->>P1: MustHoldForBailout(игрок)?
    P1->>PF: DaysInDefault = T − 2
    P1->>BS: OnHoldRequested()
    BS->>BOW: Open(B, варианты) + PauseTrigger (при IsBatchRun — после BatchEnded)
    P1-->>BH: true (оригинал идёт)
    BH->>BH: DaysInDefault → T − 1 < T, банкротства нет
    alt Принять
        BOW->>BS: Accept(kind)
        BS->>CL: CapitalIn(B)
        BS->>PF: CancelDefault() (рефлексия)
        BS->>BS: кредит / доля инвестора, bailoutsUsed++, ModReputation.Add(−10)
    else Отказаться
        BOW->>BS: Decline()
        BS->>BS: episode.bailoutDeclined = true
        Note over BH: следующий тик: ванильный Bankrupt(игрок)
    end
```

### Sequence: месячные платежи

```mermaid
sequenceDiagram
    participant DC as DateController
    participant LS as LoanService
    participant LM as LoanMath
    participant CL as CapitalLedger
    DC->>LS: LateMonthPassed (ctx.OnTick)
    loop по каждому кредиту
        LS->>LM: MonthStep(outstanding, payment, rate·M, monthsLeft)
        LM-->>LS: interest, principal
        LS->>CL: Expense(interest, Interest)
        LS->>CL: CapitalOut(principal)
        LS->>LS: monthsLeft--, при 0 → LoanClosed, ModNotify
    end
```

### Sequence: выключение фичи

```mermaid
sequenceDiagram
    participant U as Игрок
    participant HUB as Хаб Моды
    participant F as BankruptcyRecoveryFeature
    participant DSS as DisableSettlementService
    participant CL as CapitalLedger
    U->>HUB: выключить фичу
    HUB->>F: ConfirmBeforeDisable provider
    F->>DSS: BuildConfirmation()
    DSS-->>HUB: сумма, баланс после, риск дефолта (или null)
    HUB->>U: ModUI.Confirm
    alt Да
        HUB->>F: OnDisabled()
        F->>DSS: Settle()
        DSS->>CL: CapitalOut(Total)
        DSS->>DSS: закрыть кредиты, линию, договор; журнал; ModNotify
    else Нет
        HUB->>U: фича остаётся включённой
    end
```

### State: эпизод дефолта игрока

```mermaid
stateDiagram-v2
    [*] --> Normal
    Normal --> Warned: money < −0,75·D (FR-1, письмо)
    Warned --> Normal: money ≥ −0,5·D (сброс эпизода)
    Warned --> InDefault: money < порог на OnDayPassed (TriggerDefault)
    Normal --> InDefault: money < порог
    InDefault --> Normal: 7 дней подряд money ≥ порог (FR-9, CancelDefault)
    InDefault --> Normal: день 120, money ≥ порог (ванильный CancelDefault)
    InDefault --> BailoutOffer: день 120, money < порог, лимит спасений не исчерпан (P1)
    BailoutOffer --> Normal: принято (CapitalIn(B), CancelDefault)
    BailoutOffer --> Declined: отказ
    Declined --> Bankrupt: следующий тик, ванильный Bankrupt
    InDefault --> Bankrupt: день 120, money < порог, спасений нет
    Bankrupt --> [*]
```

## Error Handling

| Ситуация | Поведение | Видно игроку |
|---|---|---|
| Исключение в патче P1–P4 или обработчике тика | `ModHooks.Guard`: перехват, `ModLog` со стеком, счётчик ядра; после `hooks.faultThreshold` фича `Faulted` до перезапуска; P1 при сбое не удерживает счётчик (ванильный путь) | Уведомление ядра «Фича отключена из-за ошибки» |
| Самопроверка не нашла цель патча или член рефлексии | Фича `Unavailable`; при загрузке сейва с обязательствами они переводятся в обычный долг (FR-12 п. 5) | Строка в хабе «Моды»: «недоступна (несовместимая версия игры)» |
| Окно предложения не построилось | `BailoutService` снимает удержание; банкротство ванильное | Ванильное `BankruptcyWindow` |
| Старый сейв без секции | Значения по умолчанию; линия — по FR-7 п. 8 | — |
| Секция с `_schema` новее мода | Ядро сохраняет секцию как есть и отдаёт фиче значения по умолчанию | Строка в хабе |
| Битая запись кредита в `ModSave` | Запись отброшена, `ModLog` | — |
| Битый `balance.json` | Значения по умолчанию по полям, `ModLog` | — |
| Фича выключена посреди игры | Подтверждение в хабе; `OnDisabled` переводит обязательства в обычный долг; патчи пропускают работу; удержание P1 снято | Уведомление о переводе; кредитов на вкладке больше нет |
| Подфича выключена | Новые операции подфичи недоступны, существующие кредиты обслуживаются | Кнопки подфичи неактивны с причиной |
| Мод удалён без выключения фичи | Ванильный сейв грузится; кредиты мода исчезают | README (требование: выключить фичу перед удалением) |
| `Player.Instance`, `CentralBank.Instance` или `InflationHandler.Instance` = null (смена сцены) | Обработчик выходит без действий | — |

**Logging Strategy**: ошибки — `[PTMod] [bankruptcy-recovery] ERROR <операция>: <исключение>` (формат `ModLog`); аудит — каждая денежная операция в журнал фичи (ModSave), при флаге отладки дублируется в `ModLog`; производительность — при флаге отладки среднее время `OnDay` за 365 тиков раз в игровой год.

## Security Considerations

- **Валидация контента и сейва:** диапазоны из таблицы контента и правил `RecoveryState`; некорректное значение не применяется.
- **Нет сети, нет внешних процессов.** Файлы — только боковой файл сейва и файлы настроек/профиля (через Core).
- **Рефлексия ограничена** двумя членами `BankruptcyHandler`, проверенными самопроверкой.
- **Защита от лазеек:** выключение фичи и повторное включение не дают денег (D9); число спасений и кулдаун бридж-кредита сохраняются; кнопки отключаются, пока операция недоступна, чтобы не создавать двойных займов двойным кликом (операция выполняется один раз на подтверждение).

## Performance Considerations

| Где | Работа | Бюджет |
|---|---|---|
| `LateTick` | 1 чтение `D`, `money`, `R_3` (кэш на месяц), 2–3 сравнения, ≤ 1 операция денег | ≤ 0,05 мс, 0 аллокаций |
| `TickBankruptcies` P1/P2 | Только ветка игрока, O(1) | ≤ 0,01 мс |
| `LateMonthPassed` | ≤ 5 кредитов + линия + доля инвестора; пересчёт кэша `R_3` | ≤ 0,5 мс |
| Открытие меню | Список CPU игрока (обычно < 50), пластины (≤ 7) | ≤ 50 мс |

`R_3` кэшируется на месяц (пересчёт в `LateMonthPassed`), чтобы не обходить отчёты каждый день.

## Testing Strategy

### Юнит-тесты чистой логики (`mod/tests/PTMod.Tests/BankruptcyRecovery/`; мини-раннер ядра, `python mod/build.py --tests`)

Атрибуты `[Test]`, `[Covers(typeof(X))]`, проверки `Assert.True/False/Equal/NotEqual/Near/InRange/Throws<T>/Fail`; каждый публичный тип `Logic` указан в `[Covers]`.

- `LoanMathTests`: аннуитет `A = 8 400 000, r = 0,30, n = 12` → `P = 818 892 ± 1`; `r = 0` → `A/n`; сумма тел по графику = `A`; последний месяц гасит остаток полностью; месячный процент `2 660 000 × 0,10 / 12 = 22 166,67`.
- `CreditLineMathTests`: лимит для всех 6 сложностей; верхняя граница `3·D_L`; `LimitBase`: `max(D, floorBase × I)` (пример пола: `D = 1 млн`, `I = 2,0` → 4 млн); автозаём из примера (−7,7 млн → +2,66 млн); автозаём не превышает `avail`; `Sweep` при отрицательном балансе = 0; окно 3 года (граница дня).
- `BailoutMathTests`: `B` из примера 1977 = 36 075 000; `Allowed` для сложностей 0…5 = `[2,1,1,1,1,0]`; платёж инвестору при убытке = 0; потолок `cap`.
- `RecoveryRulesTests`: удержание только для игрока (`IsPlayer = false` → `false`); не удерживать после отказа, при исчерпанном лимите (в том числе `Allowed = 0` на сложности 5); предупреждение: отправка/повтор/сброс; серия выхода; доступность бридж-кредита (активный, кулдаун 180 дней).
- `DisableSettlementTests`: `Total` = остатки кредитов + выборка + `cap − paid`; пустое состояние → 0 (подтверждения нет); договор с инвестором на пределе `cap` → 0; `Breakdown` складывается в `Total`.
- `RecoveryBalanceTests`: валидация каждого поля (включая `limits.floorBase`, длину массивов `*ByDifficulty`), значения по умолчанию при ошибках.
- `RecoveryStateSerializationTests`: round-trip DTO ↔ `JsonObject` (`ModJson.From`/`Bind`) без поля версии; отсутствующая секция; состояние линии `none`/`open`/`closed`; битые даты; чтение примера бокового файла из этого документа. Случаи «секция без `_schema`» и «`_schema` новее» проверяет ядро (mod-framework/TSK-016).
- `AiIsolationTests`: правила и сервисы не выполняют действий для `IsPlayer = false`.

### Самопроверка на старте (через `ModHooks`)

- Существуют `BankruptcyHandler.TickBankruptcies`, `BankruptcyHandler.TriggerDefault`, `BankruptcyHandler.CancelDefault(ICompany)`, поле `BankruptcyHandler.defaultTimeLimit`, `BankruptcyWarningWindow.Open`, `ManageLinesWindow.UpdateWafers`, `StorageMath.SellStock`, `StorageMath.Income`, `ContractHelper.FinesFromContracts(Cpu)`, `ProductionMath.SaleCost`, `ProductionMath.ManufacturingDivisionValue`, `Company.AddMoney`, `InflationHandler.Inflation`.
- Prefix и postfix на `BankruptcyHandler.TickBankruptcies` допустимы (резерв ядра — только transpiler).
- Результат — строка в `Player.log` и статус фичи в хабе.

### Ручные сценарии в игре (выполняет владелец)

Подготовка: в `ptmod_settings.json` включить флаг отладки фичи — на странице фичи в хабе появятся отладочные действия (TSK-019): «Баланс = k × D», «DaysInDefault = N». Для сценариев с подтверждением выключения достаточно хаба.

- **M1. Предупреждение и меню.** 1) Новая игра, «нормально», 1975. 2) Отладка: «Баланс = −0,8 × D». 3) Дать пройти 1 дню. **Ожидается:** письмо «Риск дефолта». 4) Нажать действие в письме. **Ожидается:** меню открыто, время на паузе, сводка показывает баланс, порог, «Не в дефолте»; письмо осталось, действие можно нажать снова.
- **M2. Дефолт и кнопка в окне предупреждения.** 1) Отладка: «Баланс = −1,1 × D». 2) Дать пройти 1 дню. **Ожидается:** ванильное окно предупреждения с кнопкой «Меню спасения», письмо «Дефолт». 3) Нажать кнопку. **Ожидается:** меню, «До банкротства: 119 дн.».
- **M3. Продажа активов.** 1) В меню → «Активы» → продать 2 линии. **Ожидается:** баланс вырос на сумму из превью; в ванильном окне управления линиями мощность уменьшилась на 2. 2) Распродать склад CPU. **Ожидается:** склад 0, +29 $ за штуку. 3) Снять CPU с контрактом. **Ожидается:** штраф в отчёте «Штрафы», CPU исчез из окна производства.
- **M4. Бридж-кредит.** 1) В дефолте → «Кредиты» → 100 %. **Ожидается:** баланс +A − 5 %; строка кредита 12 мес. 2) Промотать до 1-го числа. **Ожидается:** в финансах месяца есть «Проценты», баланс уменьшился на платёж из превью. 3) Сохранить, загрузить. **Ожидается:** кредит на месте, график тот же.
- **M5. Стартовая линия и автозаём.** 1) Новая игра. **Ожидается:** «Кредиты» — линия с лимитом `a_d × D_L`, закрытие через 3 года. 2) Отладка: «Баланс = −0,95 × D». 3) Пройти 1 день. **Ожидается:** уведомление «Взято из кредитной линии», баланс ≈ −0,6 × D, дефолта нет. 4) Дойти до 1-го числа. **Ожидается:** в отчёте месяца одна строка «Проценты» по линии.
- **M6. Последний шанс.** 1) Отладка: «Баланс = −1,2 × D», «DaysInDefault = 118». 2) Пройти 1 день. **Ожидается:** окно предложения, время на паузе. 3) «Кредит спасения». **Ожидается:** баланс ≈ +0,25 × D, баннер дефолта сменился на «успех», кредит 36 мес. 4) Сохранить/загрузить — состояние сохранено. 5) В другом прогоне — «Отказаться». **Ожидается:** на следующем дне ванильное `BankruptcyWindow`. 6) Прогон на сложности «невозможно»: **Ожидается:** окна нет, ванильное банкротство. 7) «Следующий месяц» через день 120. **Ожидается:** окно открывается после завершения пакета, банкротства нет.
- **M7. Досрочный выход.** 1) В дефолте отладка «Баланс = +0,1 × D». 2) Пройти 7 дней. **Ожидается:** баннер «успех», дефолт снят до 120-го дня.
- **M8. Изоляция ИИ.** 1) Сыграть 2 года с включённой фичей на «очень сложно». **Ожидается:** в журнале и `Player.log` нет операций мода над ИИ; ИИ-банкротства идут ванильными тостами.
- **M9. Совместимость.** 1) Загрузить сейв, сделанный без мода. **Ожидается:** без ошибок; линия открыта, только если с основания прошло < 3 лет. 2) Удалить мод без выключения фичи (`install.py` restore), загрузить сейв с кредитами. **Ожидается:** сейв грузится, кредитов нет (документировано).
- **M10. Выключение фичи.** 1) Взять бридж-кредит и выбрать из линии (M4, M5). 2) В хабе выключить фичу. **Ожидается:** окно подтверждения с суммой, балансом после и (если баланс окажется ниже порога) предупреждением о дефолте. 3) «Нет». **Ожидается:** фича включена, кредиты на месте. 4) Снова выключить, «Да». **Ожидается:** баланс уменьшился на сумму из окна, кредитов нет, банк начисляет ванильные проценты на минус. 5) Включить фичу. **Ожидается:** бридж-кредит недоступен до конца кулдауна, линия не открывается, счётчик спасений прежний. 6) Выключить фичу правкой `ptmod_settings.json` при кредите в сейве, загрузить. **Ожидается:** обязательства переведены в долг при загрузке, уведомление.

### Совместимость сейвов (автоматически)

- Round-trip секции (`RecoveryStateSerializationTests`).
- Чтение примера бокового файла из этого документа.
- Секция другой фичи в том же файле не теряется (проверка через `ModSave` из `mod-framework`).

## Migration and Compatibility

- **Старые сейвы:** секции нет → состояние по умолчанию (линия `none`); стартовая линия открывается при `GameReady(Loaded)` только если `today − FoundationDate < 3 года` (на остаток срока).
- **Миграции схемы:** версию секции хранит и сверяет ядро (`_schema`, `SchemaVersion = 1`); будущие миграции — внутри `Read(section, schema)` участника.
- **Удаление мода:** ванильный сейв не содержит данных мода; кредиты исчезают (документируется; безопасный путь — выключить фичу в хабе перед удалением, тогда остаток станет обычным долгом).
- **Обновление игры:** приватные цели проверяет самопроверка; при несовпадении фича `Unavailable`, игра работает ванильно, обязательства из сейва переводятся в обычный долг.
- **Совместимость с другими фичами:** `rival-acquisitions` использует `CapitalLedger` и ставит postfix на тот же `BankruptcyHandler.TriggerDefault` (ветка ИИ); `scenarios-achievements` может слушать `RecoveryApi.BailoutAccepted` для статистики (assisted спасение не ставит); `press-reviews` применяет игровой эффект репутации (причина `rec.reputation.bailout`); ядро держит на `TickBankruptcies` transpiler P10 — патчи этой фичи (prefix/postfix) с ним совместимы.

## Новые каналы ModModifiers

Нет. Фича только читает стандартный канал `bank.interest` (для игрока) как множитель ставок кредитов мода; записей и провайдеров не регистрирует. Канал `bank.creditLimit` в лимитах мода не применяется повторно (решение владельца). Вклад в `market.rating.cpu` и `popularity.gain` — 0 (фича рыночных эффектов не поставляет).

## Трассировка FR → компоненты / патчи / задачи

| FR | Компоненты | Патчи / подписки | Задачи |
|---|---|---|---|
| FR-1 | `RecoveryMonitor`, `RecoveryRules.PreWarning` | `TickPhase.LateTick` | TSK-006, TSK-015, TSK-020 |
| FR-2 | `RecoveryWindow`, `RecoveryMonitor`, `RecoveryApi` | P3, P4 | TSK-010, TSK-013, TSK-015 |
| FR-3 | `AssetSaleService` (линии) | — | TSK-012, TSK-013 |
| FR-4 | `AssetSaleService` (склад, снятие) | — | TSK-012, TSK-013 |
| FR-5 | `AssetSaleService.OpenManageLines` | — | TSK-012, TSK-013 |
| FR-6 | `BridgeLoanService`, `LoanService`, `LoanMath`, `CapitalLedger` | `TickPhase.LateMonthPassed` | TSK-004, TSK-005, TSK-006, TSK-007, TSK-008, TSK-014 |
| FR-7 | `StartupCreditLineService`, `CreditLineMath`, `LoanService` | `LateTick`, `LateMonthPassed`, `GameReady` | TSK-005, TSK-007, TSK-009, TSK-014 |
| FR-8 | `BailoutService`, `BailoutMath`, `RecoveryRules`, `BailoutOfferWindow` | P1, `LateMonthPassed`, `BatchEnded` | TSK-003, TSK-006, TSK-010, TSK-011, TSK-015 |
| FR-9 | `RecoveryRules.NextRecoveryStreak`, `PlayerFinanceAdapter.CancelDefault` | P2 | TSK-003, TSK-006, TSK-010 |
| FR-10 | `CapitalLedger`, `RecoveryWindow` (вкладки «Кредиты», «Журнал») | — | TSK-007, TSK-008, TSK-014 |
| FR-11 | Все патчи (ветка `IsPlayer`), `RecoveryRules` | P1–P3 | TSK-006, TSK-010, TSK-018, TSK-020 |
| FR-12 | `RecoveryBalance`, `BankruptcyRecoveryFeature`, `DisableSettlement`, `DisableSettlementService` | `ConfirmBeforeDisable`, `OnDisabled`, `GameReady(Loaded)` | TSK-001, TSK-002, TSK-006, TSK-008, TSK-017, TSK-022 |
| NFR-1…7 | все | все | TSK-016…TSK-021 |
