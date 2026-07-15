# Фаза 3: Рефлексия — защита и документирование

**Приоритет:** Важно | **Сложность:** Средняя | **Шагов:** 11 (из 11) + расширение 3.4

---

### Шаг 3.1 — ReflectionGuard в RadioStoryIntegration (`RadioStoryIntegration.cs`)
- Добавить проверку `Application.version` или наличия типов в `Init()`
- Если тип не найден — лог `LogError` с версией игры и инструкцией
- Вынести magic string `"stage"` в константу с комментарием

**✅ Нужен.** При обновлении UBOAT: типы рефлексии могут измениться. Без проверки — silent failure, мод не работает, нет диагностики.

---

### Шаг 3.2 — IList приведение (`RadioStoryIntegration.cs:100-110`)
- Заменить `GetProperty("Count")` / `GetProperty("Item")` на приведение к `System.Collections.IList`
- Убрать 2 уровня рефлексии, получить проверенный код

**✅ Нужен.** `RadioStoryIntegration.cs:100-104`: `GetProperty("Count")` + `GetProperty("Item")` — 2 reflection calls на каждый story step. `IList` cast — 1 вызов, типобезопасно, быстрее.

---

### Шаг 3.3 — ctorArgs fallback (`RadioStoryIntegration.cs:122-123`)
- Заменить `Array.Empty<object>()` на `null` или проверить тип параметра перед присвоением
- Добавить fallback если параметров > 6

**✅ Нужен.** `RadioStoryIntegration.cs:123`: `ctorArgs[i] = Array.Empty<object>()` — если параметр ожидает не `object[]` (например `int`, `string`) → `InvalidCastException` при `Invoke()`.

---

### Шаг 3.4 — GetCurrentLanguage: упрощение (`RadioStoryIntegration.cs:165-193`)
- Попробовать прямой доступ: кэшировать `PropertyInfo("Language")` в `Init()`
- Fallback на текущую цепочку рефлексии
- Добавить логирование в catch (сейчас пустой `catch`)

**✅ Нужен.** `RadioStoryIntegration.cs:165-193`: 6 reflection calls + пустой `catch` при каждом вызове. Кэширование в `Init()` убирает повторные reflection lookups. Лог в catch помогает при отладке. **Дополнительно**: `RadioStoryIntegration.cs:189-191` — пустой `catch` в `GetCurrentLanguage()` без `Debug.LogWarning`. Информация об ошибке потеряна — невозможно диагностировать почему определение языка не сработало. Добавить `Debug.LogWarning("[TestUI] GetCurrentLanguage failed: " + ex)` в catch.

---

### Шаг 3.5 — "Radio/LeaveStation" magic string (`RadioStoryIntegration.cs:203`)
- `"Radio/LeaveStation"` используется напрямую в сравнении `key == "Radio/LeaveStation"`
- В отличие от `LocaleKey` (вынесен в `private const`), этот ключ — hardcoded строка
- Вынести: `private const string LeaveStationKey = "Radio/LeaveStation";`
- Аналогично вынести `"Stories/"` prefix: `private const string LocalePrefix = "Stories/";`
- Итого: все 3 магических строки — `LocaleKey`, `LeaveStationKey`, `LocalePrefix`

**✅ Нужен.** `RadioStoryIntegration.cs:142`: `key == "Stories/" + LocaleKey` — hardcoded `"Stories/"`. Строка 203: `key == "Radio/LeaveStation"` — hardcoded. Опечатка = баг, нет компиляционной проверки.

---

### Шаг 3.6 — Debug.Log в hot path: цикл по decisions (`RadioStoryIntegration.cs:100-104`)
- `InjectT3PLL38Decision` вызывается при **каждом** ContinueStory (story step)
- Строка 100: `GetProperty("Count").GetValue(decisions)` — 1 reflection call
- Строки 102-109: цикл `for (int i = 0; i < count; i++)` — N reflection calls + Debug.Log
- При типичном количестве 5-8 decision'ов — это 5-8 отражений + 5-8 логов **на каждый story step**
- Решение: обернуть цикл логирования в `#if DEBUG` или `UnityEngine.Diagnostics.Debug.isDebugBuild`
- Оставить только `Debug.Log("[TestUI] Decision count=" + count)` за пределами `#if DEBUG`

**✅ Нужен.** `RadioStoryIntegration.cs:102-109`: цикл с `Debug.Log` **на каждый story step** — 5-8 reflection calls + 5-8 string allocations. GC pressure + лог-шум.

---

