# F160. InformationPanelHandler — дублированные IsGroundActive/GetZoneIndex обходят иерархию вместо использования handler

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | UI · InformationPanelHandler · Code Smell |
| **Файл** | `Source/UI/InformationPanelHandler.cs` |
| **Строки** | 220-241 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

`InformationPanelHandler` уже держит ссылки на `groundHandler` и `zoneHandler`, но `IsGroundActive()` и `GetZoneIndex()` игнорируют их:

```csharp
// InformationPanelHandler.cs:220-226
private bool IsGroundActive()
{
    Transform bgT = transform.Find("Ground/Background");  // ручной обход иерархии
    if (bgT.IsNull()) return false;
    Image bgImage = bgT.GetComponent<Image>();
    return bgImage.IsNotNull() && bgImage.color.a > GroundHandler.ActiveAlphaThreshold;
}
```

```csharp
// InformationPanelHandler.cs:228-241
private int GetZoneIndex()
{
    Transform radioZone = transform.Find("RadioZone");    // хардкод имён
    // ... 5x transform.Find + 5x GetComponent<Image>
}
```

**Проблемы:**
1. **String-coupling** — если имена children изменятся, эти методы молча сломаются
2. **Stale-state** — alpha-based heuristic может diverge от handler state tracking
3. **Performance** — 5x `transform.Find()` + 5x `GetComponent<Image>()` каждый вызов `RefreshAll()`

## Корневая причина

Дублирование state queries вместо использования имеющихся handler references.

## Предлагаемое исправление

```csharp
private bool IsGroundActive() =>
    groundHandler.IsNotNull() && groundHandler.IsGroundEffectivelyActive();

private int GetZoneIndex() =>
    zoneHandler.IsNotNull() ? zoneHandler.CurrentIndex : 0;
```

## Зависимости

- Связан с F96 (InformationPanelHandler GetComponent vs parent) — тот же файл, тот же паттерн
- Связан с F32 (5 GetComponentInChildren подряд) — та же проблема per-frame allocations
