# F196: `build.ps1` — `/debug:full` с `/optimize+` для всех конфигураций

**Приоритет:** P3 (Low)  
**Область:** Build · Configuration  
**Файл:** `build.ps1:33-34`  
**Дата обнаружения:** 2026-08-01  

---

## Проблема

```powershell
# build.ps1:33-34
/debug:full
/optimize+
```

Оба флага применяются **всегда**, для любой конфигурации (`-Configuration`).

- `/debug:full` — генерирует **полную** отладочную информацию (включая имена локальных переменных, `pdb` с полной символикой). Для Release обычно используется `/debug:pdbonly` или флаг вообще не указывается.
- `/optimize+` — включает оптимизацию. Для Debug-сборки типично используется `/optimize-` (отключить оптимизацию для удобного дебага).

Комбинация `/debug:full` + `/optimize+` означает: оптимизированный код с полной отладочной информацией. Это приводит к:
1. Более большому размеру DLL/PDB по сравнению с `/debug:pdbonly`.
2. Путанице при дебаге: оптимизатор может удалить переменные, выполнить inlining, hoisting и т.п., из-за чего breakpoints и пошаговая отладка работают некорректно.

---

## Влияние

- F10 (Build Config Issues) документирует проблемы с HintPath, дублированием путей, отсутствием валидации, version mismatch и строгими nullable warnings — но не затрагивает debug-флаги.
- F78 (preprocessor case mismatch) документирует `/define:$Configuration` vs `DEBUG` — отдельная проблема.
- F182 (GamePath validation) — тоже отдельная проблема.

---

## Исправление

Разделить флаги по конфигурациям:

```powershell
$debugFlag = if ($Configuration -eq "Debug") { "/debug:full" } else { "/debug:pdbonly" }
$optimizeFlag = if ($Configuration -eq "Debug") { "/optimize-" } else { "/optimize+" }

csc /target:library `
    /out:$outPath `
    $debugFlag `
    $optimizeFlag `
    /define:$Configuration `
    ...
```

Или, если debug-символы не нужны в Release вообще:

```powershell
if ($Configuration -eq "Debug") {
    csc ... /debug:full /optimize- /define:DEBUG ...
} else {
    csc ... /debug:pdbonly /optimize+ /define:RELEASE ...
}
```

---

## Acceptance Criteria

- [ ] Debug-сборка: `/debug:full` + `/optimize-` + `DEBUG` символ определён.
- [ ] Release-сборка: `/debug:pdbonly` (или без debug флага) + `/optimize+` + `RELEASE` символ определён.
