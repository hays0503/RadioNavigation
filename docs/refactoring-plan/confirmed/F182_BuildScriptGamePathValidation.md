# F182: `build.ps1` doesn't validate `$GamePath` existence before invoking `csc`

**Приоритет:** P3 (Low)  
**Область:** Build · Scripting  
**Файл:** `build.ps1:1-25`  
**Дата обнаружения:** 2026-07-31  

---

## Проблема

```powershell
param(
    [string]$GamePath = "C:\Program Files (x86)\Steam\steamapps\common\UBOAT",
    [string]$Configuration = "Release",
    [string]$OutputDir = "."
)
```

`build.ps1` не проверяет существование директории `$GamePath` и ключевых сборников (`Assembly-CSharp.dll`, `UnityEngine.dll`) перед запуском компилятора `csc.exe`.

Если UBOAT установлен в нетипичный каталог или путь передан с ошибкой, `csc.exe` выдает 15+ неинформативных ошибок отсутствия меданных/референсов вместо понятной диагностики.

---

## Влияние

Усложняет диагностику сборки для разработчиков и в CI/CD пайплайнах при некорректно заданном параметре `-GamePath`.

---

## Исправление

Добавить валидацию путей в начало скрипта:

```powershell
if (-not (Test-Path $GamePath)) {
    Write-Error "[RadioNavigation] GamePath does not exist: $GamePath"
    exit 1
}

$coreRef = Join-Path $GamePath "UBOAT_Data\Managed\Assembly-CSharp.dll"
if (-not (Test-Path $coreRef)) {
    Write-Error "[RadioNavigation] Missing game managed DLLs at: $coreRef"
    exit 1
}
```

---

## Acceptance Criteria

- [ ] `build.ps1` завершается с понятной ошибкой до вызова `csc.exe`, если `$GamePath` не существует.
- [ ] Ошибка указывает точный отсутствующий путь.
