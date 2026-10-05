# F53: MorseAlphabet — буква 'A' закодирована как `._` (dot-underscore) вместо `.-` (dot-dash)

**Приоритет:** P0 (Critical)  
**Область:** Logic · MorseAlphabet  
**Файл:** `Source/Logic/MorseAlphabet.cs:12`

---

## Проблема

```csharp
Define('A', "._");   // ← '_' (ASCII 95), не '-' (ASCII 45)
Define('B', "-..."); // ← '-' (dash) — корректно
```

В кодировке Морзе буква 'A' — `.-` (dot-dash). В коде записана `._` (dot-underscore). Underscore (`_`) не равен dash (`-`). 

`MorseKeyer.GetCurrentSoundTicks()` проверяет `mc[b] == '-'` — для 'A' это `false`, поэтому 'A' озвучивается как два Dot вместо Dot-Dash.

## Поражённые станции

Позывные, содержащие 'A':
- MWN (Bushmills) — W и N не содержат A, но MWN как строка не содержит...
  
Жди, давай точнее. MorseAlphabet хранит коды для символов. Если кто-то вызывает `GetMorse('A')` — получает `"._"`. 

Все станции, у которых в callsign есть буква 'A':
- **M**WN — нет A
- F**R**Q — нет A
- LEC — нет A  
- LG — нет A
- SL — нет A
- LEX — нет A
- LJS — нет A
- LMC — нет A

Callsign'ы 14 станций не содержат 'A'. Но:
1. `TryGetCallsignFromHistory()` при null callsign генерирует из Name.Substring(0,2) — там могут быть A
2. Любая будущая станция с 'A' в callsign будет сломана
3. `MorseAlphabet.DecodeMorse()` — обратное декодирование `"._"` не вернёт 'A', потому что в reverse словаре ключ `"._"` (underscore), но при сравнении с декодированной строкой из morseDecoderBuffer (которая содержит `-` для dash) не совпадёт

**Это база данных алфавита — любой вызов GetMorse('A') возвращает неверный результат.**

## Воспроизведение

```csharp
string morse = MorseAlphabet.GetMorse('A');
// Ожидается: ".-"  (dot-dash)
// Реальность: "._"  (dot-underscore)
```

## Исправление

```csharp
Define('A', ".-");
```

Одна строка. 15 секунд.

## Риски

- Изменение затрагивает только символ 'A'
- `DecodeMorse` для `".-"` должен быть в `reverse` — проверь, что после фикса ключ `".-"` не конфликтует
- MorseAlphabet.TextToPattern() для текста с 'A' также будет исправлен
