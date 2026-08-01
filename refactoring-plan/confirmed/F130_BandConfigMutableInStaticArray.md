# F130. BandConfig — mutable class в static readonly массиве

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Data · DialConfig · Encapsulation |
| **Файл** | `Source/Data/DialConfig.cs` |
| **Строки** | 6-18 |

---

## Описание

`BandConfig` — **class** (reference type) с публичными мутабельными полями:

```csharp
// DialConfig.cs:6-18
public class BandConfig
{
    public string Name;
    public float RangeMin;
    public float RangeMax;
    public float MinAngle;
    public float MaxAngle;
    public float UnitFactor;
}
```

Хранится в `static readonly` массиве:

```csharp
// DialConfig.cs
public static readonly BandConfig[] Bands = new BandConfig[]
{
    new BandConfig { Name = "I",   RangeMin = 15f,   RangeMax = 60f,   ... },
    new BandConfig { Name = "II",  RangeMin = 60f,   RangeMax = 120f,  ... },
    // ...
};
```

`static readonly` защищает **ссылку** на массив, но **не элементы**. Любой код может сделать:

```csharp
DialConfig.Bands[0].RangeMin = -999f; // мутация — global state corruption
```

Это нарушает инкапсуляцию и создаёт risk случайной мутации конфигурации.

## Корневая причина

`BandConfig` — class вместо struct. Все поля публичные и mutable. Нет защиты от мутации.

## Предлагаемое исправление

**Вариант 1:** Сделать `BandConfig` struct:

```csharp
public struct BandConfig
{
    public string Name { get; }
    public float RangeMin { get; }
    public float RangeMax { get; }
    public float MinAngle { get; }
    public float MaxAngle { get; }
    public float UnitFactor { get; }

    public BandConfig(string name, float rangeMin, float rangeMax,
                      float minAngle, float maxAngle, float unitFactor)
    {
        Name = name;
        RangeMin = rangeMin;
        RangeMax = rangeMax;
        MinAngle = minAngle;
        MaxAngle = maxAngle;
        UnitFactor = unitFactor;
    }
}
```

**Вариант 2:** Оставить class, но сделать поля init-only (C# 9+):

```csharp
public class BandConfig
{
    public string Name { get; init; }
    public float RangeMin { get; init; }
    // ...
}
```

**Вариант 3 (минимальный):** Заменить на `readonly struct` с конструктором.

## Зависимости

- Связан с F20 (DialConfig band inconsistencies) — тот же файл
- Связан с F05 (Static Mutable State) — паттерн глобальной мутабельной конфигурации
