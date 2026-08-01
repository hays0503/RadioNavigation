# F10: Build System & Configuration

**Приоритет:** MEDIUM
**Область:** Infrastructure
**Тип:** Portability, maintainability

---

## Проблема

Система сборки завязана на абсолютный путь Steam, не имеет валидации, и содержит рассинхронизированные версии. Мод невозможно собрать на машине с нестандартной установкой UBOAT без правки файлов.

---

## Evidence

### 1. 14 hardcoded HintPath в csproj

`radionavigation.csproj:13-73` — каждая ссылка выглядит так:

```xml
<HintPath>C:\Program Files (x86)\Steam\steamapps\common\UBOAT\UBOAT_Data\Managed\Assembly-CSharp.dll</HintPath>
```

14 DLL-ссылок, все с одним абсолютным путём. Нет MSBuild variable indirection (`$(GamePath)`). При использовании `build.ps1 -GamePath` путь из параметра используется для `/references` csc.exe, но `.csproj` для IDE (Visual Studio, Rider) остаётся сломанным — Intellisense не работает без ручной правки.

**Затрагивает:** Visual Studio, Rider, JetBrains, любой IDE-билд.

---

### 2. build.ps1 дублирует 14 путей

`build.ps1:8-24` — второй список тех же самых 14 путей:

```powershell
$references = @(
    "$gamePath/UBOAT_Data/Managed/Assembly-CSharp.dll"
    "$gamePath/UBOAT_Data/Managed/UnityEngine.dll"
    ...
)
```

Два места для поддержки. Добавление новой DLL-ссылки требует правки и `.csproj`, и `build.ps1`.

---

### 3. build.ps1 без валидации

`build.ps1` (полный файл):

| Issue | Строка | Описание |
|-------|--------|----------|
| No `-ErrorAction Stop` | отсутствует | Нефатальные ошибки игнорируются |
| No `Set-StrictMode` | отсутствует | Использование неопределённых переменных — silent |
| Нет проверки GamePath | отсутствует | Если `$GamePath/UBOAT_Data/Managed/` не существует — csc.exe падает с криптической ошибкой |
| Нет проверки `csc.exe` | отсутствует | Если C# compiler не на PATH — невнятная ошибка |
| Нет проверки AssetBundle | отсутствует | `Bundles/testui` (48 MB) — необходим для runtime, не проверяется при сборке |
| Нет инкрементальной сборки | 31-37 | csc.exe вызывается всегда, независимо от изменений |
| Output в корень проекта | 29 | `radionavigation.dll` кладётся в корень, dirty-т рабочую директорию |

---

### 4. Version mismatch

| Файл | Строка | Текущее | Должно быть |
|------|--------|---------|-------------|
| `Manifest.json` | 3 | `"version": "2.0"` | `"2.0.0"` (semver) |
| `radionavigation.deps.json` | 10 | `"radionavigation/1.0.0"` | `"radionavigation/2.0.0"` |
| `radionavigation.deps.json` | 18 | `"radionavigation/1.0.0"` | `"radionavigation/2.0.0"` |

Версия `"2.0"` не соответствует semver (нет патча). `deps.json` остался на `1.0.0` с прошлого релиза.

---

### 5. Stale absolute paths в obj/

`obj/Debug/radionavigation.csproj.FileListAbsolute.txt` содержит 10 путей к старой папке `Mods\testui\` (переименовано в `RadioNavigation`). Этот файл генерируется MSBuild автоматически, но при переименовании проекта не обновился — может вызвать проблемы rebuild.

### 6. GameObject.Find — хрупкий runtime путь

`Main.cs:138`:

```csharp
Transform viewport = GameObject.Find("/GUI/Viewport")?.transform;
```

- `GameObject.Find` ищет точное совпадение имени в иерархии сцены
- Путь `/GUI/Viewport` зависит от иерархии UBOAT
- Если UBOAT переименует `Viewport` или изменит структуру GUI, панель не будет найдена и мод не загрузится
- В отличие от цепочки `transform.Find()` (от корня мода), этот поиск — глобальный
- Нет fallback: если `viewport == null`, весь код навигации продолжает работать с null

### 7. Suppressed nullable warnings (7 шт.)

`radionavigation.csproj:10`:

```xml
<NoWarn>CS8600;CS8601;CS8602;CS8603;CS8604;CS8618;CS8625</NoWarn>
```

| Warning | Что подавляет |
|---------|---------------|
| CS8600 | Converting null literal to non-nullable type |
| CS8601 | Possible null reference assignment |
| CS8602 | Dereference of a possibly null reference |
| CS8603 | Possible null reference return |
| CS8604 | Possible null reference argument |
| CS8618 | Non-nullable field not initialized |
| CS8625 | Cannot convert null literal to non-nullable reference type |

Маскируют потенциальные NRE, которые уже документированы в F07. Убрать по мере фикса F07.

---

## Предлагаемое решение

### csproj

```xml
<HintPath>$(GamePath)\UBOAT_Data\Managed\Assembly-CSharp.dll</HintPath>
```

C дефолтом:

```xml
<PropertyGroup>
  <GamePath Condition="'$(GamePath)'==''">C:\Program Files (x86)\Steam\steamapps\common\UBOAT</GamePath>
</PropertyGroup>
```

### build.ps1

```powershell
$gamePath = $env:UBOAT_PATH ?? $GamePath
if (-not (Test-Path "$gamePath/UBOAT_Data/Managed/UnityEngine.dll")) {
    Write-Error "GamePath not found: $gamePath"
    exit 1
}
```

---

## Acceptance Criteria

- [ ] `.csproj` использует `$(GamePath)` для всех HintPath
- [ ] `build.ps1` использует `$env:UBOAT_PATH` с fallback
- [ ] `build.ps1` валидирует GamePath перед сборкой
- [ ] `Manifest.json` и `deps.json` версии синхронизированы
- [ ] Версия соответствует semver (`major.minor.patch`)
