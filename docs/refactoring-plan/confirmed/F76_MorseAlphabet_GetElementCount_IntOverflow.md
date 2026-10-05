# F76: MorseAlphabet.GetElementCount() — теоретическое int overflow

**Приоритет:** P3 (Low)  
**Область:** Logic · MorseAlphabet  
**Файл:** `Source/Logic/MorseAlphabet.cs:76-84`

---

## Проблема

`GetElementCount()` использует `int` для аккумулятора:

```csharp
public static int GetElementCount(string text)
{
    int total = 0;
    for (int i = 0; i < text.Length; i++)
    {
        string morse = GetMorse(text[i]);
        if (morse.IsNotNull())
            total += morse.Length;      // ← int overflow при длинных строках
    }
    return total;
}
```

При строках длиной > ~2.14 млрд символов (или при сумме элементов > `int.MaxValue`) произойдёт тихое переполнение. В C# по умолчанию `unchecked` — исключения не будет, результат станет отрицательным.

## В现实ности

Метод вызывается только с callsign строками (2-5 символов). Переполнение **невозможно** в текущем коде. Это defence-in-depth finding.

## Исправление

```csharp
public static int GetElementCount(string text)
{
    int total = 0;
    for (int i = 0; i < text.Length; i++)
    {
        string morse = GetMorse(text[i]);
        if (morse.IsNotNull())
            total += morse.Length;
        if (total < 0)                     // ← защита от overflow
            return int.MaxValue;
    }
    return total;
}
```

Или использовать `long`:

```csharp
long total = 0;
// ...
return (int)Math.Min(total, int.MaxValue);
```

## Риски

- Косметика. В текущем коде не воспроизводится.