### Шаг 3.7 — HandleCustomDecisions: Init() в горячем пути (`RadioStoryIntegration.cs:186`)
- Каждый выбор decision → `HandleCustomDecisions` → `Init()` (строка 187)
- `Init()` имеет guard `_initialized`, но после guard идёт 3 null-проверки → 3 обращения к static полям
- При частом переключении decision'ов — лишние проверки
- **Решение**: вынести guard в начало `HandleCustomDecisions` с early return: `if (!_initialized) { Init(); if (!_initialized) return true; }`

**✅ Нужен.** `RadioStoryIntegration.cs:197`: `Init()` вызывается **каждый** выбор decision. Guard `_initialized` + 3 null-check после — лишняя работа при каждом клике.

---

### Шаг 3.8 — IList fallback при неудачном приведении (`RadioStoryIntegration.cs:100-110`)
- Шаг 3.2 предлагает заменить `GetProperty("Count")` / `GetProperty("Item")` на `IList`
- **Но**: если `decisions` не реализует `IList` — `as IList` вернёт `null`
- Добавить fallback: если `decisions as IList` == null → вернуться к текущей рефлексии с предупреждением

**✅ Нужен.** Защита от `IList` cast failure — defensive programming. Если `decisions` не реализует `IList` — fallback на текущую рефлексию с предупреждением.

---

### Шаг 3.9 — Init(): _initialized ставится до полной проверки (`RadioStoryIntegration.cs:25`)
- `_initialized = true` на строке 25 выполняется **до** поиска `_decisionCtor` (строка 44-55) и `_stageField` (строка 60)
- Если `_insertMethod == null` → early return на строке 39, но `_initialized` уже `true`
- Следующий вызов `Init()` → guard на строке 23 пропускает инициализацию → `_decisionCtor == null`, `_stageField == null`
- `InjectT3PLL38Decision` ловит это на строке 76 (`if (_decisionCtor == null || _stageField == null) return`), но **без повторной попытки** — мод молча не работает
- **Решение**: вынести `_initialized = true` в **конец** `Init()` (после всех проверок), либо добавить retry логику
- **Приоритет**: важно — если reflection targets не найдены при старте, мод никогда не заработает без перезапуска

**✅ Нужен.** `RadioStoryIntegration.cs:25`: `_initialized = true` до проверки `_decisionCtor`/`_stageField`. При неудачной инициализации — `_initialized` = true, retry невозможен, мод молча не работает.

---

### Шаг 3.10 — InsertDecision/OnUpdated: нет проверки результата Invoke (`RadioStoryIntegration.cs:128,131`)
- `_insertMethod?.Invoke(__instance, new object[] { insertIndex, decision })` — возвращает `object` (void метод → null). Если `InsertDecision` выбрасывает исключение — catch на строке 134 ловит, но story уже в неконсистентном состоянии (decision создан, не вставлен)
- `_onUpdatedMethod?.Invoke(__instance, null)` — аналогично: если `OnUpdated` выбрасывает → UI не обновлён, decision вставлен но не отображён
- `?.Invoke` — если метод == null, вернёт null без ошибки, но decision не вставлен → story без T3PLL38
- **Решение**: добавить проверку: `try { _insertMethod.Invoke(...); } catch (Exception ex) { Debug.LogError("[TestUI] InsertDecision failed: " + ex); }` отдельно от общего catch
- **Приоритет**: важно — диагностика неконсистентного состояния

**✅ Нужен.** `RadioStoryIntegration.cs:128,131`: `_insertMethod?.Invoke` и `_onUpdatedMethod?.Invoke` без отдельной обработки ошибок. Если один из них падает — story в неконсистентном состоянии, нет детального лога.

---

### Шаг 3.11 — HandleCustomDecisions: return false semantics не документированы (`RadioStoryIntegration.cs:195-218`)
- `HandleCustomDecisions` — Harmony prefix на `RadioStory.OnDecisionSelected`
- `return false` → оригинальный метод **не вызывается** (строка 215: T3PLL38决策 → ToggleRadio → return false)
- `return true` → оригинальный метод вызывается (строка 208: LeaveStation, строка 218: default)
- **Проблема**: нет документации когда return false vs true. Если разработчик добавит новый decision — может случайно вернуть false и сломать game flow
- **Решение**: добавить XML-комментарий или const: `private const bool PreventDefault = false; private const bool AllowDefault = true;`
- **Приоритет**: важно — предотвращение багов при расширении

**✅ Нужен.** `RadioStoryIntegration.cs:195`: `bool HandleCustomDecisions` — Harmony prefix. `return false` блокирует оригинальный метод. Без документации/констант — легко сломать game flow при добавлении нового decision.
