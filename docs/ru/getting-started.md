# Быстрый старт

## Установка

```sh
dotnet add package Querio          # модель, построитель, валидатор
dotnet add package Querio.Sql      # и нужные цели
```

Больше ничего с ними не приедет: пакеты ссылаются только на базовую библиотеку классов.

## 1. Опишите, что можно запрашивать

`QuerySchema` - это словарь, которым запросу разрешено пользоваться. Пишете его вы: Querio не читает
базу, и это осознанно, потому что набор того, что пользователю можно запрашивать, редко совпадает с
набором существующих таблиц.

```csharp
var schema = new QuerySchema(
    entities:
    [
        new QueryEntity("requests", "Запросы",
        [
            new QueryField("id",         "Id",            QueryFieldType.Guid),
            new QueryField("route",      "Маршрут",       QueryFieldType.Text),
            new QueryField("timestamp",  "Момент",        QueryFieldType.DateTime),
            new QueryField("durationMs", "Длительность",  QueryFieldType.Number) { Column = "duration_ms" },
            new QueryField("error",      "Ошибка",        QueryFieldType.Boolean),
            new QueryField("apiKeyId",   "Ключ API",      QueryFieldType.Guid) { Column = "api_key_id" },
        ])
        { Source = "dbo.RequestLog", PrimaryKey = ["id"] },

        new QueryEntity("apiKeys", "Ключи API",
        [
            new QueryField("id",   "Id",       QueryFieldType.Guid),
            new QueryField("name", "Название", QueryFieldType.Text),
        ])
        { Source = "dbo.ApiKeys", PrimaryKey = ["id"] },
    ],
    relations:
    [
        QueryRelation.Simple("request_apiKey", "requests", "apiKeyId", "apiKeys", "id"),
    ]);
```

Три вещи, на которые стоит обратить внимание:

- **`Key` логический, `Source`/`Column` физические.** Запрос говорит `requests` и `apiKeyId`, SQL -
  `dbo.RequestLog` и `api_key_id`. Переименуете колонку позже - ни один сохранённый запрос не
  сломается.
- **`Label` для людей.** Его показывают выборщики и сгенерированные описания.
- **`QueryFieldType` семантический.** Он определяет, какие операторы и агрегаты предлагаются по
  умолчанию и как читается сохранённое значение.

Поле можно и ограничить:

```csharp
new QueryField("secret", "Секрет", QueryFieldType.Text) { Filterable = false, Groupable = false }
```

## 2. Соберите запрос

```csharp
var spec = QueryBuilder.From(schema, "requests", "r")
    .Join("apiKeys", "k")                       // связь выводится, когда достижима ровно одна
    .Select("r", "route", "route")
    .Select("k", "name", "key")
    .CountRows("total")
    .Where(f => f
        .Since("r", "timestamp", 30, QueryTimeUnit.Day)
        .Equal("r", "error", true))
    .GroupBy("r", "route")
    .GroupBy("k", "name")
    .Having(f => f.SelectGreaterThan("total", 100))
    .OrderBySelectDescending("total")
    .Limit(20)
    .Build();
```

Псевдонимы у источников обязательны. Именно это позволяет одной сущности встретиться дважды -
соединение с самой собой или два внешних ключа в одну цель - без того, чтобы вхождения стали
неоднозначными.

## 3. Проверьте

```csharp
var result = spec.Validate(schema);
if (!result.IsValid)
{
    foreach (var error in result.Errors)
        Console.WriteLine($"{error.Path}: {error.Message}");
}
```

Валидация - про осмысленность относительно схемы, а не про конкретный бэкенд. Сможет ли данная цель
*выразить* запрос - отдельный вопрос, ответ на который вы получите при отрисовке.

## 4. Отрисуйте

```csharp
var sql = SqlRenderer.Render(spec, schema, SqlServerDialect.Instance);

Console.WriteLine(sql.Sql);
foreach (var p in sql.Parameters)
    Console.WriteLine($"{p.Name} = {p.Value}");
```

```
SELECT TOP (20) [r].[route] AS [route], [k].[name] AS [key], COUNT(*) AS [total]
FROM [dbo].[RequestLog] AS [r]
INNER JOIN [dbo].[ApiKeys] AS [k] ON [r].[api_key_id] = [k].[id]
WHERE [r].[timestamp] >= DATEADD(day, -30, SYSUTCDATETIME()) AND [r].[error] = @p0
GROUP BY [r].[route], [k].[name]
HAVING COUNT(*) > @p1
ORDER BY [total] DESC

@p0 = True
@p1 = 100
```

Отдайте текст и параметры тому, чем уже пользуетесь: Dapper, ADO.NET, что угодно. Querio не открывает
соединений.

## 5. Сохраните и прочитайте обратно

Запрос - обычный объект, подойдёт любой сериализатор:

```csharp
var json = JsonSerializer.Serialize(spec, JsonOptions);
var back = JsonSerializer.Deserialize<QuerySpec>(json, JsonOptions)!;
```

Или храните так, чтобы человек мог прочесть:

```csharp
var url  = QueryHttp.ToUri(spec, schema, "/reports");   // переживёт закладку
var said = QueryDescriber.Describe(spec, schema);       // "From Requests (r), showing Route ..."
```

И то, и другое читается обратно в тот же запрос.

## Когда цель не умеет

```csharp
try
{
    SqlRenderer.Render(spec, schema, SqliteDialect.Instance);
}
catch (QueryRenderException error)
{
    // error.Feature == QueryFeature.Percentile
    Console.WriteLine(error.Message);
}
```

Так задумано. Цель, которая вместо этого приблизила бы, ответила бы на другой вопрос и не сказала бы
об этом.

Чтобы знать заранее - например, погасить пункт в интерфейсе, - спросите до сборки:

```csharp
var choices = QueryChoices.For(spec, schema, SqliteDialect.Instance);
choices.AggregatesFor(new QueryFieldRef("r", "durationMs"));   // перцентиля в списке нет
```

## Дальше

- [Архитектура](architecture.md) - почему модель выглядит именно так
- [Цели](targets.md) - что каждая умеет и чего не умеет
- [Язык запросов](query-language.md) - запрос текстом, а не кодом
