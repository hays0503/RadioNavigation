# F36: GetTransitionCount считает только Dots

**Приоритет:** P2 (Medium)
**Область:** Logic · SonneSignalGenerator
**Тип:** Logic · Naming

---

## Проблема

Метод `GetTransitionCount` (SonneSignalGenerator.cs:118-130) по названию должен подсчитывать количество переходов (transitions) в Consol-паттерне, но фактически считает только `NavigationElementType.Dot`, игнорируя Dash.

---

## Evidence

SonneSignalGenerator.cs:118-130:
```csharp
public static int GetTransitionCount(NavigationElementType[] pattern)
{
    if (pattern.IsNull())
        return 0;

    int count = 0;
    for (int i = 0; i < pattern.Length; i++)
    {
        if (pattern[i] == NavigationElementType.Dot)   // ← только Dot!
            count++;
    }
    return count;
}
```

---

## Impact

- Если метод где-либо используется (или будет использован) для вычисления позиции внутри сектора, результат будет неверным — количество переходов должно включать и Dash
- Вводит в заблуждение разработчика

## Fix

Переименовать в `GetDotCount` или исправить логику на `!= NavigationElementType.Equisignal`.
