# F185: `InformationPanelHandler` hardcodes 6 pattern labels for 14 available stations

**Приоритет:** P2 (Medium)  
**Область:** UI · InformationPanelHandler · Data Representation  
**Файл:** `Source/UI/InformationPanelHandler.cs:68`, `Source/UI/InformationPanelHandler.cs:325`  
**Дата обнаружения:** 2026-07-31  

---

## Проблема

В `InformationPanelHandler.cs`:

```csharp
patternTexts = new Text[6];
for (int i = 0; i < patternTexts.Length; i++)
    patternTexts[i] = CreateLabel(parent, "Pattern" + i, "");
```

В `BeaconData.Stations` задано **14 станций**. Если при определенном положении подлодки и высокой чувствительности радиоприемника слышны более 6 радиомаяков, станциям с индексами > 5 не хватает текстов отображения — их сигналы и статус просто не выводятся на панель отладки/диагностики.

---

## Влияние

Потеря информации о статусе приема приема станций 7–14 на информационном экране.

---

## Исправление

Размер массива `patternTexts` должен соответствовать `BeaconData.Stations.Length` или динамически создаваться из списка зарегистрированных станций:

```csharp
patternTexts = new Text[BeaconData.Stations.Length];
```

---

## Acceptance Criteria

- [ ] Количество меток `patternTexts` равно числу станций в базе данных (`BeaconData.Stations.Length`).
- [ ] Все слышимые станции отображаются на информационной панели.
