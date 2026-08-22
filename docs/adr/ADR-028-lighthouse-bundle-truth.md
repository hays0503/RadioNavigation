# ADR-028: Геометрия бандла — единственная истина для маяка

> Статус: Accepted (2026-08-22)  
> Связи: `Source/Logic/LighthouseBeamGlow.cs`, `Source/Logic/LighthousePlacer.cs`, `Source/Logic/LighthouseRotator.cs`

## Решение

- **Удалён legacy scale-repair** из `LighthouseBeamGlow.Initialize` (нормализация масштабов lamp/rotator/root для старого «сломанного» бандла). Геометрия в `Bundles/testui` — единственная точка истины; мод масштабы не трогает.
- **Позиция света** — `LighthouseRotator → Spot Light` (`GetWorldPosition()` возвращает `spotLight.transform.position`; fallback: rotator → container-снимок → self). Убран искусственный `LightVisualOffsetY = -0.5`.
- **Flare в device-режиме** (бинокль/перископ/UZO): `UpdateFlare` не применяет `FlareMaxDistanceM = 1200` когда активна device-камера — зеркалит `FlareActiveByLogic` и игру (`LighthouseLight.cs` без лимита). Far clip device-камеры поднимается до 30000.
- **Размер flare** — `FlareSizeScale = 1.25` (+25% к screen-space размерам элементов glow/streak/star, повторно применяется при каждом включении flare — HDRPLensFlare может пересоздавать элементы).
- **Эмиссия окон** модом сознательно не ведётся: `FindWindowRenderers` ищет `Standard (DWS, Lighthouse)`, новый prefab использует `DeepStandard (Lighthouse Windows)` → `windows=0`, `_EmissionColor`/`_RotationOffset` не пишутся. Решение принято пользователем — текущий вид устраивает.

## Причина

- Scale-repair был хрупким: он «усаживал» исправленный prefab в 3 раза и зависел от имени корня (`lighthouse`) и порогов масштаба.
- Привязка к `Spot Light` согласует все эффекты (dot, flare, spot-интенсивность, beam) с реальной геометрией и переживает rescale контейнера игрой.
- В бинокле flare — единственный рабочий рендерер (screen-space dot не позиционируется в device-проекции), поэтому лимит 1200 м там был ошибкой: свет пропадал на дистанции > 1.2 км.

## Проверка

- Build: 0 ошибок; тесты: 232 PASS.
- В игре: свет маяка виден в бинокле на 2+ км (мигает при проходе луча), размер flare +25%.

```json
{"id":"ADR-028","decision":"Геометрия бандла = истина; свет на Spot Light; flare без лимита в device-режиме","reason":"Scale-repair ломал исправленный prefab; бинокль требовал flare на любой дистанции","fallback":"Fallback позиции: rotator.position → container.TransformPoint(rotatorLocalPosition)"}
```