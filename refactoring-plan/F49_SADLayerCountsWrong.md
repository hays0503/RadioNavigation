# F49: SAD layer file counts wrong

**Severity:** P2 (Medium)

**Тип:** Документация — неверная статистика

**Файлы:** `SAD/03_Current_Architecture.md`

---

## Описание

SAD `03_Current_Architecture.md` содержит неверные подсчёты файлов по слоям и в целом.

### SAD утверждает:

| Слой | SAD count | Реальность |
|------|-----------|------------|
| UI | 12 | **15** |
| Logic | 14 | **12** |
| Data | 8 | **5** |
| Sound | 6 | **2** |
| **Total** | **40** | **34** |

### Почему разница

- **UI (12→15):** SAD пропускает: `GroundHandler.cs`, `DebugAngleHandler.cs`, `ExpanderHandler.cs` (возможно, `PrefabNames.cs` отнесён к Data)
- **Logic (14→12):** SAD включает `GameStrings.cs` (не существует), `FormatDiagnosticString` (метод, не файл)
- **Data (8→5):** SAD включает `GameStrings.cs` и `PrefabNames.cs` (PrefabNames на самом деле в Source/UI/), возможно, считает `StationState.cs` как 2 файла
- **Sound (6→2):** SAD считает `CarrierGenerator`, `Atmosphere`, `ReceiverDsp` как отдельные файлы — в реальности это 3 класса **в одном файле** `DspPipeline.cs`. Также `AudioSynthesisEngine.cs` и `DspPipeline.cs` — это всё Sound layer.

---

## Влияние

1. Неверное понимание размера слоёв
2. Sound layer кажется в 3× больше, чем на самом деле
3. Data layer кажется на 60% больше
4. Итоговая метрика «40 файлов» — на 6 файлов больше реальности

---

## Рекомендация

Исправить все file counts в SAD/03. Отдельно отметить, что Sound layer содержит 3 класса в 1 файле (co-located by design). Указать, что Data — это только 5 конфигурационных файлов.
