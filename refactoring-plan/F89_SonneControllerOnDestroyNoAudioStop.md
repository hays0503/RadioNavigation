# F89: SonneController.OnDestroy — не останавливает AudioSource

**Приоритет:** P3 (Low)
**Область:** Logic · SonneController · Cleanup
**Файл:** `SonneController.cs:112-119`
**Дата обнаружения:** 2026-07-31

---

## Проблема

`audio.Play()` вызывается в `Awake()` (line 97), но `OnDestroy()` не вызывает `audio.Stop()`. Между `OnDestroy` и финальным destroy AudioSource, `OnAudioFilterRead` может быть вызван на **partially-destroyed object**.

---

## Исправление

```csharp
void OnDestroy() {
    audio.Stop();
    // ... существующий cleanup
}
```

---

## Acceptance Criteria

- [ ] AudioSource останавливается в OnDestroy
