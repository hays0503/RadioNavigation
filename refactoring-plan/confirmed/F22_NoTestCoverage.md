# F22: Нет тестов (0% Coverage)

**Приоритет:** P2 (Medium)
**Область:** Project-wide
**Тип:** Quality · Process

---

## Проблема

В проекте нет ни одного теста. 0% code coverage. Любое изменение — регрессия вслепую.

- Нет test project
- Нет test runner configuration
- Нет unit tests
- Нет integration tests
- Нет manual test plan в репозитории

---

## Evidence

```bash
# В проекте нет:
# - test/ или tests/ директории
# - xUnit/NUnit/MSTest ссылок в csproj
# - TestRunner настроек
# - Тестовых классов
```

SAD 11_Testing_Strategy.md (488 строк) содержит полный план:

| Слой | Файлы | Target coverage | Приоритет |
|------|-------|-----------------|-----------|
| Data | BeaconData, DialConfig | >80% | P0 |
| Logic | PositionFixCalculator, StationScheduler, MorseAlphabet | >70% | P0 |
| Sound | DspPipeline | >60% | P1 |
| UI | InformationPanelHandler, SonneController | >50% | P2 |

### Самая критичная untested логика

| Класс | Строк | Почему нужно тестировать |
|-------|-------|-------------------------|
| `PositionFixCalculator.FindIntersection()` | 65 | Сложная геодезия, один баг уже найден (F16) |
| `StationScheduler.Tick()` | 25 | State machine с 5 фазами |
| `MorseKeyer.Advance()` | ~60 | Комбинаторная логика dots/dashes |
| `SonneSignalGenerator.CalculateSector()` | 22 | Core алгоритм позиционирования |
| `AudioSynthesisEngine.ProcessAudio()` | 80 | Audio thread, без тестов нельзя рефакторить |

---

## Предлагаемое решение

1. Создать `tests/` директорию с xUnit проектом
2. Написать тесты на Data-слой (BeaconData, DialConfig) — быстрый выигрыш
3. Написать тесты на статическую логику (PositionFixCalculator, StationScheduler)
4. Добавить CI (GitHub Actions) для автоматического прогона тестов

---

## Acceptance Criteria

- [ ] Data-слой: >80% coverage
- [ ] Logic (PositionFixCalculator): >70% coverage
- [ ] Logic (StationScheduler, MorseKeyer): >70% coverage
- [ ] CI прогоняет тесты на каждый push
- [ ] Тесты проходят на чистой машине (без UBOAT)
