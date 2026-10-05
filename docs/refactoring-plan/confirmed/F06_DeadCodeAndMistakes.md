# F06: Dead Code, Мёртвые Параметры и Вводящие в Заблуждение Контракты

**Приоритет:** P2 (Medium)
**Область:** Codebase · Hygiene
**Блокирует:** Нет прямого блокирования, но вводит в заблуждение разработчиков и увеличивает когнитивную нагрузку

---

## Проблема

Четыре места в коде, которые либо не используются, либо игнорируют свои параметры, либо возвращают константу. Разработчик, читающий код, тратит время на понимание того, что на самом деле не работает.

---

## Evidence

### 1. `UpdateStationBeats()` — никогда не вызывается

`AudioSynthesisEngine.cs:52-63`:

```csharp
public void UpdateStationBeats(StationState[] stations, CarrierGenerator[] carriers,
    float currentFrequencyKHz, int currentZoneIndex, int sonneZoneIndex)
{
    for (int i = 0; i < stations.Length; i++)
    {
        if (stations[i].Gain.IsNegligible(0.001f))
            continue;
        float offset = currentFrequencyKHz - (stations[i].BeatFrequency - BfoBase) / 1000f;
        stations[i].BeatFrequency = Mathf.Clamp(stations[i].BeatFrequency, BfoMin, BfoMax);
        carriers[i].Frequency = stations[i].BeatFrequency;
    }
}
```

**Не вызывается ни разу** во всём проекте. Логика beat frequency вычисляется в `SonneController.UpdateStationGains()` [строка 369]:

```csharp
stations[i].BeatFrequency = Math.Max(300f, Math.Min(1800f, BfoBase + offset * 1000f));
```

Дублирование констант `BfoBase = 1000f` в обоих файлах: `SonneController.cs:12` и `AudioSynthesisEngine.cs:9`. Если `UpdateStationBeats` когда-нибудь вызовут, значения могут отличаться.

### 2. `GetForCycleDuration()` — игнорирует параметр

`ConsolCycleConfig.cs:36-39`:

```csharp
public static ConsolCycleConfig GetForCycleDuration(int cycleDurationSeconds)
{
    return Cycle120s;
}
```

Метод принимает `cycleDurationSeconds`, но **всегда возвращает `Cycle120s`**, независимо от переданного значения. Контракт метода подразумевает «получить конфиг для цикла X секунд», но реализация говорит «всегда 120s».

Вероятно, это заглушка для будущей поддержки разных длин цикла (240s, 60s). Заглушка не документирована, не логирует предупреждение.

### 3. `GetSignalBuffer()` — тривиальный алиас

`SonneController.cs:219-221`:

```csharp
public string GetSignalBuffer()
{
    return GetSignalRichText();
}
```

Просто вызывает другой метод с другим именем. Никакой дополнительной логики. При этом `GetSignalRichText()` определён публично и может быть вызван напрямую. `GetSignalBuffer()` создаёт ложное впечатление, что возвращает другой тип данных.

### 4. `ErrorKm` — всегда 0

`PositionFixCalculator.cs:112-118`:

```csharp
return new FixResult
{
    Latitude = calcLat,
    Longitude = calcLon,
    ErrorKm = 0f,    // всегда ноль!
    Valid = true
};
```

Поле `ErrorKm` всегда устанавливается в `0f`. При этом UI (`InformationPanelHandler.cs`) показывает ошибку фикса на основе этого поля и даже красит текст в разные цвета в зависимости от порога:

```csharp
if (sonneController.FixErrorKm < 10f)
    fixText.color = Color.green;
else if (sonneController.FixErrorKm < 50f)
    fixText.color = Color.yellow;
else
    fixText.color = Color.red;
```

Поскольку `ErrorKm` всегда 0, текст всегда зелёный. Система ошибок не работает, но UI делает вид, что работает. Пользователь видит «Error: 0.0 km» — это дезинформирует.

---

## Предлагаемое решение

| Проблема | Решение |
|----------|---------|
| `UpdateStationBeats()` | Удалить (dead code) |
| `GetForCycleDuration(int)` | Либо реализовать lookup table для разных длин, либо убрать параметр и заменить на константу `Cycle120s` с `Debug.LogWarning` |
| `GetSignalBuffer()` | Удалить (алиас); переименовать `GetSignalRichText` если нужно |
| `ErrorKm = 0f` | Либо удалить поле из `FixResult`, либо реализовать RMS-отклонение от weighted mean всех точек пересечения |

---

## Acceptance Criteria

- [ ] `UpdateStationBeats()` удалён
- [ ] `GetForCycleDuration()` либо работает корректно, либо не имеет параметра
- [ ] `GetSignalBuffer()` удалён
- [ ] `ErrorKm` возвращает осмысленное значение или удалён из `FixResult`/UI
