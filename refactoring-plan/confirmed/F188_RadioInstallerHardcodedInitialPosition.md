# F188: `RadioInstaller` hardcodes fixed initial local position `(300, 100, 0)` without resolution scaling

**Приоритет:** P3 (Low)  
**Область:** UI · RadioInstaller · Layout  
**Файл:** `Source/UI/RadioInstaller.cs:82`  
**Дата обнаружения:** 2026-07-31  

---

## Проблема

В `RadioInstaller.cs`:

```csharp
radio.transform.SetParent(viewport, false);
radio.transform.localPosition = new Vector3(300f, 100f, 0f);
```

Координаты `(300, 100)` захардкожены в пикселях. При запуске игры на экранах с низким разрешением или нестандартным соотношением сторон (например, 4:3, 1280x720) окно радиоприемника может оказаться сдвинутым за пределы правого края Viewport.

---

## Влияние

Окно радиоприемника может оказаться частично недоступным при первом открытии на экранах с разным разрешением.

---

## Исправление

Центрировать радиопанель относительно `viewport`:

```csharp
radio.transform.SetParent(viewport, false);
radio.transform.localPosition = Vector3.zero;
```

---

## Acceptance Criteria

- [ ] Начальное положение радиопанели устанавливается в центр Viewport (`Vector3.zero`).
- [ ] Окно гарантированно видно на любых разрешениях экрана.
