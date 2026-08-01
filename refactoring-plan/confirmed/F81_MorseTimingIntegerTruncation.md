# F81: MorseTiming — integer truncation: все morse-элементы на 5% быстрее

**Приоритет:** P1 (High)
**Область:** Logic · MorseTiming
**Файл:** `MorseTiming.cs:12, 18, 25, 32`
**Дата обнаружения:** 2026-07-31

---

## Проблема

```csharp
return Math.Max(1, (int)(ms / TickMs));  // (int) truncates, не округляет
```

С дефолтными значениями (DotSoundMs=125, DashSoundMs=875, ElementPeriodMs=1000):

| Element | Expected | Actual | Diff |
|---------|----------|--------|------|
| Dot sound (125ms) | 125ms | 100ms (2 ticks) | -25ms |
| Dot silence (875ms) | 875ms | 850ms (17 ticks) | -25ms |
| **Dot total** | **1000ms** | **950ms** | **-50ms** |
| Dash sound (875ms) | 875ms | 850ms (17 ticks) | -25ms |
| Dash silence (125ms) | 125ms | 100ms (2 ticks) | -25ms |
| **Dash total** | **1000ms** | **950ms** | **-50ms** |

Каждый morse-элемент играется на **50ms (5%) короче** задуманного.

---

## Исправление

```csharp
return Math.Max(1, (int)Math.Round(ms / TickMs));
```

---

## Acceptance Criteria

- [ ] Dot + silence total = 1000ms (20 ticks)
- [ ] Dash + silence total = 1000ms (20 ticks)
