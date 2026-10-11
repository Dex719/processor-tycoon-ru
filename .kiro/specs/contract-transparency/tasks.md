# Tasks: contract-transparency

## Document Information

- **Feature Name**: contract-transparency
- **Version**: 0.2
- **Date**: 2026-10-10
- **Author**: Dex719
- **Status**: Draft v0.2 — одобрено к реализации владельцем 2026-10-10
- **Related Documents**:
  - Requirements: `.kiro/specs/contract-transparency/requirements.md`
  - Design: `.kiro/specs/contract-transparency/design.md`
  - Framework: `.kiro/specs/mod-framework/` (сервисы `PTMod.Core`; номера задач — `.kiro/specs/mod-framework/tasks.md`)
  - Решения владельца: `.kiro/steering/owner-decisions.md`

## Implementation Overview

Сначала пишется чистая логика с тестами: шансы, требования, обязательства, политика подтверждения, решения автоматики, классификация исхода тендера, журнал. Затем адаптер к игре и патчи. В конце — окна `ModUI`, строки и проверки.

Единственный патч, который меняет поток игры, — prefix на `Contract.AddOffer` (TSK-007). Его делаем после логики риска и журнала. Он обязан падать «в ваниль»: при любой ошибке предложение уходит как без мода.

### Implementation Strategy
- Чистая логика (`mod/src/PTMod/Features/ContractTransparency/Logic/`) не ссылается на `UnityEngine` и типы игры, использует общее подмножество API .NET Framework 4.8 и `mscorlib` Unity (без `init`, `record`, `Index`, `Range`), поэтому тестируется мини-раннером ядра без запуска игры.
- Адаптер `ContractInspector` — единственное место, где читаются приватные члены игры. Он же сверяет результаты с `Contract.FailsAtAnyRequirement` и `Contract.ChanceToSign`.
- Патча на `Contract.GetBestOffers` нет (цель закреплена ядром, P23): метод только вызывается через `AccessTools`.
- Каждая задача заканчивается сборкой (`python mod/build.py --game-dir <папка игры>`) и зелёными тестами (`python mod/build.py --tests`). Запуск игры — только в TSK-022 (владелец).

### Development Approach
- **Testing Strategy**: TDD для `Logic/` (примеры чисел из design.md, раздел «Формулы и баланс»); каждый публичный тип `Logic/` указан в `[Covers]`; самопроверка целей патчей на старте; ручные сценарии M1–M14.
- **Integration Strategy**: патчи регистрируются через `ctx.Patch` (`ModHooks`) и снимаются ядром при выключении фичи; UI подключается последним.
- **Deployment Strategy**: в составе `PTMod.dll` через `install.py --with-mods` (`mod-framework`).

> Номера задач `mod-framework` в поле **Depends** сверены с `.kiro/specs/mod-framework/tasks.md` (v0.2): ModHooks — TSK-023, ModTicks — TSK-024, ModSave — TSK-025, ModCompanies — TSK-026, ModModifiers — TSK-027/059/060, ModConditions — TSK-019/032, ModNotify — TSK-033, ModUI — TSK-034…036/064, ModMail — TSK-037/038/062, ModIds — TSK-066, мини-раннер — TSK-067.

## Фаза 1 — Каркас и чистая логика

- [x] **TSK-001**: Скелет фичи `ContractTransparencyFeature` (авто 2026-10-10)
  - **Requirement**: FR-13, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/ContractTransparencyFeature.cs` (`[ModFeature]`, `IModFeature`: `Id`, `NameKey`, `DescriptionKey`, `DependsOn`, `EnabledByDefault`, `SupportsHotToggle`, `Register(FeatureContext)`, `OnEnabled`/`OnDisabled`), `mod/src/PTMod/Features/ContractTransparency/ContractTransparencySettings.cs` (`ParamSpec`)
  - **Acceptance**:
    - фича с ID `contract-transparency` видна в `ModFeatures.All` и в хабе «Моды» с переключателем «Включено»;
    - `SupportsHotToggle = true`: `OnEnabled`/`OnDisabled` работают без перезапуска;
    - тела патчей обёрнуты в `ModHooks.Guard`; тестовый флаг `ModSettings.Debug("debug.brokenPatch")` доводит фичу до `Faulted`, игра работает, ядро показывает одно уведомление; своих счётчиков ошибок в коде фичи нет;
    - сборка зелёная.
  - **Depends**: mod-framework TSK-022 (`FeatureContext`, `ModFeatures`), TSK-023 (`ModHooks`), TSK-021 (`ModSettings`), TSK-039 (хаб «Моды»), TSK-067 (мини-раннер)

- [x] **TSK-002**: Контент `rules.json`, `ContractRules` и настройки (авто 2026-10-10)
  - **Requirement**: FR-4, FR-13, NFR-6
  - **Deliverables**: `mod/src/PTMod/Content/contract-transparency/rules.json`, `mod/src/PTMod/Features/ContractTransparency/Logic/ContractRules.cs`, `mod/tests/PTMod.Tests/ContractTransparency/ContractRulesTests.cs`
  - **Acceptance**:
    - значения по умолчанию как в design.md (`lowActualChance` 0.10, `capacity` 500, `tenderHistoryYears` 10, `ai.contractMarginThreshold` 0.45);
    - значение вне диапазона заменяется дефолтом и пишется в `ModLog` (тест);
    - оверрайд из `<папка игры>/PTMod/content/contract-transparency/rules.json` перекрывает встроенный (`ModContent.LoadConfig`);
    - `ParamSpec` `confirmMode` (Int 0…2, по умолчанию 1 = «Только рискованные»), `showActualProbability`, `notifyTenderLoss`, `logAutomation` (Bool) объявлены через `ctx.Param` и видны в хабе.
  - **Depends**: TSK-001; mod-framework TSK-021 (`ModContent`), TSK-067

- [x] **TSK-003**: `OddsMath` — шансы и фактическая вероятность (авто 2026-10-10)
  - **Requirement**: FR-2, FR-11
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/Logic/OddsMath.cs`, `mod/tests/PTMod.Tests/ContractTransparency/OddsMathTests.cs`
  - **Acceptance**:
    - тесты проходят: 60/40 → 78.9/21.1 %; 50/30/20 → 69.4/20.8/9.7 %; 1000/1 → шансы 100/1, P 100/0 %; 50/50 → 66.7/33.3 % (раньше предложивший первым);
    - `IpsForChance(0.5, 60) == 60`;
    - `WithCandidate` удаляет все CPU компании кандидата (сравнение по `CompanyKey`);
    - `PessimisticProbability` добавляет участников и пересчитывает;
    - точность P — 1e-9;
    - `OddsMath` указан в `[Covers]`.
  - **Depends**: mod-framework TSK-067 (мини-раннер)

