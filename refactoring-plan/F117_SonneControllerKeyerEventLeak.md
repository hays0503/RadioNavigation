# F117. SonneController — keyer event subscriptions leak in OnDestroy

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | Logic · SonneController · Events · Memory |
| **Файл** | `Source/Logic/SonneController.cs` |
| **Строки** | 90-91 (Awake), 112-119 (OnDestroy) |

---

## Описание

В `Awake()` для каждой станции создаются подписки на события keyer:

```csharp
// SonneController.cs:90-91
keyer.OnElementCompleted += (elemType, isCallsign) => OnStationElementCompleted(stationIndex, elemType, isCallsign);
keyer.OnSequenceFinished += () => OnStationSequenceFinished(stationIndex);
```

В `OnDestroy()` отписываются **только** `RadioHandler.OnFrequencyChanged`, `volumeSpinner.OnValueChanged` и `feedbackSpinner.OnValueChanged`:

```csharp
// SonneController.cs:112-119
private void OnDestroy()
{
    RadioHandler.OnFrequencyChanged -= OnFrequencyChanged;
    if (volumeSpinner.IsNotNull())
        volumeSpinner.OnValueChanged -= OnVolumeChanged;
    if (feedbackSpinner.IsNotNull())
        feedbackSpinner.OnValueChanged -= OnFeedbackChanged;
    // <-- keyers[i].OnElementCompleted и keyers[i].OnSequenceFinished НЕ отписаны
}
```

Каждый `keyer` (объект `MorseKeyer`) содержит `Action<NavigationElementType, bool> OnElementCompleted` и `Action OnSequenceFinished`. Lambda-обработчики захватывают `stationIndex` и `this`. Если keyer переживёт SonneController — ссылка на уничтоженный controller удерживается → **memory leak** + **potential NRE** при следующем событии.

## Корневая причина

`OnDestroy` отписывается от `RadioHandler` и спиннеров, но **пропускает** keyer events. Lambda-подписки не отписываются через `-=` (для этого нужна сохранённая ссылка на делегат).

## Предлагаемое исправление

Сохранить делегаты и отписаться в `OnDestroy`:

```csharp
private Action<NavigationElementType, bool>[] keyerElementHandlers;
private Action[] keyerSequenceHandlers;

// В Awake():
keyerElementHandlers = new Action<NavigationElementType, bool>[beaconStations.Length];
keyerSequenceHandlers = new Action[beaconStations.Length];

for (int i = 0; i < beaconStations.Length; i++)
{
    int stationIndex = i;
    var handler1 = new Action<NavigationElementType, bool>((elemType, isCallsign) =>
        OnStationElementCompleted(stationIndex, elemType, isCallsign));
    var handler2 = new Action(() => OnStationSequenceFinished(stationIndex));

    keyerElementHandlers[i] = handler1;
    keyerSequenceHandlers[i] = handler2;

    keyer.OnElementCompleted += handler1;
    keyer.OnSequenceFinished += handler2;
}

// В OnDestroy():
for (int i = 0; i < keyers.Length; i++)
{
    if (keyers[i].IsNotNull())
    {
        keyers[i].OnElementCompleted -= keyerElementHandlers[i];
        keyers[i].OnSequenceFinished -= keyerSequenceHandlers[i];
    }
}
```

## Зависимости

- Связан с F14 (Event Subscription Leaks) — та же категория, другой компонент
- Связан с F11 (Memory Leaks) — утечка через делегаты
