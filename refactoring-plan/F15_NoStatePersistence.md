# F15: Отсутствует сохранение состояния мода

**Приоритет:** P2 (Medium)
**Область:** Logic · Main / UI
**Тип:** Quality of life

---

## Проблема

Мод не сохраняет никакого состояния между сессиями. Каждый запуск игры — factory reset: громкость, фидбек, позиция панели, выбранная зона — всё сбрасывается на дефолт.

- Нет `ISaveable` реализации
- Нет `PlayerPrefs` использования
- Нет вызова `SaveManager`
- Нет сериализации состояния

---

## Evidence

### Полный поиск дал ноль совпадений

```bash
rg "PlayerPrefs|ISaveable|SaveManager|OnSave|OnLoad|BinaryFormatter|Json|Xml" Source/ --no-filename | wc -l
# => 0
```

### Единственный lifecycle-метод — заглушка

Main.cs:24:

```csharp
public void OnLoaded()  // мод загружен — только инициализация, не restore
```

`OnLoaded()` является частью `IUserMod` интерфейса UBOAT mod API. Он вызывается при загрузке мода, но не содержит логики восстановления состояния.

### Что теряется при рестарте

| Параметр | Место | Default |
|----------|-------|---------|
| Reiver Gain (Volume) | VolumeSpinner → SonneController | 0° (тишина) |
| Feedback | FeedbackSpinner → AudioSynthesisEngine | 0° |
| Позиция панели на экране | PanelDragHandler | anchoredPosition по умолчанию |
| Выбранная зона (Zone index) | ZoneHandler | Zone1 |
| Expand/collapse состояние | ToggleHandler/Expander | свёрнуто |

---

## Предлагаемое решение

1. **Минимальное:** сохранять Volume и Feedback через `PlayerPrefs.SetFloat` в OnDestroy, читать в Start.
2. **Полное:** реализовать `ISaveable` для сохранения полного состояния мода в сейв-файл игры.
3. UI-позицию сохранять через `PlayerPrefs.SetString` как сериализованный `Vector2`.

---

## Acceptance Criteria

- [ ] Volume и Feedback восстанавливаются после перезапуска
- [ ] Выбранная зона восстанавливается
- [ ] Позиция панели восстанавливается
- [ ] Состояние expand/collapse восстанавливается