- [x] **TSK-004**: `RequirementCheck`, `CommitmentMath`, `ConfirmPolicy` (авто 2026-10-10)
  - **Requirement**: FR-1, FR-3, FR-4
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/Logic/RequirementCheck.cs`, `.../Logic/CommitmentMath.cs`, `.../Logic/ConfirmPolicy.cs`, `mod/tests/PTMod.Tests/ContractTransparency/RequirementCheckTests.cs`, `.../CommitmentMathTests.cs`, `.../ConfirmPolicyTests.cs`
  - **Acceptance**:
    - границы `Ips == MinIps × 0.75` и `Power == Max` проходят; корпус 0/1/2 × DIP/PLCC/PGA — 9 тестов;
    - штраф за разрыв 50 000 шт. / 730 дней ≈ 888 166 667;
    - каждый из 7 `RiskFlags` срабатывает отдельно;
    - матрица `ConfirmMode × RiskFlags` покрыта.
  - **Depends**: TSK-002

- [x] **TSK-005**: Адаптер `ContractInspector` со сверкой (авто 2026-10-10)
  - **Requirement**: FR-1, FR-2, FR-12
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/ContractInspector.cs`, `.../OddsReport.cs`, `.../RequirementReport.cs`
  - **Acceptance**:
    - `ToleranceMultiplier` читает `Contract.mipsToleranceMultiplier`;
    - `GameChances` вызывает приватный `Contract.GetBestOffers` через AccessTools (не патчит) и читает `Contract.CpuOffer.Cpu` и `Chance`;
    - `BuildOdds` сверяет шанс кандидата с `Contract.ChanceToSign` (допуск 0.5 п. п.), `BuildRequirements` — с `Contract.FailsAtAnyRequirement`; расхождение → флаг и `ModLog`;
    - разбивка множителей — `ModModifiers.Explain("contract.signChance", {Company, CompanyKey, Cpu, Contract})` (`ModSources`, `ModsCapped`); провайдер без причины → строка без текста;
    - ключ компании — `ModCompanies.KeyOf`, при `null` — `sid:<ISaveID.SaveID>`; `SaveID` через интерфейс компании не читается;
    - `GetCpu == null` не роняет код;
    - `IsAvailable == false`, если член не найден.
  - **Depends**: TSK-003, TSK-004; mod-framework TSK-023 (`ModHooks`: AccessTools, самопроверка), TSK-026 (`ModCompanies`), TSK-027, TSK-059, TSK-060 (`ModModifiers.Explain`, `ModifierContext.Contract`)

## Фаза 2 — Журнал и патчи

- [x] **TSK-006**: `ContractLog`, `ContractLogModel`, DTO, `ContractKey` и секция `ModSave` (авто 2026-10-10)
  - **Requirement**: FR-8, NFR-3, NFR-6
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/ContractLog.cs` (`IModSaveParticipant`, тонкий адаптер), `.../Logic/ContractLogModel.cs`, `.../Logic/SaveModels.cs` (DTO без поля версии), `.../Logic/LogRing.cs`, `.../Logic/ContractKeyBuilder.cs`, `mod/tests/PTMod.Tests/ContractTransparency/ContractLogModelTests.cs`, `.../ContractKeyTests.cs`
  - **Acceptance**:
    - `Read`/`Write` через `ModJson.Bind`/`ModJson.From`; `Write()` не возвращает `_schema` (его добавляет ядро), `SchemaVersion = 1`; `ResetToDefaults` даёт пустой журнал;
    - 520 записей → 500 последних;
    - итоги старше 10 лет удаляются при загрузке;
    - сериализация туда-обратно без потерь;
    - нет секции → пустой журнал; `FromSave` устойчив к пустому и частично битому DTO;
    - ключ стабилен, `|` в имени экранируется;
    - журнал из 500 записей ≤ 256 КБ.
  - **Depends**: TSK-002; mod-framework TSK-009, TSK-010 (`ModJson`), TSK-025 (`ModSave`)

- [x] **TSK-007**: `OfferGate` — prefix и postfix `Contract.AddOffer` (авто 2026-10-10)
  - **Requirement**: FR-3, FR-4, FR-14, NFR-2
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/Patches/OfferGate.cs`
  - **Acceptance**:
    - CPU ИИ, автоматика и `BypassScope` проходят без изменений;
    - ручное предложение при `NeedsConfirmation == true` не выполняется, вызывается открытие экрана (заглушка до TSK-013);
    - `ConfirmAndSend` вызывает `AddOffer` под обходом и `ContractWindow.TriggerUIUpdate`;
    - postfix пишет `OfferSent`/`OfferAutoConfirmed` только если `OffersIDs` вырос;
    - исключение в prefix → `true` и `ModLog`;
    - `Priority.First`.
  - **Depends**: TSK-005, TSK-006

- [x] **TSK-008**: `TenderRecorder` — итоги тендеров и автоотмена (авто 2026-10-10)
  - **Requirement**: FR-5, FR-7
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/Patches/TenderRecorder.cs`, `.../Logic/TenderOutcomeClassifier.cs`, `mod/tests/PTMod.Tests/ContractTransparency/TenderOutcomeTests.cs`
  - **Acceptance**:
    - prefix `Contract.ChooseCpu` снимает `OffersIDs` и мощности;
    - postfix `Contract.ChooseCpu` пересобирает шансы розыгрыша повторным вызовом `Contract.GetBestOffers(OffersIDs)`; патча на `GetBestOffers` нет, самопроверка подтверждает, что цель не закреплена за ядром;
    - `TenderOutcomeClassifier.Classify` определяет исход (Won, Lost: outbid / autoReject / shadowed / retired, Void: noOffers / retired) — тесты на 7 случаев;
    - в перемотке (`ModTicks.IsFastForwarding`) и при загрузке (`ModTicks.IsLoading`) ничего не пишется;
    - `AutoRejected` содержит нужно, занято и мощность.
  - **Depends**: TSK-005, TSK-006; mod-framework TSK-024 (`ModTicks`)

- [x] **TSK-009**: `AutomationAdvisor` и `AutomationLogger` (авто 2026-10-10)
  - **Requirement**: FR-6, FR-9
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/Logic/AutomationAdvisor.cs`, `.../Patches/AutomationLogger.cs`, `mod/tests/PTMod.Tests/ContractTransparency/AutomationAdvisorTests.cs`
  - **Acceptance**:
    - порядок проверок совпадает с `ContractAutomation.SendOffers` (снят → `SellOnContracts` → маржа → требования `AddOffer`);
    - маржа 0.35 при пороге 40 → `SkippedMargin` с числами;
    - финализатор сбрасывает `AutomationScope` даже при исключении;
    - сверка до и после → `Mismatch` в `ModLog`;
    - при выключенной автоматике — одна запись `AutomationOff`;
    - `Preview()` возвращает те же решения без вызова `AddOffer`.
  - **Depends**: TSK-005, TSK-006, TSK-007

