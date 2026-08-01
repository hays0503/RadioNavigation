# F129. MorseAlphabet.GetElementCount() не учитывает границы слов

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Logic · MorseAlphabet · Morse Standard |
| **Файл** | `Source/Logic/MorseAlphabet.cs` |
| **Строки** | 75-84 |

---

## Описание

`GetElementCount()` добавляет 3 элемента-тишины между каждым символом:

```csharp
// MorseAlphabet.cs:75-84
public static int GetElementCount(string text)
{
    int total = 0;
    for (int i = 0; i < text.Length; i++)
    {
        if (i.IsPositive()) total += 3; // 3 элемента тишины между символами
        total += GetMorse(text[i]).Length;
    }
    return total;
}
```

По стандарту Морзе:
- **Между элементами одного символа:** 1 unit тишины (учитывается в MorseTiming через ElementPeriodMs - SoundMs)
- **Между символами:** 3 units тишини (здесь: 3 элемента)
- **Между словами (пробел):** 7 units тишини

Текущий код treating пробел как обычный символ с 3-element паузой вместо 7-element. Если callsign содержит пробел или другой разделитель слов, пауза между словами будет **неправильной** (3 вместо 7).

Дополнительно: `TextToPattern()` (строки 93-104) имеет ту же проблему — добавляет 3 false-элемента перед каждым символом, включая пробел.

## Корневая причина

Нет различия между inter-character pause (3) и inter-word pause (7).

## Предлагаемое исправление

```csharp
public static int GetElementCount(string text)
{
    int total = 0;
    for (int i = 0; i < text.Length; i++)
    {
        if (i.IsPositive())
        {
            if (text[i - 1] == ' ')
                total += 7; // inter-word pause
            else
                total += 3; // inter-character pause
        }
        if (text[i] != ' ')
            total += GetMorse(text[i]).Length;
    }
    return total;
}
```

Аналогично в `TextToPattern()`.

## Зависимости

- Связан с F53 (MorseAlphabet 'A' = `._`) — тот же файл
- Независим от других F-проблем
