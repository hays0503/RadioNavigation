# Рецепт: три способа сохранить состояние мода между сейвами UBOAT

Проверено по исходникам игры (2026-08-24): `UBOAT.Game.Core.Serialization\SavesManager.cs`,
`ClassDescriptor.cs`, `InjectionFramework.cs`, `SceneObject.cs`, `BackgroundTasksManager.cs`.

## Способ 1 — события SavesManager + собственное хранилище

Статические поля мода игра **не** сериализует (`ClassDescriptor.IsFieldSerializable`
отбрасывает `IsStatic`). Храните данные сами (PlayerPrefs / static store), восстанавливайте
в очереди `Loaded`.

```csharp
var sm = InjectionFramework.Instance.GetInstance<SavesManager>();
sm.LoadingInitialization += () => { /* сброс статики до выгрузки сцены */ };
sm.SavingStarted += () => SaveMyStore();          // перед сериализацией
sm.Loaded += queue => queue.Enqueue(() => {       // действие на главном потоке
    RestoreFromStore();                            // 1 экшен = 1 кадр, каждый в try/catch
});
```

Порядок загрузки (`LoadCoroutine`): `LoadingInitialization` → уничтожение старых
SceneObject'ов → `LoadingStarted` → десериализация в рабочем потоке → `Loaded(Queue<Action>)`,
экшены выполняются по одному за кадр на главном потоке.

## Способ 2 — компонент под иерархией SceneObject

`SaveData.sceneObjects = SceneObject.sceneObjects`; сериализатор обходит GameObject'ы
и пишет **все** компоненты (`gameObject.GetComponents`) кроме блэклиста
(`unserializedTypes`/`unserializedNamespaces` в SavesManager).

Правила полей (`ClassDescriptor.CollectFields`, `IsFieldSerializable`):
- пишутся public **и** private instance-поля;
- пропускаются: `const` (`IsLiteral`), `[NonSerializedInGameState]`, `static`;
- `[NonSerialized]` пропускается, но `[NonSerialized]+[GameStateField]` — пишется.

```csharp
public class MyPanel : MonoBehaviour // повесить на объект внутри сцены кампании
{
    private float lastBearing;      // сохранится автоматически
    [NonSerializedInGameState]
    private Texture icon;           // исключить из сейва
}
```

## Способ 3 — свой ScriptableObjectSingleton

При `InjectIntoAssembly(мод)` фреймворк находит неабстрактных наследников
`ScriptableObjectSingleton`, грузит их через `Resources.Load("Singletons/<ИмяТипа>")`
и регистрирует как injectable. При сейве `GetSingletons()` кладёт все синглтоны в
`SaveData.scriptableObjects` — ваши поля попадают в файл автоматически.

```csharp
public class RadioNavState : ScriptableObjectSingleton
{
    public float calibrationOffset;   // сериализуется
    public override void AwakeSingleton() { }
    public override void StartSingleton() { }
}
```

Требования: ассет SO обязан лежать в `Resources/Singletons/<ИмяТипа>`; класс без
атрибутов (класс с `[NonSerializedInGameState]`, как сам SavesManager, целиком
не сериализуется); инжектьте сборку сами (`InjectIntoAssembly`), авто-инжект по имени
сборки не сработает.

## Подводные камни

1. **Десериализация в рабочем потоке.** `LoadingThread` — отдельный `Thread`;
   поля синглтонов и компонентов присваиваются вне главного потока. Unity API там
   нельзя — только через `MainThreadBlock`. Финальное чтение своего состояния делайте
   в экшене из очереди `Loaded` (главный поток).
2. **Fake-null после смены сцены.** Ссылки на старые GameObject'ы становятся
   «мёртвыми»: `== null` истинно, но это destroyed-объект. Проверяйте только через
   `== null` / `(bool)obj`, никогда `ReferenceEquals(obj, null)`; ссылки обновляйте
   заново после загрузки.
3. **AddSaveBlockade при критичных операциях.** Во время длительной асинхронной работы
   ставьте `AddSaveBlockade(this, warning)` и обязательно снимайте
   `RemoveSaveBlockade(this)` — забытая блокировка навсегда запрещает сейвы.
   Дубликат по тому же объекту не добавляется.
4. **BackgroundTask'и** переживают сейв только если запущены событием `CampaignStart`
   (`runningSerializableTasks`); прочие задачи — в `runningUnserializableTasks` и гибнут.
