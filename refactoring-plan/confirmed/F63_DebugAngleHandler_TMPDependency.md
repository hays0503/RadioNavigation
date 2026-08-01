# F63: DebugAngleHandler — зависит от TextMeshPro без проверки наличия сборки

**Приоритет:** P2 (Medium)  
**Область:** UI · DebugAngleHandler · Fragile Dependency  
**Файл:** `Source/UI/DebugAngleHandler.cs`

---

## Проблема

```csharp
using TMPro;  // ← жёсткая зависимость от TextMeshPro

public class DebugAngleHandler : MonoBehaviour
{
    [SerializeField] private TMP_Text debugText;  // ← поле
```

Если TMPro не загружен (другой билд Unity, stripped build, UBOAT обновление):
1. Компиляция: VS ругается на отсутствие TMPro.dll
2. Runtime: десериализация префаба падает при поиске MissingReference
3. Хендлер молча деградирует (уже не в Debug билде)

## Контекст

`DebugAngleHandler` отображает отладочную информацию. В Release (#if !DEBUG) он не должен загружаться вовсе, но:
- Префаб может содержать компонент (Unity попытается десериализовать)
- Если сборка TMPro отсутствует, весь префаб не соберётся

## Исправление

1. Заменить `TMP_Text` на `Text` (стандартный uGUI) — debug info не требует TMPro
2. Или обернуть весь класс в `#if !DEBUG` + `#endif`
3. Или использовать условную компиляцию `#if TEXTMESH_PRO`

15 минут.