- [x] **TSK-010**: `TenderMailer` — сводка и уведомление (авто 2026-10-10)
  - **Requirement**: FR-5
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/TenderMailer.cs`
  - **Acceptance**:
    - подписка `ctx.OnTick(TickPhase.DayPassed, …)` без `RunDuringFastForward`; в перемотке и при загрузке не вызывается;
    - при непустом буфере — одно письмо `ModMail.Send` «Итоги тендеров {год}»: `Tag = "ct:tenders:<yyyy-MM-dd>"`, `Priority = MailPriority.Normal`, строка на контракт, действие `open-log` «Открыть журнал» (`ModMailAction`, `Repeatable = true`); блоков ID писем нет;
    - `ModMail.RegisterActionHandler` открывает окно журнала с фильтром «Итоги тендеров» (после TSK-014), письмо не закрывается;
    - при проигрыше и `notifyTenderLoss` — одно `ModNotify`;
    - в пакетном прогоне (`ModTicks.IsBatchRun`) письмо и уведомление показываются ядром после `BatchEnded`, по одному на день;
    - пустой буфер не делает ничего.
  - **Depends**: TSK-008; mod-framework TSK-024 (`ModTicks`), TSK-033 (`ModNotify`), TSK-037, TSK-038 (`ModMail`: теги, действия), TSK-057 (`IsBatchRun`), TSK-062 (приоритеты, квоты)
  - **Частично в куске 2, закрыто в куске 3** (авто 2026-10-10): в куске 2 были готовы письмо, уведомление, подписка на день, тег, приоритет, действие `open-log` и обработчик действия; не хватало окна журнала. В куске 3 `ContractLogWindow` (TSK-014) зарегистрирован через `LogWindowLauncher.SetOpener(ContractLogWindow.Open)` в `Register`: `TenderMailer.OnMailAction` по действию «Открыть журнал» открывает журнал на фильтре «Итоги тендеров», письмо остаётся открытым (в игре не проверялось, M6 — TSK-022).

## Фаза 3 — Интерфейс

- [x] **TSK-011**: `ContractWindowAugmenter` — кнопки и «(факт. N %)» (авто 2026-10-10)
  - **Requirement**: FR-2, NFR-5
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/UI/ContractWindowAugmenter.cs`
  - **Acceptance**:
    - postfix `ContractWindow.Start` добавляет «Почему?» рядом с вероятностью и «Журнал» (клоны `ButtonUI`);
    - postfix `ContractWindow.UpdateProbabilityText` дописывает `ct.odds.actual`, только если CPU прошёл требования и включён `showActualProbability`;
    - при выключении фичи кнопки скрыты;
    - смена темы перекрашивает.
  - **Depends**: TSK-005; mod-framework TSK-034, TSK-035, TSK-036 (`ModUI`)

- [x] **TSK-012**: Окно `OddsPanel` — разбор шанса (авто 2026-10-10)
  - **Requirement**: FR-1, FR-2
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/UI/OddsPanel.cs`
  - **Acceptance**:
    - три строки требований с цветом и числами;
    - таблица предложений в порядке розыгрыша (компания, CPU, IPS, ванильный и игровой шанс, факт.);
    - строки «Модификаторы модов» с источниками из `ModModifiers.Explain` (фича, множитель, текст причины; «(ограничено)» при `IsBalanceCapped`/`IsFuseCapped`), «затенено», «что изменить», «предложений нет»;
    - пометка «расхождение с игрой» при флаге;
    - открывается и закрывается без ошибок для контракта без предложений.
  - **Depends**: TSK-011

- [x] **TSK-013**: Модальный `OfferReviewDialog` (авто 2026-10-10)
  - **Requirement**: FR-3, FR-4
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/UI/OfferReviewDialog.cs`
  - **Acceptance**:
    - все поля FR-3.2 и предупреждения FR-3.3;
    - окно на `WindowTemplate.Modal`: время на паузе, пока окно открыто;
    - «Предложить» → `OfferGate.ConfirmAndSend`; «Отмена» и закрытие → запись `OfferCancelledOnReview`;
    - переключатель «Больше не спрашивать для безопасных» меняет `confirmMode` на `Risky`;
    - перед отправкой проверка `ContractManager.ContractExists`;
    - окно помещается в 1280×720.
  - **Depends**: TSK-007, TSK-012

- [x] **TSK-014**: Окно `ContractLogWindow` (авто 2026-10-10)
  - **Requirement**: FR-8
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/UI/ContractLogWindow.cs`
  - **Acceptance**:
    - открывается из окна контрактов, хаба «Моды» и действия письма;
    - 6 фильтров `DropdownUI`;
    - записи от новых к старым;
    - панель подробностей показывает `data` и итог тендера целиком.
  - **Depends**: TSK-006, TSK-011

- [x] **TSK-015**: Вкладка `AutomationPreviewPanel` (авто 2026-10-10)
  - **Requirement**: FR-9
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/UI/AutomationPreviewPanel.cs`
  - **Acceptance**:
    - показывает `AutomationLogger.Preview()` с датой следующего прогона и днями до него;
    - «Обновить» пересчитывает по текущему `Player.ContractProfitMargin`;
    - при выключенной автоматике — пометка.
  - **Depends**: TSK-009, TSK-014

- [x] **TSK-016**: `BusinessSignGate` — подтверждение подписи сделки через `ModUI.Confirm` (авто 2026-10-10)
  - **Requirement**: FR-10
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/Patches/BusinessSignGate.cs`
  - **Acceptance**:
    - prefix `ContractNegotiationWindow.SignContract` откладывает подпись (кроме режима «Никогда») и вызывает `ModUI.Confirm` с текстом: срок, линии, наценка, эксклюзивность, штрафы, оценка согласия;
    - «Подписать» вызывает оригинал под обходом `BypassScope`;
    - postfix пишет `BusinessSigned`;
    - «Нет» оставляет окно переговоров без изменений;
    - своё окно для сделок не создаётся.
  - **Depends**: TSK-004, TSK-006; mod-framework TSK-064 (`ModUI.Confirm`)

- [x] **TSK-017**: Оценка возможных конкурентов (авто 2026-10-10)
  - **Requirement**: FR-11
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/ContractInspector.cs` (`PotentialAiBidders`), блок в `UI/OddsPanel.cs`
  - **Acceptance**:
    - список CPU ИИ, прошедших `FailsAtAnyRequirement`, не снятых, `SellOnContracts`, с маржой ≥ `ai.contractMarginThreshold`;
    - пессимистичная вероятность;
    - пометки «оценка» и «на сложности 5 ИИ активнее» (при `Player.DifficultyLevel == 5`).
  - **Depends**: TSK-012

