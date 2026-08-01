# F78: build.ps1 — preprocessor case mismatch (`Debug` vs `DEBUG`)

**Приоритет:** P0 (Critical)
**Область:** Build · Configuration
**Файл:** `build.ps1:35`
**Дата обнаружения:** 2026-07-31

---

## Проблема

```powershell
/define:$Configuration   # → /define:Debug (когда $Configuration = "Debug")
```

Но в исходном коде все `#if` используют UPPERCASE:

```csharp
#if DEBUG
    Debug.Log("[RadioNav] Prefab hierarchy: ...");
#endif
```

C# preprocessor symbols **case-sensitive**. `/define:Debug` ≠ `DEBUG`.

---

## Влияние

Все `#if DEBUG` блоки **никогда не компилируются** — даже в Debug-сборке. Debug diagnostics, hierarchy dumps, и debug panels мертвы.

---

## Исправление

```powershell
# Вместо:
/define:$Configuration

# Нужно:
if ($Configuration -eq "Debug") {
    /define:DEBUG
} else {
    /define:RELEASE
}
```

---

## Acceptance Criteria

- [ ] В Debug-сборке `#if DEBUG` блоки компилируются
- [ ] В Release-сборке `#if DEBUG` блоки исключены
