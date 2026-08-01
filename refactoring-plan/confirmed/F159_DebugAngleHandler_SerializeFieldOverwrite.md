# F159. DebugAngleHandler — [SerializeField] поля перезаписываются GetComponentInChildren в Start()

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | UI · DebugAngleHandler · Unity Lifecycle |
| **Файл** | `Source/UI/DebugAngleHandler.cs` |
| **Строки** | 10-11, 34-35 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// DebugAngleHandler.cs:10-11 (объявление)
[SerializeField] private TMP_Text beaconText;   // Inspector-assigned
[SerializeField] private Slider slider;          // Inspector-assigned

// DebugAngleHandler.cs:34-35 (Start)
beaconText = GetComponentInChildren<TMP_Text>(true);  // ПЕРЕЗАПИСЫВАЕТ
slider = GetComponentInChildren<Slider>(true);         // ПЕРЕЗАПИСЫВАЕТ
```

`[SerializeField]` позволяет дизайнеру назначить компоненты через Inspector. Но `Start()` безусловно перезаписывает их `GetComponentInChildren`. Это:
1. Вводит в заблуждение — Inspector показывает настраиваемые поля, которые игнорируются
2. Ломает intent — если дизайнер назначил не-child компонент

## Корневая причина

`[SerializeField]` несовместим с перезаписью в `Start()`.

## Предлагаемое исправление

Либо убрать `[SerializeField]` (если всегда ищем в children):

```csharp
private TMP_Text beaconText;   // без [SerializeField]
private Slider slider;         // без [SerializeField]
```

Либо использовать Inspector-значение с fallback:

```csharp
if (beaconText.IsNull())
    beaconText = GetComponentInChildren<TMP_Text>(true);
if (slider.IsNull())
    slider = GetComponentInChildren<Slider>(true);
```

## Зависимости

- Связан с F63 (DebugAngleHandler TMPro dependency) — тот же файл
