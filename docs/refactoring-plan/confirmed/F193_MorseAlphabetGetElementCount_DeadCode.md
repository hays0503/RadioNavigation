# F193: `MorseAlphabet.GetElementCount()` — мёртвый код

**Приоритет:** P2 (Medium)  
**Область:** Logic · MorseAlphabet · Dead Code  
**Файл:** `Source/Logic/MorseAlphabet.cs:75-84`  
**Дата обнаружения:** 2026-08-01  

---

## Проблема

Метод `GetElementCount()` определён, но **никогда не вызывается** ни в одном файле проекта:

```csharp
// MorseAlphabet.cs:75-84
public static int GetElementCount(string text)
{
    int total = 0;
    for (int i = 0; i < text.Length; i++)
    {
        if (i.IsPositive()) total += 3;
        total += GetMorse(text[i]).Length;
    }
    return total;
}
```

**Подтверждение отсутствия вызовов:** grep `GetElementCount` по всем `.cs` файлам возвращает только определение (MorseAlphabet.cs:75). Ни один caller не существует.

### Почему это проблема

F75 (INDEX: "Мёртвый код — DecodeMorse(), StationScheduler.ToTicks()") документирует два других метода как мёртвый код, но **пропускает** `GetElementCount()`. F76, F129, F170 документируют **баги внутри** этого метода (int overflow, отсутствие границ слов, выход за таблицу), но не замечают, что метод **никогда не вызывается** — поэтому все исправления в F76/F129/F170 бесполезны.

F125 даже **неверно утверждает**, что `GenerateConsolPattern()` в `SonneSignalGenerator.cs:~92` вызывает `GetElementCount(callsign)`. На самом деле `GenerateConsolPattern` (SonneSignalGenerator.cs:81-116) вызывает `MorseAlphabet.GetMorse()` напрямую, а подсчёт длины callsignа делается через **совсем другой метод** — `SonneSignalGenerator.GetCallsignElementCount()` (SonneSignalGenerator.cs:132), который не вызывает `GetElementCount`.

---

## Влияние

- Разработчики тратят время на исправление багов (F76, F129, F170) в коде, который никогда не выполняется.
- F125 содержит неверную информацию о call-графе, вводя в заблуждение.
- Метод остаётся в public API, создавая ложное впятие о возможностях библиотеки.

---

## Исправление

1. **Удалить** `GetElementCount()` (MorseAlphabet.cs:75-84).
2. **Исправить F125** — заменить утверждение о вызове `GetElementCount()` из `GenerateConsolPattern()` на корректное описание: `GenerateConsolPattern` использует `GetCallsignElementCount()` + прямые вызовы `GetMorse()`.
3. **Отменить или перенаправить** F76, F129, F170 — так как исправлять баги в мёртвом коде бессмысленно.

---

## Acceptance Criteria

- [ ] `GetElementCount()` удалён из `MorseAlphabet.cs`.
- [ ] grep `GetElementCount` по `.cs` файлам возвращает 0 результатов.
- [ ] F125 исправлен: больше нет ложного утверждения о call-графе.
- [ ] F76, F129, F170 пересмотрены или закрыты как избыточные.
