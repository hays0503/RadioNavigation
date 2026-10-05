# F48: SAD station array sizing wrong (4 vs 14)

**Severity:** P1 (High)

**Тип:** Документация — неверная конфигурация

**Файлы:** `SAD/04_Component_Architecture.md`, `SAD/Appendix_B_Class_Diagrams.md`

---

## Описание

SAD последовательно утверждает, что массивы StationScheduler, MorseKeyer, CarrierGenerator имеют размер **4** (соответствующий 4 станциям). Реальность: `BeaconData.cs:47-48` определяет **14 станций**.

### SAD пишет (04_Component_Architecture.md):
```
StationScheduler[] (4 stations)
MorseKeyer[] (4 keyers)
CarrierGenerator[] (4 carriers)
```

### Реальность:
```csharp
// BeaconData.cs:47-48
public static BeaconStation[] Stations = new BeaconStation[]
{
    // ... 14 станций
};

// SonneController.cs
private StationScheduler[] schedulers;  // размер = Stations.Length = 14
private MorseKeyer[] keyers;            // размер = Stations.Length = 14
private CarrierGenerator[] carriers;    // размер = Stations.Length = 14
```

### Последствия

| Аспект | SAD утверждение | Факт |
|--------|----------------|------|
| Количество станций | 4 | 14 |
| Размер schedulers[] | 4 | 14 |
| Размер keyers[] | 4 | 14 |
| Размер carriers[] | 4 | 14 |
| Влияние на циклы | Неправильное понимание | Настоящие циклы итерируют 14 элементов |

---

## Влияние

1. Разработчик, планирующий архитектуру под «4 станции», создаст неверную аллокацию
2. Memory и performance оценка в SAD неверна (14× структур, а не 4×)
3. Appendix B class diagram рисует неправильные multiplicity

---

## Рекомендация

Исправить все упоминания размера массивов с 4 на 14 в SAD документах. Указать, что размер определяется из `BeaconData.Stations.Length`, а не хардкодится.
