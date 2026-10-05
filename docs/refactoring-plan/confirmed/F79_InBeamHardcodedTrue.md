# F79: InBeam = true хардкод — всегда "IN BEAM"

**Приоритет:** P0 (Critical)
**Область:** Logic · SonneController · Navigation
**Файл:** `SonneController.cs:409`
**Дата обнаружения:** 2026-07-31

---

## Проблема

```csharp
stations[i].InBeam = true;  // ВСЕГДА true
```

В реальном CONSOL beam покрывает только определённую угловую секцию (typically ±2.5° от основного направления). `InBeam` должен быть `true` только когда игрок находится в пределах beam.

---

## Влияние

Индикатор "IN BEAM" / "OUTSIDE" в diagnostics **всегда показывает "IN BEAM"** для каждой станции. Не даёт пользователю полезной информации.

---

## Исправление

```csharp
float angleDiff = Math.Abs((float)((bearing + 360f) % 360f) - beaconStations[i].BaseAngle);
if (angleDiff > 180f) angleDiff = 360f - angleDiff;
stations[i].InBeam = angleDiff <= config.SectorWidthDeg / 2f;
```

---

## Acceptance Criteria

- [ ] InBeam = true только когда игрок в пределах beam
- [ ] InBeam = false когда игрок вне beam
- [ ] Diagnostics показывает корректный статус
