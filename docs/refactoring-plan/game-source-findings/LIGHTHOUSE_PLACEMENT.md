# Руководство: размещение 3D-префаба мода на географической точке + точка с подписью на карте

**Дата:** 2026-08-13
**Цель:** поставить 3D-модель (префаб `lighthouse` из собственного AssetBundle мода `Bundles/testui`) на координаты 53.519973, 8.127701 (Вангероге, Северное море) и показать на карте «точку с подписью».
**Результат:** реализовано, проверено в игре двумя чистыми прогонами (сейв-лоад Autosave_1/Autosave_2): `placed at Lat 53.519970 Lon 8.127701`, маркер `Lighthouse Wangerooge` на карте, ноль исключений.

**Измененные файлы:**
- `Source/Logic/LighthousePlacer.cs` — новый (установка 3D-модели как сущности песочницы)
- `Source/UI/StationMapOverlay.cs` — точка маяка + фикс fake-null в teardown
- `Source/Logic/Main.cs` — 2 строки (вызов `LighthousePlacer.EnsureRunning()`, сброс при релоаде мода)
- `build.ps1` — побочный фикс: автоопределение компилятора (см. раздел 7)

---

## 1. Исследование: источники паттернов

Два воркшоп-мода как источники готовых подходов (анализированы их исходники):

| Мод | Подход | Применимость |
|-----|--------|--------------|
| **Expanded Ice** (3667041413) | Runtime: `SandboxGroup.Create` + `SandboxEntity.Create(name, blueprint)` + `Coordinates.ToLatLong/ToWorldPosition`; `Sandbox.Instance`, `GetCountryByCode("Neutral", true)`, `AddGroup`/`AddPublicGroup`, `group.Spawn()` | **Взят за основу** — всё на C#, игра сама компилирует `Source\*.cs` |
| **Expanded Buoys** (3313392069) | Data-подход: мод кладет `Data Sheets/*.xlsx`, игра мержит строки (Mod.cs `LoadSpreadsheets` → `gameData.LoadSpreadsheet`); строки `Sandbox/Static Entities` требуют существующую группу | Отвергнут: нужны бинарные xlsx + существующая игровая группа в районе точки + запись в `Entities/Types` |

Дополнительно проверены статически:
- дата-шиты игры (`UBOAT_Data\Data Sheets\*.xlsx` — структура колонок `Entities/Types`, `Sandbox/Static Groups`, `Sandbox/Static Entities`);
- манифесты бандлов игры (2051 файл) — иконок `Icons/Map/Structure*` и `Icons/Map/Lighthouse` в игре **нет**, см. раздел 5.8;
- исходники игры `C:\code\projects\uboat` (assembly `com.uboat.game`, Unity 2021.3.45f2).

---

## 2. Проверенные API (file:line в исходниках игры)

