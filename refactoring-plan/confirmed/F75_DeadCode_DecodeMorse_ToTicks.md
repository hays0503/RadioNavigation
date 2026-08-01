# F75: Мёртвый код — DecodeMorse(), StationScheduler.ToTicks()

**Приоритет:** P2 (Medium)  
**Область:** Logic · Hygiene  
**Файлы:** `Source/Logic/MorseAlphabet.cs:68-73`, `Source/Logic/StationScheduler.cs:106-109`

---

## Проблема

Два метода мертвы или бесполезны:

### 1. `MorseAlphabet.DecodeMorse()` (строка 68-73)

```csharp
public static char DecodeMorse(string morse)
{
    return reverse.TryGetValue(morse, out char ch) ? ch : '?';
}
```

**Нигде не вызывается.** Ни один caller в проекте не использует этот метод. Callsign-декодирование в `SonneController.DecodeCallsignBuffer()` (строка 170-212) делает посимвольное сравнение напрямую.

### 2. `StationScheduler.ToTicks()` (строка 106-109)

```csharp
private static int ToTicks(float ms)
{
    return MorseTiming.ToTicks(ms);
}
```

Бесполезная обёртка: просто делегирует `MorseTiming.ToTicks` без трансформации. Caller-ы могли бы вызывать `MorseTiming.ToTicks` напрямую.

## Исправление

- `DecodeMorse()` — удалить (или сохранить для будущих тестов, если F22 запланирован)
- `ToTicks()` — заменить вызовы на `MorseTiming.ToTicks` и удалить метод

## Риски

- `DecodeMorse()` может понадобиться для тестов (F22)
- `ToTicks()` — удаление безопасно, private метод
