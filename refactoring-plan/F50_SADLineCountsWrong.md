# F50: SAD line counts wrong (10+ files)

**Severity:** P2 (Medium)

**Тип:** Документация — неверная статистика

**Файлы:** `SAD/03_Current_Architecture.md`, `SAD/04_Component_Architecture.md`

---

## Описание

SAD содержит неверные подсчёты строк кода для многих файлов проекта.

### Таблица расхождений

| Файл | SAD утверждение | Реальность | Отклонение |
|------|----------------|-----------|------------|
| `MorseKeyer.cs` | ~150 | **240** | +60% |
| `PositionFixCalculator.cs` | ~130 | **224** | +72% |
| `MorseAlphabet.cs` | ~80 | **126** | +58% |
| `SonneSignalGenerator.cs` | ~90 | **146** | +62% |
| `DialConfig.cs` | ~184 | **158** | -14% |
| `ComparisonExtensions.cs` | ~50 | **34** | -32% |
| `StationState.cs` | ~30 | **16** | -47% |
| `SonneController.cs` | ~506 | **508** | ~0% (OK) |
| `Total (проект)` | ~4,200 | **4,197** | ~0% (OK) |

### Детали

- **MorseKeyer (150→240):** SAD не учёл методы `StartElement()`, `AdvanceElement()`, `GetCurrent*Ticks()`, enum `State`
- **PositionFixCalculator (130→224):** SAD пропустил `CalculateIntersection()`, `CalculateDistance()`, `CalculateBearing()`, `ToRadians()`
- **MorseAlphabet (80→126):** Пропущен `TextToPattern()` и дублирующие перегрузки `GetMorse()`
- **SonneSignalGenerator (90→146):** Пропущен `GenerateBearingTable()` (44 строки), `LookupBearing()`, `CallsignElementCount`
- **DialConfig (184→158):** SAD считает `sortedList` поля как большее количество строк
- **ComparisonExtensions (50→34):** SAD считает 34 «extension methods» — их 25, файл 34 строки
- **StationState (30→16):** SAD, вероятно, считает комментарии и пустые строки

---

## Влияние

1. Неверная оценка сложности компонентов
2. God Object анализ опирается на неверные LOC — PositionFix на 72% больше, чем SAD предполагает
3. Метрики в SAD/04 Component Metrics не заслуживают доверия

---

## Рекомендация

Обновить все LOC в SAD/03 и SAD/04. Для будущих рефакторингов указать актуальные размеры файлов.
