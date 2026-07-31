# F43: AudioSource не добавлен, если SonneController уже существует на префабе

**Приоритет:** P1 (High)
**Область:** UI · RadioInstaller
**Тип:** Logic · Silent Failure

---

## Проблема

В `RadioInstaller.Instantiate` (RadioInstaller.cs:69-73) `EnsureComponent<AudioSource>` вызывается только внутри `if (radio.GetComponent<SonneController>().IsNull())`. Если префаб **уже содержит** `SonneController` (например, при повторной инициализации), но **не содержит** `AudioSource`, то `AudioSource` не будет добавлен.

При этом `SonneController.Awake()` (SonneController.cs:94-97) обращается к `GetComponent<AudioSource>()` — если null, будет NRE.

---

## Evidence

RadioInstaller.cs:69-73:
```csharp
if (radio.GetComponent<SonneController>().IsNull())
{
    EnsureComponent<AudioSource>(radio);   // ← только здесь!
    radio.AddComponent<SonneController>();
}
// если SonneController уже есть — AudioSource НЕ добавлен
```

SonneController.cs:8:
```csharp
[RequireComponent(typeof(AudioSource))]   // Unity добавит только при AddComponent, не на already-existing
```

SonneController.cs:94-97:
```csharp
var audio = GetComponent<AudioSource>();
audio.clip = silentClip;    // ← NRE если AudioSource = null
```

---

## Impact

- NRE в `Awake()` если префаб имеет SonneController без AudioSource
- Аудио полностью отсутствует при повторной инициализации
- `[RequireComponent]` не срабатывает на уже существующих компонентах

## Fix

Всегда вызывать `EnsureComponent<AudioSource>(radio)` вне зависимости от наличия SonneController.
