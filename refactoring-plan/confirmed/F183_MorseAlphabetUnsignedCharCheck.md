# F183: `MorseAlphabet.GetMorse` performs redundant `c < 0` check on unsigned `char`

**Приоритет:** P3 (Low)  
**Область:** Logic · MorseAlphabet · Code Hygiene  
**Файл:** `Source/Logic/MorseAlphabet.cs:63`  
**Дата обнаружения:** 2026-07-31  

---

## Проблема

```csharp
public static string GetMorse(char c)
{
    if (c < 0 || c >= table.Length)
        return "";
    return table[c] ?? "";
}
```

В C# тип `char` (`ushort`) является 16-битным беззнаковым целым числом ($0 \dots 65535$).
Условие `c < 0` всегда ложно (`false`) и вызывает предупреждение компилятора `CS0472` ("The result of the expression is always 'false'").

---

## Влияние

Лишний код, предупреждения при строгом уровне предупреждений компилятора (`/warnaserror`).

---

## Исправление

Удалить проверку `c < 0`:

```csharp
public static string GetMorse(char c)
{
    if (c >= table.Length)
        return "";
    return table[c] ?? "";
}
```

---

## Acceptance Criteria

- [ ] Отсутствует проверкa `c < 0`.
- [ ] Компиляция проходит без `CS0472`.
