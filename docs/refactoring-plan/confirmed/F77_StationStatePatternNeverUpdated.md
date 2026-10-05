# F77: StationState.Pattern никогда не обновляется после инициализации

**Приоритет:** P0 (Critical)
**Область:** Logic · SonneController · Diagnostics
**Файл:** `SonneController.cs:85, 418-423, 284`
**Дата обнаружения:** 2026-07-31

---

## Проблема

`stations[i].Pattern` устанавливается пустым в `Awake()` и **никогда не обновляется**:

```csharp
// Awake() — line 85:
stations[i].Pattern = new NavigationPattern(Array.Empty<Group>(), 8);

// UpdateStationNavigation() — line 418-423:
var consolPattern = SonneSignalGenerator.GenerateConsolPattern(
    callsign, sector.DotCount, config);
schedulers[i].CurrentConsolPattern = consolPattern;
// ← stations[i].Pattern НЕ обновляется!

// GetStationDiagnostics() — line 284:
Pattern = s.Pattern,  // ← всегда пустой
```

---

## Влияние

Панель diagnostics **всегда показывает пустой Pattern** для каждой станции. Полезность diagnostics = 0.

---

## Исправление

В `UpdateStationNavigation()` после генерации consolPattern добавить:

```csharp
stations[i].Pattern = consolPattern;
```

---

## Acceptance Criteria

- [ ] Diagnostics panel отображает реальный consol pattern для каждой станции
- [ ] Pattern обновляется при каждом цикле UpdateStationNavigation
