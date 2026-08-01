# F57: RadioInstaller.Instantiate() — Debug.Log без `#if DEBUG` в Release

**Приоритет:** P1 (High)  
**Область:** Build · Debug Logging  
**Файл:** `Source/UI/RadioInstaller.cs:40-49`

---

## Проблема

Блок из 10 Debug.Log вызовов в `RadioInstaller.Instantiate()` не обёрнут в `#if DEBUG`:

```csharp
Debug.Log("[RadioNav][Main] Ground exists=" + (ground.IsNotNull())
    + " Background exists=" + (groundBg.IsNotNull())
    + " Checkmark exists=" + (groundCm.IsNotNull())
    + " [Ground] pos=" + (ground.IsNotNull() ? ground.localPosition.ToString() : "null")
    + " [Background] pos=" + (groundBg.IsNotNull() ? groundBg.localPosition.ToString() : "null")
    + " img=" + (groundBg.IsNotNull() && groundBg.GetComponent<Image>().IsNotNull())
    + " [Checkmark] pos=" + (groundCm.IsNotNull() ? groundCm.localPosition.ToString() : "null")
    + " sprite=" + (cmImg.IsNotNull() && cmImg.sprite.IsNotNull() ? cmImg.sprite.name : "no-sprite")
    + " imgEnabled=" + (cmImg.IsNotNull() ? cmImg.enabled.ToString() : "no-img")
    + " imgAlpha=" + (cmImg.IsNotNull() ? cmImg.color.a.ToString("F2") : "-"));
```

Каждый вызов Debug.Log в Release:
- Аллоцирует строку (string concat с ~20+ вызовов ToString)
- Пишет в Unity console (блокирующий I/O)
- Происходит при каждом instantiate префаба (перезагрузка сцены, релоад мода)

F31 насчитал 81 Debug.Log в Release, но этот блок размером ~10 строк не учтён.

## Исправление

Обернуть в `#if DEBUG`. 2 минуты.

## Дополнительно

В том же файле строки 40-49 содержат потенциальный NRE при обращении `cmImg.sprite`, если GroundCheckmark отсутствует в префабе. После `#if DEBUG` это перестанет быть проблемой в Release.