| API | Где | Назначение |
|-----|-----|------------|
| `Coordinates.ToWorldPosition(float lat, float lon)` → `Vector2` (км) | Coordinates.cs:47 | перевод координат в мировое км-пространство песочницы (x = восток, y = север) |
| `Coordinates.ToLatLong(Vector2)` | Coordinates.cs:42 | обратный перевод для логов/диагностики |
| `SandboxGroup.Create<T>(name, type, position, country)` | SandboxGroup.cs:885 | создание группы (GameObject + AddComponent + поля) |
| `SandboxStaticGroup` | Sandbox.cs:1277 | игра создает «Location»-группы только этим подклассом |
| `SandboxEntity.Create(name, blueprint, purposeName = null)` | SandboxEntity.cs:1053 | создание сущности |
| `SandboxEntity.Enabled/DisplayInUI/IsPublic/RandomizeSpawnPosition/Position` | SandboxEntity.cs | флаги и позиция |
| `Sandbox.AddGroup/AddPublicGroup/FindGroup/GetCountryByCode` | Sandbox.cs:1015/1051/918/898 | регистрация, дедуп, страна |
| `GameState.State.Gameplay` | UBOAT.Game.Core/GameState.cs | гейт готовности статиков |
| `EntityType(GameDataRow)` | EntityType.cs:106 | runtime-тип сущности |
| `EntityBlueprint(EntityType, GameDataRow, Country)` | EntityBlueprint.cs:132 | blueprint из той же строки |
| `GameDataRow(string path, string[] keys, int[] keyHashes, List<string> values)` | GameDataRow.cs:34 | публичный ctor — можно строить строку на лету |
| `ResourceManager.RetrieveAsset<T>(path, referent, logErrors)` / `InstantiatePrefab` | ResourceManager.cs:529/386 | резолв ассетов по имени (бандлы модов грузит Mod.cs `LoadAssetBundles`, Mod.cs:115) |
| `MapPointUI` / `MapPointData` / `MapChartingUI.pointPrefab` (приватное поле, через рефлексию) | UBOAT.Game.UI.Map | «точка с подписью»: `Duplicate(data)`, `Name` → TMP label, `UpdatePoints` |
| Механизм инжекции статиков | InjectionFramework.cs:368-374 | переинжекция ВСЕХ "uboat"-сборок на каждый `OnSceneAwake`; `SceneEventsListener` имеет `[DefaultExecutionOrder(-9000)]` |

---

## 3. Архитектура решения

```
OnSceneStart("Main Scene") → LighthousePlacer.EnsureRunning()
  → Update (ретрай до 3600 кадров):
      1. гейт: gameState.CurrentState == Gameplay      (статики SandboxGroup инжектированы)
      2. дедуп: sandbox.FindGroup("Lighthouse Wangerooge")
      3. проба имени префаба: "lighthouse" → "Lighthouse" (регистр!)
      4. страна: GetCountryByCode("Neutral", false)    (ретрай — грузится после сейва)
      5. runtime EntityType/EntityBlueprint из GameDataRow (категория "Structure", Prefab Path = "lighthouse")
      6. SandboxGroup.Create<SandboxStaticGroup>(name, "Location", pos, country)
      7. SandboxEntity.Create(...): Enabled=true, DisplayInUI=false, IsPublic=false
      8. AddEntity → Position → AddGroup → IsGroupPublic → AddPublicGroup → Spawn()
StationMapOverlay.TryCreateMarkers():
  → 5 станций + 1 точка маяка (MapPointUI, Name="Lighthouse Wangerooge", Interactive=false)
```

Сущность — только для 3D-модели (маркер сущности отключен `DisplayInUI=false`); на карте рисуется своя точка `MapPointUI` (transient, как станции: пересоздается при открытии карты/загрузке сейва, в сейв не пишется).

---

## 4. Runtime-построение EntityType/EntityBlueprint

`EntityType(GameDataRow)` читает: col0 = имя, col1 = категория (`"Structure"` → `EntityTypeCategory.Structure`, Y префаба сохраняется), `GetString("Prefab Path")` — ключ, col13/14 — Military/IsClass. `CreateDefaultBlueprint` создает blueprint для страны Neutral. Колонки 2-17 — параметры (`EntityBlueprint` ctor читает индексы 2..16 и ключ `"Estimated Durability (AI)"`).

Строка строится на лету: 18 значений, числовые колонки = `"0"` (иначе шум `Parameter ... wrong format` в логе), `keys[9] = "Prefab Path"` → имя ассета бандла.

---

## 5. Находки-грабли (симптом → причина → фикс)

