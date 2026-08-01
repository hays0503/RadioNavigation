# Справочник: API игры UBOAT ↔ мод RadioNavigation

Собран 2026-08-01 по исходникам `C:\code\projects\uboat` (полный C#-проект). Использовать при фиксах G1–G4 и любых работах с интеграцией.

## Окружение

| Параметр | Значение |
|----------|----------|
| Путь к исходникам | `C:\code\projects\uboat` |
| Сборка игры | `com.uboat.game` (csproj: TargetFramework net40, LangVersion 15.0) |
| Unity | **2021.3.45f2** (UnityPlayer.dll) |
| Harmony | **0Harmony.dll v2.2.2** в `Managed\`; игра сама не патчит (0 вызовов Create/PatchAll) |
| AssetBundle мода | Unity **2021.3.33f1** (заголовок UnityFS) — совместим |
| Установленная сборка | `com.uboat.game.dll` mtime 2026-06-21 (исходники на 2 дня новее) |

## Загрузка модов (UBOAT.Game.Core.Mods)

- `ModManager.cs:17-31` — `ModManifest`: name, version, description, author, supportedGameVersions, assemblyName, permissions, steamFileId. `supportedGameVersions` **не валидируется**; неизвестные поля (напр. `dependencies`) игнорируются.
- `Mod.cs:235-270` — `LoadAssembly()`: ищет `Source\*.cs` в папке мода; если `persistentDataPath\Temp\<assemblyName>.dll` устарел (по mtime vs исходники и vs `Managed\com.uboat.game.dll`) — **перекомпилирует сам**.
- `Mod.cs:303-330` — `Compile()` (Roslyn): `DefineSymbols.Clear()` → добавляет `UBOAT`, `UBOAT_2024_1`, `UNITY_STANDALONE_WIN`. **DEBUG-символа нет** → `#if DEBUG`-блоки в рантайме не компилируются.
- `Mod.cs:115-135` — `LoadAssetBundles()`: грузит файлы из папки мода (исключая .meta/.manifest/Bundles/.trees).
- `Mod.cs:199-233` — `InitializeAssembly()`: `InjectionFramework.Instance.InjectIntoAssembly(assembly)` + вызов `OnLoaded()`.
- Путь модов: `persistentDataPath\Mods\<id>\` = `AppData\LocalLow\Deep Water Studio\UBOAT\Mods\<id>\`.
- `ModManager.cs:222` — игра-сборка: id "uboat" → assemblyName "uboat.game".

## DI (DWS.Common.InjectionFramework)

- `InjectionFramework.cs:100-111` — конструктор подписывается на `SceneEventsListener.OnSceneAwake`; `Initialize()`: `InjectIntoLoadedAssemblies("uboat")` — фильтр по имени сборки (мод «radionavigation» не проходит — инжекция мода только через явный `InjectIntoAssembly`, Mod.cs:207).
- `InjectionFramework.cs:237-243` — `InjectIntoAssembly(Assembly)` → `InjectIntoAssemblyInternal(types)`.
- `InjectionFramework.cs:131-155` — `AddInjectableInstancesFromScene` / `FromDynamicallySpawnedPrefab` — инстансы для `[Inject]`.

## RadioStory / Story (UBOAT.Game.Scene, UBOAT.Game.Scene.Stories)

Иерархия: `RadioStory : GenericStory : Story`.

| Член | Где | Доступ | Примечание |
|------|-----|--------|------------|
| `ContinueStory()` | RadioStory.cs:262 | public override bool | Таргет патча (постфикс InjectT3PLL38Decision) |
| `OnDecisionSelected(Decision)` | RadioStory.cs:402 / Story.cs:332 | protected virtual | Таргет патча (префикс HandleCustomDecisions) |
| `OnStopped()` | RadioStory.cs:126 / GenericStory.cs:472 | protected override | Таргет патча (постфикс HideRadioOnStop) |
| `InsertDecision(int, Decision)` | Story.cs:377 | protected | Единственная сигнатура; **искать на Story/RadioStory**, не на GenericStory (G2) |
| `ClearDecisions()` | Story.cs:412 | protected | Вызывается в GenericStory.ContinueStory:577 |
| `Decisions` | Story.cs:186 | public `ReadOnlyCollection<Decision>` | Обёртка над тем же листом (InsertDecision виден через неё) |
| `AddDecision(...)` ×6 | Story.cs:382-404 | protected | Разные сигнатуры |
| `SelectDecision(Decision)` | Story.cs:321-325 | public | `ApplyCost → OnDecisionSelected → CallCallback` |
| `OnUpdated()` | Story.cs:438 | protected | Не переопределён; **искать на Story** (G3) |
| `Decision.LocalizationKey` | Story.cs:24 | **public readonly поле** | **Не свойство!** (G4) |
| `Decision.CallCallback()` | Story.cs:81 | public | Вызывается из SelectDecision |
| Decision-конструкторы | Story.cs:44/57/70 | public | Мод выбирает :57 (`pars[3] == Action`) |

## Locale (UBOAT.Game.Core.Data)

- `Locale.cs:18` — `class Locale : ScriptableObjectSingleton, ILocale` — **instance**, не static.
- `RetrieveString(string key)` — Locale.cs:588; `RetrieveString(string, string)` — :613; `RetrieveString3` — :598. Оба патча мода попадают в реальные перегрузки.
- `Language` — свойство, Locale.cs:181; инициализируется из Steam (`SteamApps.GetCurrentGameLanguage()`, :283).
- Singleton: `ScriptableObjectSingleton.LoadSingleton(Type)` — ScriptableObjectSingleton.cs:28 (сигнатура мода RSI:300/317 верна).
- Имена типов для `Type.GetType`: `"UBOAT.Game.Core.Data.Locale, com.uboat.game"` и `"DWS.Common.InjectionFramework.ScriptableObjectSingleton, com.uboat.game"` — первая ветка работает.

## Аудио (контекст гипотез F01/F13/F206)

- В игре **нет** `OnAudioFilterRead` (0 вхождений) — DSP-аудио мода работает в собственном аудио-потоке.
- Игра: `RadioUsageAction` (UBOAT.Game.Scene.Characters.Actions) с `RadioTransmission`, `TransmissionStoppedInRange` (RadioStory.cs:438-450); silent-клип мода (mono) — мод-сторона.

## Прочее

- `GameObject.Find("/GUI/Viewport")` (Main.cs:138) — в коде игры не создаётся; это иерархия сцены (статически непроверяемо).
- `ResourceManager.InstantiatePrefab("Radio")` — ресурс из AssetBundle мода, загруженного лоадером.
- Story-фрагменты/радио-скрипты игры — данные (не .cs) — вне статической проверки.