- [x] **TSK-018**: События и предикаты `ct.*` для других фич (авто 2026-10-10)
  - **Requirement**: FR-14
  - **Deliverables**: `mod/src/PTMod/Features/ContractTransparency/ContractTransparencyEvents.cs`, регистрация предикатов в `ContractTransparencyFeature.cs`
  - **Acceptance**:
    - `OfferSent` поднимается один раз на отправку (вручную, автоматика, API) через `ModHooks.Raise`;
    - `TenderResolved` — на итог с игроком, не в перемотке;
    - типы `ct.offersSent` и `ct.tendersWon` (параметр `atLeast`) зарегистрированы через `ModConditions.Register`, не отклонены проверкой префикса (`ModIds.IsPredicateNameAllowed`), разбираются `ModConditions.Parse`, счётчики переживают сохранение.
  - **Depends**: TSK-007, TSK-008; mod-framework TSK-019, TSK-032 (`ModConditions`), TSK-066 (`ModIds`)

## Фаза 4 — Строки, проверки, документация

- [x] **TSK-019**: Строки `ct.*` (ru, en) (авто 2026-10-10)
  - **Requirement**: NFR-4
  - **Deliverables**: `mod/src/PTMod/L10n/ru.json`, `mod/src/PTMod/L10n/en.json` (раздел `ct.*`)
  - **Acceptance**:
    - каждый ключ из кода есть в обоих файлах; `python install.py --check` не находит расхождений ru/en и чужих префиксов;
    - термины совпадают с `ptru` («Предложить», «Отозвать», «Вероятность:», «Автоотмена предложений», «Автоподписание контрактов»);
    - деньги и IPS форматируются через `StringFormatter.FloatToMoney` и `StringFormatter.FloatToIps`.
  - **Depends**: TSK-011…TSK-016; mod-framework TSK-013 (`ModL10n`), TSK-043 (строки ядра, `prefixes`), TSK-045 (`install.py --check`)

- [x] **TSK-020**: Самопроверка целей и замеры (авто 2026-10-10)
  - **Requirement**: FR-12, NFR-1, NFR-2
  - **Deliverables**: регистрация целей и членов в `ContractTransparencyFeature.cs`; таймеры `ModLog` в `OfferGate`, `TenderRecorder`, `AutomationLogger`, `TenderMailer`
  - **Acceptance**:
    - самопроверка находит все 6 целей патчей и приватные члены из design.md («Самопроверка на старте»), включая метод `Contract.GetBestOffers`; `ModHooks.IsReserved` для целей фичи — `false`;
    - эмуляция отсутствия члена (тестовый флаг) отключает только зависящую часть;
    - таймеры пишут время в `ModLog` при `ModSettings.VerboseLog`.
  - **Depends**: TSK-007…TSK-016; mod-framework TSK-042 (`ModSelfCheck`)

- [x] **TSK-021**: Тесты совместимости сейвов (авто 2026-10-10)
  - **Requirement**: NFR-3
  - **Deliverables**: `mod/tests/PTMod.Tests/ContractTransparency/SaveCompatibilityTests.cs`, фикстуры `mod/tests/PTMod.Tests/ContractTransparency/Fixtures/*.ptmod.json`
  - **Acceptance**:
    - фикстуры без секции, с `_schema` 1 и с `_schema` 99 грузятся (для `_schema` 99 секция сохраняется ядром нетронутой, журнал пуст);
    - неизвестные поля игнорируются;
    - запись не трогает чужие секции.
  - **Depends**: TSK-006; mod-framework TSK-016 (`SideFileModel`)

- [ ] **TSK-022**: Ручная проверка в игре (владелец)
  - **Requirement**: FR-1…FR-14, NFR-1, NFR-5
  - **Deliverables**: отметки о прохождении сценариев M1–M14 из design.md в разделе «Progress» (в Evidence: дата, версия мода, замечания)
  - **Acceptance**:
    - все сценарии M1–M13 пройдены (M14 — если включена `platform-generations`);
    - в `Player.log` нет исключений с префиксом `[PTMod]` от фичи;
    - метрика «≤ 0.05 мс в день» подтверждена замером.
  - **Depends**: TSK-001…TSK-021; mod-framework TSK-044, TSK-068 (установка `install.py --with-mods`)

- [ ] **TSK-023**: Описание фичи для игроков — Частично (авто 2026-10-10): ключи `ct.hub.*` и страница в хабе готовы и под тестами; раздел README написан в `mod/requests/contract-transparency.md`, п. 15, и ждёт вставки итоговым интегратором (`mod/README.md` нет, корневой README — не файл этой фичи)
  - **Requirement**: FR-13
  - **Deliverables**: текст описания в хабе «Моды» (ключи `ct.hub.*` в `mod/src/PTMod/L10n/*.json`); раздел «Прозрачность контрактов» в README модов (`mod/README.md`)
  - **Acceptance**:
    - описание объясняет режимы подтверждения, «фактическую вероятность» и журнал;
    - есть пример с числами 60/40 → 21 %.
  - **Depends**: TSK-019

## Dependency graph

```
mod-framework (TSK-022 FeatureContext, TSK-023 ModHooks, TSK-021 ModSettings/ModContent, TSK-025 ModSave, TSK-026 ModCompanies,
               TSK-027/059/060 ModModifiers, TSK-024 ModTicks, TSK-033 ModNotify, TSK-037/038/062 ModMail, TSK-034…036/064 ModUI,
               TSK-019/032 ModConditions, TSK-066 ModIds, TSK-067 мини-раннер)
        │
TSK-001 ──> TSK-002 ──> TSK-004 ──┐
                 │                 ├──> TSK-005 ──┬──> TSK-007 ──┬──> TSK-009 ──> TSK-015
TSK-003 ─────────┼─────────────────┘              │              │
                 └──> TSK-006 ────────────────────┼──> TSK-008 ──┼──> TSK-010
                          │                       │              └──> TSK-018
                          │                       └──> TSK-011 ──> TSK-012 ──┬──> TSK-013
                          │                                                  └──> TSK-017
                          ├──> TSK-014 (после TSK-011) ──> TSK-015
                          ├──> TSK-016 (+TSK-004, mod-framework TSK-064)
                          └──> TSK-021
TSK-011…TSK-016 ──> TSK-019 ──> TSK-023
TSK-007…TSK-016 ──> TSK-020
TSK-001…TSK-021 ──> TSK-022 (владелец)
```

## Progress

