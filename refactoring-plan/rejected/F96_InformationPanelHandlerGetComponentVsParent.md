# F96: InformationPanelHandler — GetComponent vs GetComponentInParent

**Приоритет:** P3 (Low)
**Область:** UI · InformationPanelHandler
**Файл:** `InformationPanelHandler.cs:160-161`
**Дата обнаружения:** 2026-07-31

---

## Проблема

```csharp
radioHandler = GetComponent<RadioHandler>();
sonneController = GetComponent<SonneController>();
```

Ищет только на том же GameObject. Если handler components на parent → вернёт null → сломанный panel.

---

## Исправление

```csharp
radioHandler = GetComponentInParent<RadioHandler>();
sonneController = GetComponentInParent<SonneController>();
```

---

## Acceptance Criteria

- [ ] Компоненты найдены на parent если отсутствуют на текущем GameObject
