# Рецепт: Наблюдение за временем и погодой без поллинга

Источник: `UBOAT.Game.Core.Time.GameTime`, `UBOAT.Game.Scene.Environment.Weather`,
`UBOAT.Game.Core.Time.TimeCompressionController` (исходник игры, проверено 2026-08-24).

## 1. DI-инъекция

```csharp
[Inject] private static GameTime gameTime;      // UBOAT.Game.Core.Time
[Inject] private static Weather weather;        // UBOAT.Game.Scene.Environment
[Inject] private static TimeCompressionController timeCompressionController; // опционально
```

Инъекция в статические поля — стандарт DWS.Common.InjectionFramework; подписки делайте
в обработчике `OnSceneAwake`/`Start` мода, отписку — в `OnDestroy`.

## 2. События GameTime

| Событие | Сигнатура | Когда |
|---|---|---|
| `HourChanged` | `event Action` | смена часа (`CurrentDateTime.Hour`) |
| `DayEnded` | `event Action` | смена дня месяца |
| `MonthEnded` | `event Action` | смена месяца |
| `PauseStateChanged` | `event Action` | вкл/выкл паузы (`gameTime.Paused`) |
| `TicksSimulated` | `event Action<long>` | после каждого чанка симуляции тиков |
| `LiveTicksSimulationStarted` / `Step` / `Stopped` | `event Action` | «живой» пропуск времени |
| `PhysicsTimeStepChanged` | `event Action` | изменение fixedDeltaTime |

Ключевые свойства: `StoryTicks` (long), `GmtDateTime => new DateTime(storyTicks).AddHours(-1)`,
`CurrentDateTime`, `LocalDateTime` (через TimeZoneTracker), `TimeScale` (Parameter).

```csharp
gameTime.HourChanged += OnHour;
private void OnHour()
{
    var gmt = gameTime.GmtDateTime; // UTC-1 от storyTicks
}
```

## 3. Паттерн Parameter.Changed (погода и TimeScale)

Параметры погоды — `Parameter`: `weather.Cloudiness/Fogginess/Rain/Thunder/WindSpeed`
(и `Temperature`). Подписка:

```csharp
weather.Cloudiness.Changed += OnCloudiness;
weather.Fogginess.Changed += OnFogginess;
gameTime.TimeScale.Changed += OnTimeScale;

private void OnCloudiness(Parameter p) { float v = p.Value; }
```

Сигнатура хендлера — `(Parameter p)`; снимайте `.Value`. Параметры меняются плавно
(Perlin-модель погоды), событие летит часто при активной погоде.

## 4. Тёмное время суток (гейтинг видимости неба)

`weather.IsDarkWeather` вычисляется каждый кадр в `UpdateVisualAspects`:

```csharp
bool dark = todSky.SunZenith >= 92f
         || todSky.SampleAtmosphereOptimizedHDR(new Vector3(0f,1f,0f)).b < 0.2f
         || (float)cloudiness > 0.8f
         || (float)fogginess > 0.3f;
```

Переход состояния вызывает `DarkWeatherStarted` / `DarkWeatherEnded` (`event Action`).
Для гейтинга видимости маяков/звёзд используйте эти события, а не собственный порог:
они уже объединяют ночь, облачность и туман. Смежные свойства:
`IsNight => todSky.SunZenith >= 90f`, `VisualDetectabilityFactors` (суммарный фактор
видимости, обновляется редко — см. п.5), `ApparentWeatherDarkness`.

## 5. Ловушка: LowFrequencyUpdate = раз в 5 секунд

`Weather.LowFrequencyUpdate` зарегистрирована как
`executionQueue.AddTimedUpdateListener(LowFrequencyUpdate, 5f * Random.value)` и
возвращает `5f` (интервал повтора). Она пересчитывает кэшированные геттеры:
`VisualDetectabilityFactors`, `TotalRadioModifier`, `RadarDetectabilityFactors`,
`PassiveHydrophoneDetactabilityFactors`, `SonicLayerDepth`.
**Эти значения могут быть устаревшими до 5 с** — не читайте их как мгновенные.
Мгновенные величины (`Fogginess.Value`, `FogDensity = Lerp(0.0004, 0.02, Fogginess)`,
`Cloudiness.Value`) пересчитываются каждый кадр.

## 6. Определение сжатия времени

```csharp
timeCompressionController.TimeCompressionChanged += OnTC;
private void OnTC()
{
    bool on = timeCompressionController.TimeCompression;   // bool-флаг
    float scale = gameTime.TimeScale.Value;                // текущий множитель
    float artificial = gameTime.ArtificialTimeScale;       // доп. множитель (TC)
}
```

Также следите за `gameTime.Paused` через `PauseStateChanged`. Во время пропусков
(`SimulateTicks`) `TicksSimulated(long)` приходит пакетами до ~3 часов тиков;
`LiveTicksSimulation*` сигнализируют пошаговый «живой» пропуск (шаг каждые ~2 с).
