# P1.M1_GodObject_RadioNavigationMain_EntryPoint (196 LOC)

**Приоритет:** P1-Major  
**Область:** Logic — mod entry point & lifecycle management  
**Статус:** Новый документ, не задокументировано  

---

## Описание проблемы

`Main.cs` из 196 строк объединяет **~7 независимых ответственностей**: Harmony patching system, Unity scene lifecycle events, DI injection setup, AssetBundle loading/validation, static singleton management и debug logging — всё в одном классе который должен быть <50 LOC для лучшей модульности.

### Table of Responsibilities (196 total lines)

| # | Ответственность | Строки | Детали |
|---|---------------|--------|--------|
| 1 | Harmony patching orchestration | 24-84 (~60 LOC) | Find methods via Reflection, apply patches, rollback on failure — ALL в одном методе OnLoaded()  
| 2 | AssetBundle loading & prefab instantiation | 157-183 (~26 LOC) | Load bundle, instantiate 'Radio', validate structural hierarchy  
| 3 | Scene lifecycle event binding to Unity events | 86-89,108-126, 128-194 Three separate scene handlers (OnSceneStart, OnSceneAwake, OnSceneLoad) each with different logic
| 4 | Static instance management & validation | 151-154 — reusing existing RadioHandler.Instance or creating new 
| 5 | DI InjectionFramework bootstrap | 108-126 (OnSceneAwake handler injects entire assembly including ALL MonoBehaviours, Harmony patches, static classes  
| 6 | Runtime Debug diagnostics logging | ~40 LOC across multiple logs — version info, patch counts, scene names, validation results, diag outputs like line 181-184
| 7 | Prefab structure rigid validation (hardcode paths) | строка 165 — hardcoded required children array match against prefab hierarchy at runtime  
## Почему это проблема

Каждый из these seven responsibilities:

1. **Has independent failure modes:** patching fails (game update changes internals), AssetBundle missing, DI injection broken, scene validation fail — каждый отдельный failure point обрабатывается via separate try/catch but всё в одном методе/файле делает debugging и maintenance сложным для любого dev who wants to fix ONE concern but ends up navigating ~196 lines of mixed-purpose code.

2. **Requires full recompilation для любого обновления:** если нужно изменить patching logic или добавить новый prefab validation path, приходится пересобирать весь модуль даже когда changes are entirely localized к отдельной sub-component.

3. **Debug logging mixed with production logic** — строка 26 version log, строка 86 OnSceneStart binding code, debug Hierarchy dump при __DEBUG (#if DEBUG) build mode (строки 99-103, line186), всё это production functionality.

4. **Hardcoded prefab validation at строке 165 (`"RequiredChildren = {"CursorSpinner", "Toggle", ...}`)) — если UBOAT update меняет GUI hierarchy или добавляет новые elements в layout, mod сломается silently or with confusing error message because hardcod path не может be dynamically adjusted per scene version.

## Кodo Examples (Problem Areas)

```csharp
// OnLoaded(): 60 LOC patching orchestration — find methods, apply patches, rollback on any failure
var continueStory = typeof(RadioStory).GetMethod("ContinueStory", ...);
var retrieveString1 = typeof(Locale).GetMethod("RetrieveString", ...);
...targets = new[] { // ~5 different patch targets, each requiring FindMethod + CreateHarmonyMethod call 
    new { method=continueStory, postfix=new HarmonyMethod(...)},  
};

// SceneEventsListener_0nSceneStart(): 67 LOC mixed scene validation, DI setup, asset loading, prefab hierarchy check
if (resourceManager.IsNull()) return; // early exit on failed injection  
GameObject radio = resourceManager.InstantiatePrefab("Radio");      
string[] requiredChildren = { "CursorSpinner", "Toggle"... };

// ~10+ Debug.Log calls throughout OnLoaded + both Scene event handlers — production and debug mixed.
  if (scene.name != “Main Scene”) Debug.LogError("[RadioNav][Main] ...”); 
```

## Риски

| Риск | Вероятность | Impact  
|------|-----------|--------|-----
Any change requires recompile full mod - slow iteration cycle при development & testing | Средний-Высокий | Slower iteration, harder to isolate bugs | Patching logic errors silently fail because of try/catch swallow within OnLoaded (строки 69-83) — user only sees generic 'Pre-flight failed' message instead of specific target method was not found
Hardcoded prefab path validation fails on UBOAT updates with confusing debug error messages | Высокий | Mod не работает при любом minor GUI change to Unity scene hierarchy  | 40+ LOC debug logging mixed w/ production business logic makes code harder read & maintain 
## Рекомендованное решение — Extract into Separate Concern Classes:

Create **ModBootstrap.cs** extract DI injection и AssetBundle loading logic (OnSceneStart handler).  
Create **PatchManager.cs** move Harmony patching orchestration + Pre-flight validation к separate file. This allows independent testing of patch application before scene loading starts.
Extract scene-level validation — Move hardcoded required children check к data-driven configuration или runtime-scena-aware path resolver.
Split OnSceneAwake vs OnSceneStart handlers к dedicated SceneManager subclass.  

## Файлы для изменения

| Файл | Текущие LOC → Target | Действие  
|-----------|------|------------- 
| Main.cs | 196 LOC → ~40 (thin delegation wrapper) | Extract PatchManager, Bootstrap classes  
PatchManager.cs — NEW FILE: | 0 → ~80 | Harmony orchestration Only + Pre-flight diagnostics 
ModBootstrap.cs — NEW FILE: | 0 → ~120 | DI initialization + AssetBundle lifecycle management + prefab hierarchy validation  

---
*Создано: 2026-07-30. Связан с REFACTORING_PLAN.md — этап "EntryPoint Decomposition & Lifecycle Separation".*