| Задача | Статус |
|---|---|
| TSK-001 Скелет фичи | Complete (авто 2026-10-10, кусок 1: `ContractTransparencyFeature` (`[ModFeature]`, `SupportsHotToggle = true`, `Register` читает `rules.json` и объявляет параметры) + `ContractTransparencySettings` + `Logic/ContractParamSpecs` (таблица `ParamSpec`, единственная запись id фичи); `Guarded(...)` — обёртка `ModHooks.Guard`, флаг `debug.brokenPatch` заставляет такие тела бросать (пять сбоев → `Faulted`), собственных счётчиков ошибок нет; сборка фичи и тесты зелёные; патчей в куске ещё нет, поэтому «Faulted по флагу», видимость в хабе и горячее переключение проверяются в игре владельцем в TSK-022; см. `mod/requests/contract-transparency.md`, п. 2) |
| TSK-002 rules.json, ContractRules, настройки | Complete (авто 2026-10-10, кусок 1: `Content/contract-transparency/rules.json`, `Logic/ContractRules` (`FromJson` не бросает, значение вне диапазона или не того типа → дефолт + строка в `problems`, которую фича пишет в `ModLog.Warn`; неизвестное поле сообщается и игнорируется), загрузка `ModContent.LoadConfig` в `Register`; 4 `ParamSpec` (`confirmMode` Int 0…2 = 1, три Bool = вкл) через `ctx.Param`; тесты `ContractRulesTests` (в т.ч. наложение оверрайда через `ContentMerger.MergeConfig`) и `ContractTransparencySkeletonTests`; тесты лежат в `mod/tests/PTMod.Tests/Features/ContractTransparency/`, не в `.../ContractTransparency/`, см. `mod/requests/contract-transparency.md`, п. 3; чтение оверрайда с диска и видимость параметров в хабе проверяются в игре) |
| TSK-003 OddsMath | Complete (авто 2026-10-10, кусок 1: `Logic/OddsMath` (+ `OfferInput`, `OfferOdds`); 60/40 → 78,9/21,1 %, 50/30/20 → 69,4/20,8/9,7 %, 1000/1 → шансы 100/1 и P 100/0 %, 50/50 → 66,7/33,3 %; `IpsForChance(0.5, 60) == 60`; `WithCandidate` по `CompanyKey`; `PessimisticProbability`; шанс считается в `float`, как `DefineChance`, половина округляется к чётному; точность 1e-9; `OddsMathTests`, 37 тестов; сверх design.md добавлены `SumIps`, `MaxIps`, `WithoutCompany`, `ShadowedOwn`, `StrongestOwnCpuId`) |
| TSK-004 RequirementCheck, CommitmentMath, ConfirmPolicy | Complete (авто 2026-10-10, кусок 1: `Logic/{RequirementCheck,CommitmentMath,ConfirmPolicy}`; границы `Ips == MinIps × 0.75` и `Power == Max` проходят; корпус 0/1/2 × DIP/PLCC/PGA — 9 отдельных тестов; штраф за разрыв 50 000 шт. / 730 дней = 888 166 666,67; каждый из 7 `RiskFlags` срабатывает отдельно и все семь вместе; матрица режим × риски; денежные поля `CommitmentReport` — `double`, см. `mod/requests/contract-transparency.md`, п. 4) |
| TSK-005 ContractInspector со сверкой | Complete (авто 2026-10-10, кусок 1: `ContractInspector` (+ `OddsReport`, `RequirementReport`, `ModifierSourceRow`): допуск IPS из поля контракта, `GameChances` через приватный `GetBestOffers` (`AccessTools`, без патча), `BuildOdds`/`BuildRequirements` со сверкой с `ChanceToSign` (0,5 п. п.) и `FailsAtAnyRequirement`, расхождение → флаг и `ModLog`, разбивка `ModModifiers.Explain`, ключ компании `KeyOf` или `sid:` через `ISaveID`, `GetCpu == null` пропускается с предупреждением, `IsAvailable`/`UnavailableReason`; добавлен `BuildCommitments` для TSK-007; собирается против игры, члены `Contract` сверены с настоящим `Assembly-CSharp.dll` (ReflectionOnly: поле float, `GetBestOffers(List<int>)`, `CpuOffer.Cpu/Chance`); адаптер не запускался (нужна игра), офлайн-тестов у него нет — проверка в TSK-022) |
| TSK-006 ContractLog, ContractLogModel, DTO, ContractKey, ModSave | Complete (авто 2026-10-10, кусок 2: `ContractLog : IModSaveParticipant` (тонкий адаптер: `SectionId` = `contract-transparency`, `SchemaVersion` = 1, `ResetToDefaults` / `Read` / `Write` без `_schema`, строки проблем идут в `ModLog`; регистрация `ctx.SaveParticipant` в `Register`), `Logic/ContractLogCodec` (`ModJson.Bind` / `ModJson.From`; секция держится в 256 КБ: если журнал больше, старые записи отрезаются только в файле, модель не меняется), `Logic/ContractLogModel` (кольцо `log.capacity`: 520 записей → 500 новейших; итоги старше `tenderHistoryYears` лет и их строки журнала удаляются при загрузке; фильтры вкладок; счётчики FR-14; буфер «итоги сегодня»; `FromSave` не бросает на пустом и частично битом DTO), `Logic/SaveModels` (DTO без поля версии), `Logic/LogRing<T>`, `Logic/ContractKeyBuilder` (`Apple|1986-1|52000|299|1`, `|` и `\` экранируются, `TryParse`), `Logic/LogVocabulary`, `Logic/TextSafe`, `Logic/LogDates`; `ContractInspector.ContractKey` и `ContractLabel`; журнал из 500 записей автоматики занимает в файле 245 КБ (измерено на тексте, который пишет ядро: с отступами, на два уровня глубже); тесты `ContractLogModelTests` (28), `ContractKeyTests` (9), `TextAndFormatTests` (7); отличия от наброска design.md (`data` — словарь, а не список `KV`, иначе 500 записей не помещаются в 256 КБ; `double` вместо `float`; поля `player` и `capacityNotChecked`): `mod/requests/contract-transparency.md`, п. 5; чтение и запись через настоящий `ModSave` и перезагрузку сейва проверяются в игре, TSK-021 и TSK-022) |
| TSK-007 OfferGate (Contract.AddOffer) | Complete (авто 2026-10-10, кусок 2: `Patches/OfferGate` — prefix (`Priority.First` = 800) и postfix `Contract.AddOffer(int)`; правило — чистая `Logic/OfferGatePolicy` (ИИ, автоматика и `BypassScope` проходят без изменений; вызов, который игра и так отклонит, проходит без экрана; для ручного предложения решает `ConfirmPolicy`, риски считаются только в режиме «только рискованные»); `NeedsConfirmation` → оригинал не выполняется и открывается экран (`OfferReviewScreen`: до TSK-013 это `ModUI.Confirm` со списком рисков и кнопками «Предложить» / «Отмена»; если вопрос показать нельзя, предложение уходит по-ванильному); `ConfirmAndSend` вызывает `AddOffer` под `BypassScope` (сбрасывается в `finally`) и `ContractWindow.TriggerUIUpdate`; `RecordCancelled` пишет «отменено на экране проверки»; postfix пишет `OfferSent` / `OfferAutoConfirmed` только если `OffersIDs` вырос и считает `offersSent`; исключение в prefix → `true`; чужие CPU ищутся в коротком списке `Player.GetCpus()`, а не через `CpuDataProvider.GetCpu`, который обходит все CPU мира; тесты `OfferGatePolicyTests` (13) и `PatchSourceTests` (11: таблица патчей, 800, возврат `true`, `finally`, рост списка); цели `Contract.AddOffer`, `HasReceivedOffer`, `FailsAtAnyRequirement`, `ContractWindow.TriggerUIUpdate` сверены с настоящим `Assembly-CSharp.dll` (ReflectionOnly); патч в игре не запускался: ручные сценарии M2, M3, M10 — TSK-022) |
| TSK-008 TenderRecorder (ChooseCpu), TenderOutcomeClassifier | Complete (авто 2026-10-10, кусок 2: `Patches/TenderRecorder` — prefix снимает `OffersIDs` и мощности (нужно, занято, мощность, литейщик) только для контракта с предложением игрока, postfix читает `ChosenCpuID` и повторным вызовом приватного `GetBestOffers(OffersIDs)` через `ContractInspector.GameChances` берёт шансы розыгрыша; патча на `GetBestOffers` нет, `PatchTargetsCheck` на старте проверяет, что три патчимые цели свободны, а `GetBestOffers` только вызывается; в перемотке и при загрузке ничего не пишется; `Logic/TenderOutcomeClassifier` (+ `TenderFacts`, `TenderOutcome`, `TenderKind`, `TenderReason`) — 7 случаев: выигран, проигран (конкурент, автоотмена, затенено, снят с производства), не состоялся (нет предложений, снят с производства); `Logic/TenderAssembler` собирает из снимка итог (участники, шансы игры, вероятности `OddsMath`, победитель, снятые предложения, `AutoRejected` — нужно / занято / мощность / «своя фабрика или литейщик», признак «игра мощности не проверяла» при выключенной автоотмене) — всё это под тестами без игры; `TenderOutcomeTests` (29); патч в игре не запускался: M6, M7, M13 — TSK-022) |
| TSK-009 AutomationAdvisor, AutomationLogger | Complete (авто 2026-10-10, кусок 2: `Logic/AutomationAdvisor.Decide` — порядок проверок `ContractAutomation.SendOffers` (выключено → снят → `SellOnContracts` → маржа в `float`, равенство проходит → уже предложено → требования); маржа 0,35 при пороге 40 → `SkippedMargin` с числами; `Logic/AutomationReportBuilder` (строка на решение с числами формулы, `Mismatch` + строка `ModLog` там, где игра сделала другое, итоговая строка; выключенная автоматика — одна запись и без итога); `Patches/AutomationLogger` — prefix поднимает `AutomationScope` и строит план (`AutomationPlanner`), финализатор первым делом опускает флаг (даже при исключении), сверяет план с `OffersIDs` и пишет журнал, исходное исключение отдаёт дальше; `Preview()` возвращает те же решения без `AddOffer`, дату следующего прогона и число дней; сверх спецификации: CPU, снятые с производства или не для контрактов, получают одну строку на CPU, а не по строке на контракт; `AutomationAdvisorTests` (17); патч в игре не запускался: M5 — TSK-022) |
| TSK-010 TenderMailer | Complete (авто 2026-10-10, кусок 2 + кусок 3: письмо «Итоги тендеров {год}» (тег `ct:tenders:<yyyy-MM-dd>`, `Normal`, строка на контракт), действие `open-log`, одно `ModNotify` при проигрыше, подписка `DayPassed` без опций, пустой буфер — ничего; в куске 3 окно журнала подключено через `LogWindowLauncher.SetOpener(ContractLogWindow.Open)`, действие письма открывает журнал на «Итогах тендеров»; в игре не проверялось: M6, M13 — TSK-022) |
| TSK-011 ContractWindowAugmenter | Complete (авто 2026-10-10, кусок 3: `UI/ContractWindowAugmenter` — postfix `ContractWindow.Start` ставит под полем «Вероятность» ряд из двух клонов кнопки игры «Почему?» (открывает `OddsPanel`) и «Журнал» (`LogWindowLauncher`); postfix `ContractWindow.UpdateProbabilityText` дописывает `ct.odds.actual` («40% (факт. 21 %)») только для CPU, прошедшего требования, при `showActualProbability`, рабочем `ContractInspector` и контракте с предложениями; закрытое окно не считается (NFR-1), при открытии его обновляет `ContractWindowWatcher`; выключение фичи прячет ряд и возвращает игровой текст (`SetVisible`); приватные поля окна ищутся через `AccessTools` (все 4 и метод сверены с настоящим `Assembly-CSharp.dll`), нет члена — часть выключена с записью в `ModLog`; оба патча `Optional`; чистая логика `Logic/ProbabilityLabel`; кнопки — клоны `Widget.Button`, тему перекрашивает игра; положение кнопок (ряд под полем или наложение справа, если раскладки нет) и вид в игре не проверялись: M1, M3, M4, M10 — TSK-022; см. `mod/requests/contract-transparency.md`, п. 7) |
| TSK-012 OddsPanel | Complete (авто 2026-10-10, кусок 3: `UI/OddsPanel` — окно `contract-transparency.odds` (`WindowTemplate.Panel`, 560 x 520, прокрутка), заполняется при каждом открытии и при смене темы; три строки требований красным и зелёным с числами и недостающей величиной, итог игры, пометка «расхождение с игрой» (`Logic/RequirementLines`, AC-1.1…1.3); для проходящего CPU — итог (шанс игры, фактическая вероятность), таблица лучших предложений по компаниям в порядке розыгрыша (компания, CPU, IPS, ванильный и игровой шанс, факт.; строка игрока выделена), «Модификаторы модов: ×k» с источниками из `ModModifiers.Explain` (фича, причина, множитель; «(ограничено)» — `core.explain.capped`), «затенено», «что изменить» (IPS для 50 % и для первого места), «предложений нет», «первое в очереди со 100 % всегда выигрывает», «расхождение с игрой» (`Logic/OddsLines`, AC-2.1…2.5); для контракта без предложений и для недоступного `ContractInspector` показывает пояснение; тесты `RequirementLinesTests`, `OddsLinesTests`; в игре не проверялось: M1, M4, M14 — TSK-022; блок «возможные участники» — TSK-017) |
| TSK-013 OfferReviewDialog | Complete (авто 2026-10-10, кусок 3: `UI/OfferReviewDialog` — окно `contract-transparency.review` на `WindowTemplate.Modal` (время стоит), 600 x 540 без прокрутки: контракт и предложение двумя колонками (все поля FR-3.2), под ними предупреждения FR-3.3 или «рисков не найдено» (`Logic/ReviewLines` + `Logic/RiskLines`, AC-3.1); «Предложить» проверяет `ContractManager.ContractExists` и зовёт `OfferGate.ConfirmAndSend`, «Отмена», крестик, выключение фичи и конец сцены пишут `OfferCancelledOnReview` ровно один раз; «Больше не спрашивать для безопасных» (виден в режиме «Всегда», `ConfirmPolicy.AfterDontAsk`) ставит `confirmMode` = «Только рискованные»; `OfferReviewScreen` сначала открывает это окно, а если его показать нельзя — прежний вопрос `ModUI.Confirm`, а если и его нельзя — предложение уходит по-ванильному; тесты `ReviewLinesTests`, `ConfirmPolicyTests`, `UiSourceTests`; в игре не проверялось: M2, M3 — TSK-022) |
| TSK-014 ContractLogWindow | Complete (авто 2026-10-10, кусок 3: `UI/ContractLogWindow` — окно `contract-transparency.log` (`WindowTemplate.Panel`, 720 x 560): вкладки «Журнал» и «Предпросмотр автоматики», шесть фильтров `ModUI.Dropdown` (`LogFilter`), записи от новых к старым строками-кнопками (дата, CPU и контракт, фраза записи цветом события; 40 на страницу и «Показать ещё»), панель подробностей: для записи — когда, что, кто, фраза и все числа с названиями (`Logic/LogLines.Details`), для итога тендера — исход, ваш CPU, победитель, таблица предложений в порядке розыгрыша и снятые автоотменой (`FindTender` + `LogLines.TenderDetails`); открывается из окна контрактов («Журнал»), страницы фичи в хабе («Контракты: журнал», `ctx.HubPage`) и действия письма (`LogWindowLauncher.SetOpener`); тесты `LogLinesTests`, `PanelStringsTests`; в игре не проверялось: M5, M6, M9 — TSK-022) |
| TSK-015 AutomationPreviewPanel | Complete (авто 2026-10-10, кусок 3: `UI/AutomationPreviewPanel` — вкладка окна журнала: `AutomationLogger.Preview()` (те же решения, что у журнала прогона, без `AddOffer`), дата следующего прогона и дни до него, порог `Player.ContractProfitMargin`, счётчики и строки «будет отправлено / пропущено по марже / игра отклонит / уже предложено» (`Logic/PreviewLines`, AC-9.1: порог 40 % — 1 отправка и 2 пропуска по марже, 35 % — 2 отправки); при выключенной автоматике — пометка «31 декабря ничего не будет отправлено»; «Обновить» пересчитывает по текущему порогу; до 60 строк, остальное одной строкой; тест `PreviewLinesTests`; в игре не проверялось: M5 — TSK-022) |
| TSK-016 BusinessSignGate (ModUI.Confirm) | Complete (авто 2026-10-10, кусок 3: `Patches/BusinessSignGate` — prefix `ContractNegotiationWindow.SignContract` по `Logic/SignGatePolicy` откладывает подпись (режим «Никогда» и своё обращение под `BypassScope` проходят; клик при открытом вопросе проглатывается) и зовёт `ModUI.Confirm` с условиями (поставщик, клиент, срок в годах, доля линий, наценка, эксклюзивность, штрафы, оценка согласия — из приватных полей окна, `Logic/DealLines`); «Подписать» вызывает оригинал под `BypassScope` (сброс в `finally`), «Нет» ничего не меняет; если вопрос показать нельзя или поле окна не найдено — подпись по-ванильному; postfix пишет `BusinessSigned` с условиями числами; своего окна нет; оба патча `Optional`; девять полей и метод сверены с настоящим `Assembly-CSharp.dll`; тесты `SignGatePolicyTests`, `DealLinesTests`, `UiSourceTests`; в игре не проверялось: M8 — TSK-022) |
| TSK-017 Возможные конкуренты | Complete (авто 2026-10-10, кусок 4: `ContractInspector.PotentialAiBidders(contract)` — лучший CPU каждой не обанкротившейся ИИ-компании среди тех, что проходят `FailsAtAnyRequirement`, не сняты (`IsRetired`), продаются по контрактам (`SellOnContracts`) и дают маржу `(цена − RealUnitCostWithNewPrice(цена)) / цена` не ниже `ai.contractMarginThreshold` из `rules.json` (правило — чистая `Logic/AiBidRules`, порядок вопросов от дешёвых к дорогим, как в `AIBehaviourContract.OnTick`: `margin < 0.45f` в `float`, NaN не отсекается, как в игре); `ContractInspector.BuildPotential` (+ `PotentialReport`, `OddsReport.BestOffers`) считает число ежемесячных ходов ИИ до даты рассмотрения (`Logic/AiMoveCalendar`: первые числа месяцев строго после сегодня и строго до месяца рассмотрения — розыгрыш идёт на раннем тике 1-го числа, ход ИИ — на позднем) и не опрашивает ИИ, если ходов не осталось; `Logic/PotentialBidders` оставляет в списке тех, кто что-то меняет (компания без предложения или CPU быстрее её текущего), и считает пессимистичную вероятность через `OddsMath.PessimisticProbability` (множитель модов — на кандидате); `Logic/PotentialLines` + блок в конце `UI/OddsPanel`: «Возможные участники до рассмотрения (оценка): N», строки «компания: CPU, N MIPS» от быстрых к медленным (до 12, остальные — «…и ещё N»), «Если все они подадут предложения: ваша вероятность X % (сейчас Y %)» (красным ниже `risk.lowActualChance`), пометка «Оценка: фокус ИИ на типах контрактов не учитывается…» и, при `Player.DifficultyLevel == 5`, «На сложности 5 ИИ активнее…»; AC-11.1 (50 и 70 против 60 → 2 участника, вероятность по трём предложениям 28,5 %) закрыт тестом; 8 ключей `ct.odds.potential.*` в ru и en; в игре не проверялось (где лежит блок, имена компаний и CPU, пометка на сложности 5) — за владельцем, TSK-022)|
| TSK-018 События и предикаты ct.* | Complete (авто 2026-10-10, кусок 4: `ContractTransparencyEvents` (публичный статический класс) — `OfferSent(contractKey, cpuId, source)` и `TenderResolved(TenderResultDto)`, поднимаются через `ModHooks.Raise` (ошибка подписчика изолируется и засчитывается его фиче); `OfferSent` — из `OfferGate.Record` (postfix `AddOffer`) один раз на предложение, которое реально добавилось (`OfferEventRules.WasSent`: список вырос), после счётчика и строки журнала, для ручной отправки (с экраном проверки и без), автоматики и API (`ManualAutoConfirmed` подписчик видит как `Manual`); `TenderResolved` — из `TenderRecorder.Finish` после записи в журнал, не в перемотке и не при загрузке, только для итога, который журнал принял, подписчик получает копию (`TenderResultCopy`, ничего общего с журналом); предикаты `ct.offersSent` и `ct.tendersWon` (`atLeast` ≥ 0, целое; неизвестный параметр — ошибка с путём узла; счётчики — игрока, чужой субъект даёт false) — чистая `Logic/ContractPredicates`, регистрация `ContractConditions.Register()` из `Register` фичи через `ModConditions.Register`; счётчики — `ContractLogModel.OffersSent/TendersWon` (`IContractCounters`), уже в секции `ModSave` и переживают сохранение (тест: запись, чтение, предикат); имена приняты правилом `ModIds.IsPredicateNameAllowed` и отклонены чужой фиче (тест на реестре ядра); AC-14.1 — тест предиката и сверка порядка в источниках; в игре не проверялось — за владельцем, TSK-022)|
| TSK-019 Строки ct.* | Complete (авто 2026-10-10, кусок 5: таблицы `L10n/{ru,en}/contract-transparency.json` — 267 ключей в каждой; `StringTablesTests`: каждый ключ-литерал из исходников и каждый ключ, который код строит из префикса (`LogLines.AllNameKeys`, тексты параметров), есть в обеих таблицах, ключи и номера `{n}` совпадают, пустых текстов нет, нет ключей без префикса `ct.` и ключей, которых код не просит; термины сверены с `translation/ui.json` (ptru): «Предложить», «Отозвать», «Вероятность:», «Автоотмена предложений», «Автоподписание контрактов» — тексты, где фича называла переключатель игры по-своему, исправлены (`ct.risk.autoReject`, `ct.log.filter.autoReject`, `ct.log.t.capacityNotChecked`, `ct.preview.off`, `ct.reason.automationOff`); деньги и IPS печатает только `GameArgFormatter` через `StringFormatter.FloatToMoney` / `FloatToIps`, таблица «ключ строки → аргументы-деньги и аргументы-IPS» сверена со всеми строками построителей, любое другое форматирование чисел запрещено тестом; `python install.py --check` — 0 ошибок; см. `mod/requests/contract-transparency.md`, п. 14) |
| TSK-020 Самопроверка и замеры | Complete (авто 2026-10-10, кусок 5: `Logic/MemberCatalog` — 6 целей патчей и 19 приватных членов из design.md (в т.ч. `Contract.GetBestOffers`, `CpuOffer.Cpu/Chance`, поля `ContractWindow` и `ContractNegotiationWindow`), у каждой строки — части фичи, которые отключает пропажа; `Logic/SelfCheckEvaluator` решает, что отключено и не делает ли пропажа цели обязательного патча фичу недоступной; `ContractSelfCheck` (из `OnEnabled`) ищет члены в игре, спрашивает `ModHooks.IsReserved` про шесть целей (тест по таблице ядра: ни одна не закреплена, `GetBestOffers` закреплена — P23 — и только вызывается), пишет находки в `ModLog`; все 25 строк сверены с настоящим `Assembly-CSharp.dll` (ReflectionOnly); тестовый флаг `debug.ct.missing.<ключ члена>` прячет член от `GameMembers` — единственной двери к приватным членам для `ContractInspector`, `ContractWindowAugmenter` и `BusinessSignGate` — и отключает только зависящую часть (модель флага и «каждый член по одному» — `SelfCheckTests`); таймеры `PerfTimer` / `TimingSites` / `TimingStats` в `OfferGate`, `TenderRecorder`, `AutomationLogger`, `TenderMailer`, а также в `BuildOdds` и `ContractLog.Read/Write`: при `ModSettings.VerboseLog` пишут `time: <место> N ms (n, avg, max, budget)`, первые 20, потом каждый сотый и любой выше бюджета; бюджеты — таблица «Performance Considerations»; `TimingTests`; в игре не проверялось: строка «selfcheck: all 25 members…», флаг на отладочной сборке, замеры 0,05 мс в день — TSK-022, см. `mod/requests/contract-transparency.md`, п. 14 и 16) |
| TSK-021 Совместимость сейвов | Complete (авто 2026-10-10, кусок 5: `SaveCompatibilityTests` и четыре фикстуры `Fixtures/{no-section,schema-1,schema-99,damaged-section}.ptmod.json` в `mod/tests/PTMod.Tests/Features/ContractTransparency/` (не в `ContractTransparency/` из Deliverables, см. п. 3 запросов); секции раздаёт настоящая `SideFileModel` ядра, файл пишет и читает настоящий `SideFileIo` поверх `FakeFileSystem`; без секции — пустой журнал без жалоб, секция появляется при следующем сохранении; `_schema` 1 — 12 записей, 2 итога, счётчики 7 и 1, всё пишется и читается обратно; неизвестные поля (в секции, записи, итоге, строках предложений и снятых предложений) игнорируются без записи в журнал; `_schema` 99 — журнал пуст, `Write()` не вызывается, секция в файле прежняя после нескольких сохранений и повторной загрузки; запись не меняет секции ядра, чужой фичи и ничьи (в т.ч. число 2^53 + 1); выключенная фича оставляет секцию как была; битая секция читается, сколько удаётся, и перезаписывается целой; 520 записей автоматики в записанном файле ≤ 256 КБ; исходник `ContractLog` сверен с участником теста; загрузка настоящего сейва в игре — M9, M11, M12, TSK-022) |
| TSK-022 Ручная проверка в игре (владелец) | Pending |
| TSK-023 Описание для игроков | Частично (авто 2026-10-10, кусок 5: `ct.hub.description` (строка фичи в хабе — три темы и пример «40 % против 60 % — 21 %») и подробный текст на странице настроек фичи в хабе — `ct.hub.help.modes|actual|example|log` с заголовками, ru и en: режимы подтверждения («Всегда», «Только рискованные», «Никогда») с кнопками «Предложить» и «Отозвать», фактическая вероятность с примером 60/40 → 21 % (0,4 × 0,4 / (1 − 0,4 × 0,6); числа сверены тестом с `OddsMath`: 78,9 / 21,1), журнал; `StringTablesTests`; **не сделано:** раздел «Прозрачность контрактов» в README — `mod/README.md` нет, корневой README — не файл этой фичи; готовый текст лежит в `mod/requests/contract-transparency.md`, п. 15, для вставки итоговым интегратором) |

## Покрытие требований

| FR / NFR | Задачи |
|---|---|
| FR-1 | TSK-004, TSK-005, TSK-012 |
| FR-2 | TSK-003, TSK-005, TSK-011, TSK-012 |
| FR-3 | TSK-004, TSK-007, TSK-013 |
| FR-4 | TSK-002, TSK-004, TSK-007, TSK-013 |
| FR-5 | TSK-008, TSK-010 |
| FR-6 | TSK-009 |
| FR-7 | TSK-008 |
| FR-8 | TSK-006, TSK-014 |
| FR-9 | TSK-009, TSK-015 |
| FR-10 | TSK-016 |
| FR-11 | TSK-003, TSK-017 |
| FR-12 | TSK-005, TSK-020 |
| FR-13 | TSK-001, TSK-002, TSK-023 |
| FR-14 | TSK-007, TSK-018 |
| NFR-1 | TSK-020, TSK-022 |
| NFR-2 | TSK-001, TSK-007, TSK-020 |
| NFR-3 | TSK-006, TSK-021 |
| NFR-4 | TSK-019 |
| NFR-5 | TSK-011, TSK-013, TSK-022 |
| NFR-6 | TSK-002, TSK-006 |
| NFR-7 | TSK-002, TSK-006, TSK-021 |
