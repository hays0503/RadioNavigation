# F201. Полный Consol-паттерн транслируется дважды за цикл; фаза «только позывной» никогда не играет

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | Logic · StationScheduler · SonneController · State Machine |
| **Файл** | `Source/Logic/StationScheduler.cs:45-55`; `Source/Logic/SonneController.cs:421-424, 464-466`; `Source/Sound/AudioSynthesisEngine.cs:102-106`; `Source/Logic/MorseKeyer.cs:146-160` |
| **Дата обнаружения** | 2026-08-01 |

---

## Описание

`StationScheduler.GetCurrentConsolPattern()` (`StationScheduler.cs:45-55`) возвращает `CurrentConsolPattern` **и в фазе `Callsign`, и в фазе `Navigation`**:

```csharp
// StationScheduler.cs:45-55
public NavigationElementType[] GetCurrentConsolPattern()
{
    switch (CurrentPhase)
    {
        case Phase.Callsign:
        case Phase.Navigation:
            return CurrentConsolPattern;   // ← в обеих фазах
        default:
            return null;
    }
}
```

Поле `CurrentConsolPattern` инициализируется `null` (`:27`), но `UpdateStationNavigation()` каждые 0.2 с безусловно присваивает его (`SonneController.cs:421-424`), когда `IsKeyerAvailableForPatternUpdate(i)` (`:464-466`) истинно:

```csharp
// SonneController.cs:464-466
private bool IsKeyerAvailableForPatternUpdate(int i) =>
    !keyers[i].IsActive
    || schedulers[i].CurrentPhase != StationScheduler.Phase.Navigation;
```

Условие `phase != Navigation` истинно в фазе `Callsign` (и в паузах), поэтому паттерн всегда не-null задолго до первой передачи.

В `ProcessAudio` (`AudioSynthesisEngine.cs:102-106`) при неактивном кейере **приоритет отдаётся полному паттерну**, а callsign-последовательность загружается только если паттерн null:

```csharp
// AudioSynthesisEngine.cs:102-106
var consolPattern = sched.GetCurrentConsolPattern();
if (consolPattern.IsNotNull() && consolPattern.Length.IsPositive())
{
    keyers[s].LoadConsol(consolPattern, sched, stations[s].Config, stations[s].CallsignLength);
}
```

### Итоговый цикл (~133 с вместо ~120 с)

1. `Pause` (1 тик) → фаза `Callsign`: кейер грузит **полный паттерн** (позывной + точки + equisignal + тире ≈ 60-70 с) — не только позывной.
2. Паттерн заканчивается (`MorseKeyer.cs:146-160` → `OnSequenceFinished` → `PreNavPause`, 3 с) → фаза `Navigation`: кейер снова грузит **тот же полный паттерн** (≈ 60-70 с).
3. `PostNavSilence` (1 с) → `Pause` → повтор.

Результат: навигационная передача (точки → equisignal → тире) идёт **дважды за цикл**, два equisignal, позывной повторён дважды, а фаза «только позывной» фактически недостижима — окно, когда `CurrentConsolPattern` ещё null, существует лишь первые ~0.2 с сессии (до первого `UpdateStationNavigation`), когда игрок физически не в Sonne-зоне.

---

## Влияние

- Метка фазы «Callsign» в инфо-панели показывает, что играет позывной, а фактически транслируется навигационный паттерн.
- Две передачи за цикл: число точек можно прочитать дважды; если лодка движется, паттерны двух передач могут различаться (паттерн обновляется каждые 0.2 с во время пауз/фазы Callsign).
- Длительность цикла ~133 с вместо задокументированных ~120 с (56 с Callsign + 60 с Navigation + паузы).
- Не покрыто: **F145** (callsignLength первого цикла — другой сценарий), **F98** (двойной tick пауз — ортогонально), **F65/F113/F116/F175** (внутреннее состояние MorseKeyer).

---

## Сценарий воспроизведения

1. Войти в Sonne-зону, слушать любую станцию.
2. Первая передача после паузы: полный паттерн с позывным, затем второй полный паттерн в фазе Navigation.
3. Декодированный текст содержит позывной дважды за цикл.

---

## Исправление

1. `GetCurrentConsolPattern()` должен возвращать `null` в фазе `Callsign` — тогда `GetCurrentSequence()` (`StationScheduler.cs:32-43`) отдаст `CallsignPattern` и фаза позывного заработает:

```csharp
public NavigationElementType[] GetCurrentConsolPattern()
{
    if (CurrentPhase != StationScheduler.Phase.Navigation)
        return null;
    return CurrentConsolPattern;
}
```

2. Либо (если двухфазная передача паттерна задумана) — синхронизировать `IsKeyerAvailableForPatternUpdate` с фазами и исключить двойную загрузку.

---

## Зависимости

- **F145** — первый callsign-цикл: после исправления станет достижим и его сценарий (callsignLength) оживёт.
- **F98** — двойной tick пауз в `ProcessAudio` ускоряет паузы; суммарная длина цикла зависит от обоих факторов.
- **F113/F175** — переходы между типами последовательностей в `MorseKeyer` при смене фазы.
- **F77** — `StationState.Pattern` не обновляется; при рефакторинге фазы паттерна стоит пересмотреть оба.

---

## Acceptance Criteria

- [ ] В фазе `Callsign` транслируется только позывной (не полный паттерн)
- [ ] За цикл передаётся ровно одна навигационная передача и один equisignal
- [ ] Позывной декодируется один раз за цикл
- [ ] Суммарная длительность цикла ≈ 120 с (56 с + 60 с + паузы)