| # | Симптом | Причина | Фикс |
|---|---------|---------|------|
| 1 | NRE в `SandboxGroup.Create` на старте сцены | Статики `SandboxGroup.sandbox` инжектятся только к переходу в Gameplay (фреймворк переинжектит "uboat"-сборки на каждый OnSceneAwake, но момент готовности — Gameplay) | Гейт `gameState.CurrentState == Gameplay` + ретрай |
| 2 | `Country with code "Neutral" not found` при загрузке сейва | Страны грузятся после десериализации сейва | Ретрай `waiting for countries...`, `GetCountryByCode("Neutral", false)` (тихий) |
| 3 | Персистентный NRE при живом геймплее (3600 попыток) | `SandboxGroup.Create<SandboxGroup>` с БАЗОВЫМ классом; игра «Location»-группы создает только как `SandboxStaticGroup` | `Create<SandboxStaticGroup>` — сразу заработало |
| 4 | NRE `ClearOwnedMarkers` в `OnDestroy` при teardown | `IsNull()/IsNotNull()` боксит в `object` → сравнение по ссылке обходит Unity fake-null → `.gameObject` на уничтоженном объекте | Прямые `== null` / `!= null` (Unity-перегрузка оператора) |
| 5 | NPE при построении runtime-строки | Пустой/отсутствующий ключ `"Prefab Path"` → NPE в ctor EntityType (`prefabPath.EndsWith("/")`); <17 значений → NPE в EntityBlueprint (`GetParameter(6)` → null → `AddDeltaModifier(null)` → `GameDataModifier`) | Обязательно: ключ `"Prefab Path"` + ≥17 значений |
| 6 | Шум в логе: `Parameter at "Entities/Types", column X doesn't exist or has a wrong format!` ×11 | Пустые строки в числовых колонках | Числовые колонки = `"0"` (становятся `GameDataParameter(0f)`) |
| 7 | Префаб «не найден» | Имена ассетов бандла регистрозависимы | Проба `"lighthouse"` → `"Lighthouse"` через `RetrieveAsset` до создания blueprint |
| 8 | Иконка на карте — `Unknown` | Цепочка `Icons/Map/{IconOverride}` → `Structure - Neutral` → `Structure` → `Unknown`; в бандлах игры (проверены все 2051 манифестов) есть только `Unknown` (61×54) | Решение пользователя: «точка с подписью» — `MapPointUI` + `DisplayInUI=false` у сущности |

---

## 6. Верификация (лог-доказательства)

```
[RadioNav][Lighthouse] prefab resolved: 'lighthouse'
[RadioNav][Lighthouse] placed at Lat 53.519970 Lon 8.127701 prefab='lighthouse' group='Lighthouse Wangerooge' entity='Lighthouse Wangerooge'
[RadioNav][Lighthouse] placement finished
[RadioNav][Map] lighthouse marker name='Lighthouse Wangerooge' lat=53,519970 lon=8,127701 encounter=(904.77, 5957.84) active=True
[RadioNav][Map] transient station markers created=5 lighthouse=1
```

Нет `Prefab is missing`, нет `SpawnError`, нет NRE в teardown (фикс раздела 5.4). Два чистых прогона подряд (Autosave_1, Autosave_2) — сейв-лоад работает (дедуп по `FindGroup`).

**Поведение:** установка происходит при старте геймплея (после подгрузки стран); точка на карте transient (как станции); при перезагрузке мода `LighthousePlacer.Reset()`.

---

## 7. Побочный фикс: build.ps1

Локальная сборка `powershell -File build.ps1` падала в этом окружении: `csc` нет в PATH, framework `csc.exe` — только C# 5 (не компилирует `?.` и т.п.), Roslyn из .NET SDK требует явных ссылок на ядро (фасады игры неполны — нет `SortedList`). Фикс: автоопределение компилятора (Roslyn `csc.dll` из `dotnet/sdk` первым, потом PATH, потом framework), добавление ссылок `mscorlib/System/System.Core/System.Xml` из Framework для Roslyn-ветки, передача аргументов плоским массивом (вложенные массивы в `@(...)` ломали пути с пробелами). Результат: `Build OK -> .\radionavigation.dll`.

---

## Ссылки

- Исходники игры: `C:\code\projects\uboat` (API-карта: `GAME_SOURCE_REFERENCE.md`)
- Дата-шиты игры: `C:\Program Files (x86)\Steam\steamapps\common\UBOAT\UBOAT_Data\Data Sheets\*.xlsx`
- Реализация: `Source/Logic/LighthousePlacer.cs`, `Source/UI/StationMapOverlay.cs`
