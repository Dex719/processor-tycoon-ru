# Роадмап модов Processor Tycoon RU

Источник идей — `pt-modding/IDEAS.md`: веб-поиск и оценка по карте кода, 2026-10-10. Владелец выбрал две группы целиком: «чего больше всего просят игроки» и «самое интересное по fun».

## Спеки

| Волна | Спека (slug) | Идея | Размер | Зависит от |
|---|---|---|---|---|
| 0 | `mod-framework` | основа всех модов | L | решения 0001–0006 |
| 1 | `first-year-onboarding` | I01 обучение первого года через письма | M | mod-framework |
| 1 | `contract-transparency` | I03 причины отказов, подтверждение подписи, лог автоматики | M | mod-framework |
| 1 | `research-tree-ux` | I22 вкладки дерева, сравнение CPU, подсказка модификаторов | M | mod-framework |
| 1 | `bankruptcy-recovery` | I02 меню спасения и стартовая кредитная линия | L | mod-framework |
| 2 | `event-system` | I13 движок событий, выбор с таймером | L | mod-framework |
| 2 | `market-shocks` | I12 аварии фабрик, скачки цен, циклы спроса | L | event-system |
| 2 | `design-flaw-recalls` | I36 брак в выпущенных CPU: отзыв, ревизия, патч | M | event-system |
| 2 | `historical-events` | I37 календарь реальных событий индустрии | L | event-system |
| 2 | `press-reviews` | I23 обзоры и бенчмарки в прессе | M | mod-framework; связь с design-flaw-recalls |
| 3 | `rival-personalities` | I14 характеры соперников и реакции на игрока | L | mod-framework; события — через event-system |
| 3 | `rival-acquisitions` | I06 покупка и спасение соперников | L | bankruptcy-recovery |
| 3 | `scenarios-achievements` | I18 сценарии, испытания, достижения | L | mod-framework (`ModConditions`) |
| 4 | `platform-generations` | I24 поколения платформ, лояльность сокету | L | mod-framework |
| 4 | `cpu-architectures` | I09 путь архитектуры и поддержка ПО | XL | research-tree-ux |
| 4 | `gpu-product-line` | I16 линейка GPU и платформа | XL | mod-framework |

## Порядок работ

```
mod-framework ─┬─> first-year-onboarding
               ├─> contract-transparency
               ├─> research-tree-ux ──────────> cpu-architectures
               ├─> bankruptcy-recovery ───────> rival-acquisitions
               ├─> event-system ─┬─> market-shocks
               │                 ├─> design-flaw-recalls <── press-reviews
               │                 ├─> historical-events
               │                 └─> rival-personalities (реакции как события)
               ├─> scenarios-achievements
               ├─> platform-generations
               └─> gpu-product-line
```

1. **Волна 0 — каркас.**
   - Решения 0001–0006 → одобрение владельца.
   - Спайк: `PTMod.dll` грузится, Harmony работает на Unity 6 Mono.
   - Затем `mod-framework` целиком.
2. **Волна 1 — просьбы игроков.** Четыре независимые спеки, можно параллельно.
3. **Волна 2 — события.** Сначала `event-system`, потом контент-паки.
4. **Волна 3 — соперники и мета.**
5. **Волна 4 — большой контент.** Каждую XL-спеку можно резать на части после design-фазы.

## Процесс

- Все спеки — Feature Spec в статусе **Draft**. Фазы requirements/design/tasks написаны сразу по просьбе владельца. До реализации нужно явное одобрение владельца (гейт).
- Реализация идёт строго по `tasks.md`: чекбоксы и таблица Progress синхронизируются.
- Проверка — по разделу Testing в `design.md`. Ручной прогон игры делает владелец.
