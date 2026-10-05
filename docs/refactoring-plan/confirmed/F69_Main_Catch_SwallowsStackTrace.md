# F69: Main.cs — catch блок логгирует только e.Message, теряя stack trace

**Приоритет:** P1 (High)  
**Область:** Logic · Main  
**Файл:** `Source/Logic/Main.cs:192`

---

## Проблема

В `OnSceneAwake()` (или соседнем методе) catch блок выводит только сообщение, а не полное исключение:

```csharp
catch (Exception e)
{
    Debug.LogError("[RadioNav][Main] Error: " + e.Message);  // ← теряет stack trace
}
```

`Debug.LogError(string)` — обычная строка без кликабельного stack trace. Нужно использовать `Debug.LogException(Exception)`:

```csharp
catch (Exception e)
{
    Debug.LogException(e);  // ← сохраняет полный стек, кликабельно в Unity Console
}
```

## Сравнение

Рядом есть правильный паттерн (строка 123-124):

```csharp
catch (Exception e)
{
    Debug.LogException(e);   // ← правильно
}
```

## Влияние

- При ошибке в `OnSceneAwake` разработчик не видит, где именно упало
- Клик по сообщению не открывает исходный код
- Затрудняет отладку ошибок мода в полевых условиях (у пользователей)

## Исправление

```csharp
Debug.LogException(e);
```

или, если нужно сохранить префикс:

```csharp
Debug.LogError("[RadioNav][Main] Error: " + e);
// ToString() включает stack trace
```

## Риски

- Нет. Изменение логгирования не влияет на логику.
