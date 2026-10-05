# F37: 6 станций с null callsign

**Приоритет:** P2 (Medium)
**Область:** Data · BeaconData
**Тип:** Data Quality

---

## Проблема

6 из 14 станций имеют `Callsign = null` в истории (`BeaconHistoryEntry.Callsign`). При генерации Consol-паттерна `GenerateConsolPattern` (SonneSignalGenerator.cs:88) проверяет `string.IsNullOrEmpty(callsign)` — если null, callsign-секция полностью пропускается.

Это меняет ритм и структуру цикла: станция сразу переходит к навигационным элементам без опознавательного сигнала.

---

## Evidence

BeaconData.cs — станции с null callsign:
- Beauvais (строка 251): `Callsign = null`
- Bayeux (строка 274): `Callsign = null`  
- Petten (строка 297): `Callsign = null`
- Plougourvest (строка 319): `Callsign = null`
- Brieg (строка 343): `Callsign = null`
- Husum (строка 367): `Callsign = null`

SonneSignalGenerator.cs:88-101:
```csharp
if (!string.IsNullOrEmpty(callsign))   // ← null → секция пропущена
{
    foreach (char c in callsign.ToUpperInvariant()) { /* Morse */ }
}
```

---

## Impact

- Игрок не слышит позывной — сложнее идентифицировать станцию
- Длительность цикла сокращена (отсутствует CallsignDurationMs)
- Реальные Consol-станции всегда передавали позывной
