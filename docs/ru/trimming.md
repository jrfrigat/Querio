# Публикация с триммингом и чтение сохранённого запроса

Querio дружелюбен к триммингу, и одну вещь по этому поводу сделать придётся. Страница короткая,
потому что ответ - одно свойство или один атрибут. Но пропустить его значит получить отказ во время
выполнения уже опубликованного приложения, а это худшее место для такого открытия.

## В чём дело

`QuerySpec` - это контракт данных: потребители хранят сохранённые отчёты как JSON и читают их позже,
часто уже на новой сборке. Типы спецификации - позиционные записи, поэтому сериализатор сопоставляет
свойства JSON с **именами параметров конструктора**. В публикации с триммингом этому мешают две
разные вещи, и их легко перепутать:

1. **Триммер срезает имена параметров** у всего, на что, по его мнению, никто не смотрит через
   рефлексию. С этим Querio справляется сам: `src/Querio/ILLink.Descriptors.xml` едет внутри пакета и
   велит триммеру сохранить сборку целиком. Здесь от вас ничего не требуется.
2. **`PublishTrimmed` полностью отключает рефлексивный путь `System.Text.Json`** - ещё до того, как он
   посмотрит на какой-либо тип. Вот это уже ваш ответ, потому что это свойство вашего приложения, а
   не Querio.

Без ответа на второе десериализация падает:

```
InvalidOperationException: Reflection-based serialization has been disabled for this application.
```

## Ответ А: оставить рефлексию включённой

Одно свойство в файле проекта приложения:

```xml
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
  <JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>
</PropertyGroup>
```

После этого очевидный код работает без изменений:

```csharp
var options = new JsonSerializerOptions
{
    Converters = { new JsonStringEnumConverter() },
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

var spec = JsonSerializer.Deserialize<QuerySpec>(json, options);
```

Это меньшее из изменений, и начинать стоит с него. Оно съедает часть той экономии, ради которой
тримминг и затевался, и не работает под Native AOT.

## Ответ Б: генератор исходников

Описать модель на этапе компиляции. Рефлексия не нужна вовсе, предупреждений тримминга нет, работает
под Native AOT:

```csharp
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(QuerySpec))]
internal sealed partial class QueryJson : JsonSerializerContext;
```

```csharp
var options = new JsonSerializerOptions(QueryJson.Default.Options)
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    TypeInfoResolver = QueryJson.Default,
};

var spec = JsonSerializer.Deserialize<QuerySpec>(json, options);
```

Объявить достаточно один `QuerySpec` - остальную модель генератор обойдёт сам.

## Как это остаётся правдой

`tests/Querio.TrimmingProbe` - консольное приложение, публикуемое с триммингом и self-contained; оно
везёт внутри бинарника проверочный корпус из `tests/Querio.Tests/Corpus/`. Оно десериализует каждый
документ и записывает обратно, сверяя побайтово, **обоими** способами - через рефлексию и через
генератор исходников, - и возвращает ненулевой код выхода, если хоть один документ не пережил
публикацию.

Задание `trimming` в CI публикует и запускает его на каждый push. Юнит-тесты проверяют, что дескриптор
ILLink на месте; это проверяет, что он делает свою работу, - вопрос другой, и именно он важен.

## Почему перечисления пишутся именами

В обоих примерах выше перечисления настроены как строки. Это не украшение: сохранённый запрос,
записавший `2` вместо оператора, поменяет смысл в тот день, когда кто-нибудь вставит значение в
середину перечисления. `GreaterThan` стоит нескольких лишних байт и переживает рост модели.
