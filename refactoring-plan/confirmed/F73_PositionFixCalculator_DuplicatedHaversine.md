# F73: PositionFixCalculator — Haversine формула дублирована в 2 местах

**Приоритет:** P2 (Medium)  
**Область:** Logic · PositionFixCalculator · Duplication  
**Файл:** `Source/Logic/PositionFixCalculator.cs:149-152, 203-217`

---

## Проблема

Формула Haversine для расстояния по большой дуге реализована **дважды** в одном файле:

### 1. Inline в `FindIntersection()` (строки 149-152):

```csharp
double a = Math.Sin(dlat / 2) * Math.Sin(dlat / 2)
         + Math.Cos(lat1) * Math.Cos(lat2)
         * Math.Sin(dlon / 2) * Math.Sin(dlon / 2);
double dist12 = 2.0 * Math.Asin(Math.Sqrt(a));
```

### 2. Отдельный метод `GetDistanceKm()` (строки 203-217):

```csharp
public static double GetDistanceKm(GeoLocation a, GeoLocation b)
{
    double dlat = ToRadians(b.Latitude - a.Latitude);
    double dlon = ToRadians(b.Longitude - a.Longitude);
    double lat1 = ToRadians(a.Latitude);
    double lat2 = ToRadians(b.Latitude);
    double haversine = Math.Sin(dlat / 2) * Math.Sin(dlat / 2)
                     + Math.Cos(lat1) * Math.Cos(lat2)
                     * Math.Sin(dlon / 2) * Math.Sin(dlon / 2);
    return 2.0 * EARTH_RADIUS_KM * Math.Asin(Math.Sqrt(haversine));
}
```

Inline-версия не умножает на `EARTH_RADIUS_KM` (используется для angular distance внутри алгоритма). Это не баг (разное применение), но дублирование кода.

## Дополнительно

- `ToRadians()` тоже дублирован (F12 — уже задокументировано)
- `EARTH_RADIUS_KM` — магическая константа 6371

## Исправление

Вынести Haversine в приватный метод:

```csharp
private static double Haversine(double lat1, double lon1, double lat2, double lon2)
{
    double dlat = ToRadians(lat2 - lat1);
    double dlon = ToRadians(lon2 - lon1);
    double a = Math.Sin(dlat / 2) * Math.Sin(dlat / 2)
             + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
             * Math.Sin(dlon / 2) * Math.Sin(dlon / 2);
    return 2.0 * Math.Asin(Math.Sqrt(a));
}
```

Использовать и в `FindIntersection()` (без `EARTH_RADIUS_KM`), и в `GetDistanceKm()` (с умножением).

## Риски

- Изменение вычислений может повлиять на точность позиционирования
- Должно быть покрыто тестами (F22) перед изменением
