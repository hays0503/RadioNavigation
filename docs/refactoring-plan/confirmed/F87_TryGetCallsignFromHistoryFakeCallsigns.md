# F87: TryGetCallsignFromHistory — fallback produces fake callsigns

**Приоритет:** P2 (Medium)
**Область:** Logic · SonneController
**Файл:** `SonneController.cs:477`
**Дата обнаружения:** 2026-07-31

---

## Проблема

```csharp
callsign = station.Name.Substring(0, Math.Min(2, station.Name.Length)).ToUpper();
```

6 из 14 станций (Beauvais, Bayeux, Petten, Plougourvest, Brieg, Husum) имеют `History[0].Callsign == null`. Fallback выдаёт "PL", "BR" и т.д. — **не настоящие callsigns** → `DecodeCallsignBuffer` не может декодировать.

---

## Исправление

Либо дополнить исторические данные реальными callsigns, либо пропускать callsign phase для станций без known callsign.

---

## Acceptance Criteria

- [ ] Все 14 станций имеют корректные callsigns в history
- [ ] Или: callsign phase пропускается если callsign неизвестен
