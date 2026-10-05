# F27: ComparisonExtensions — Anti-Pattern

**Приоритет:** P2 (Medium)
**Область:** Logic · ComparisonExtensions
**Тип:** Code Style · Readability

---

## Проблема

`ComparisonExtensions.cs` определяет extension-методы `.IsNull()`, `.IsNotNull()`, `.IsPositive()`, `.IsNegative()`, `.IsZero()`, `.Negligible()` — всего ~34 метода, которые дублируют встроенные операторы C#. Это скрывает NRE-риски от статического анализатора Roslyn и ухудшает читаемость.

---

## Evidence

ComparisonExtensions.cs (весь файл, ~34 строки):

```csharp
public static bool IsNull(this object obj) => obj == null;
public static bool IsNotNull(this object obj) => obj != null;
public static bool IsPositive(this float value) => value > 0f;
public static bool IsZero(this int value) => value == 0;
```

~93 call-сайтов через 15+ файлов:

| Файл | Call-site count |
|------|----------------|
| `RadioStoryIntegration.cs` | ~30 |
| `MorseKeyer.cs` | ~15 |
| `AudioSynthesisEngine.cs` | ~5 |
| `InformationPanelHandler.cs` | ~10 |
| `PositionFixCalculator.cs` | ~3 |
| `SonneController.cs` | ~8 |
| Другие UI handlers | ~20 |

### Почему это проблема

1. **Roslyn static analysis не видит extension methods** — NRE-риски не обнаруживаются
2. **`<Nullable>enable</Nullable>` невозможен** пока все call-сайты используют extension methods
3. **Ложное чувство безопасности** — `if (x.IsNull())` выглядит защитно, но Roslyn не понимает этот pattern
4. **Избыточность** — `.IsZero()` для `== 0`, `.IsPositive()` для `> 0f`, `.Negligible()` для Math.Abs < threshold

---

## Предлагаемое решение

1. Сохранить только `.IsNegligible(float a, float b)` — реальная ценность (float comparison with epsilon)
2. Удалить `.IsNull()`/`.IsNotNull()` — заменить на `== null`/`!= null`
3. Удалить `.IsPositive()`/`.IsNegative()`/`.IsZero()` — заменить на `> 0`, `< 0`, `== 0`

---

## Acceptance Criteria

- [ ] ComparisonExtensions содержит только IsNegligible
- [ ] is null / == null / != null используются вместо IsNull/IsNotNull
- [ ] Проект может включить `<Nullable>enable</Nullable>
