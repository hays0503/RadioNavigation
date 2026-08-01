# F72: SpinnerHandlerBase — Mathf.Lerp без Time.deltaTime (FPS-зависим)

**Приоритет:** P2 (Medium)  
**Область:** UI · SpinnerHandler  
**Файл:** `Source/UI/SpinnerHandlerBase.cs:93-94`

---

## Проблема

```csharp
if (UseSmoothing())
    currentAngle = Mathf.Lerp(currentAngle, targetAngle, sensitivity);
```

`sensitivity` — `[SerializeField]` с дефолтом 0.5f, **не умноженный на `Time.deltaTime`**.

`Mathf.Lerp(a, b, t)` без time-scaling даёт frame-rate-зависимое поведение:

| FPS | Фактор в секунду | Визуально |
|-----|-----------------|-----------|
| 30  | `0.5^30` ≈ 0 | Медленное движение |
| 60  | `0.5^60` ≈ 0 | В 2 раза быстрее |
| 144 | `0.5^144` ≈ 0 | Очень быстро |

На 30 FPS анимация будет в ~2 раза медленнее, чем на 60 FPS.

## Исправление

```csharp
if (UseSmoothing())
    currentAngle = Mathf.Lerp(currentAngle, targetAngle, 1f - Mathf.Pow(1f - sensitivity, Time.deltaTime * 60f));
```

Или более простой вариант с `Time.deltaTime`:

```csharp
float smoothFactor = 1f - Mathf.Exp(-sensitivity * Time.deltaTime * 60f);
currentAngle = Mathf.Lerp(currentAngle, targetAngle, smoothFactor);
```

## Риски

- Изменение чувствительности на разных частотах кадров может быть заметно
- Требуется проверить на 30, 60 и 144 FPS
- Низкий приоритет — косметическая анимация спиннера
