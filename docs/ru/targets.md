# Цели

Один объект запроса, шесть способов превратить его во что-то ещё. Каждая цель объявляет, чего не
умеет выразить, и бросает `QueryRenderException` вместо приближения.

## Querio.Sql

Параметризованный SQL для трёх движков. Значения не попадают в текст.

```csharp
var result = SqlRenderer.Render(spec, schema, PostgreSqlDialect.Instance);
result.Sql;          // сам оператор
result.Parameters;   // пары имя/значение для вашего клиента
```

| | SQL Server | PostgreSQL | SQLite |
| --- | --- | --- | --- |
| Ограничение строк | `TOP (n)` либо `OFFSET/FETCH` при пропуске | `LIMIT/OFFSET` | `LIMIT/OFFSET` |
| Поиск без учёта регистра | `LIKE` | `ILIKE` | `LIKE` |
| Усечение до дня | `DATEADD(day, DATEDIFF(day, 0, x), 0)` | `date_trunc('day', x)` | `date(x)` |
| Перцентиль | только без группировки | да | **нет** |
| Табличные функции | да | да | **нет** |
| Усечение до квартала | да | да | **нет** |

Две детали, которые стоит знать:

- **Постраничность без сортировки.** `OFFSET` нужно, в чём пропускать, поэтому SQL Server получает
  `ORDER BY (SELECT NULL)`, когда у запроса нет своей сортировки.
- **Перцентиль с группировкой в SQL Server.** Там `PERCENTILE_CONT` - оконная функция, а не агрегат,
  и её нельзя совместить с `GROUP BY` в одном операторе. Диалект отказывается и объясняет, а не
  выдаёт то, что вернёт другую форму результата.

Встроенные функции пишутся без кавычек (`UPPER(x)`), квалифицированное имя - это процедура и
квотируется по частям (`[dbo].[CalcTax](x)`). Закавыченная встроенная отправила бы движок искать
несуществующую процедуру.

## Querio.OneC

Язык запросов 1С - цель, которая держит всю модель честной, потому что она не SQL-образная.

```csharp
var result = OneCRenderer.Render(spec, schema);
result.Query;        // кириллические ключевые слова
result.Parameters;   // в стиле &p0
```

Не умеет: перцентили, `OFFSET`, перекрёстные соединения, табличные функции. Каждое ограничение
объявлено, а не обнаруживается опытным путём.

1С вообще не умеет заключать идентификатор в кавычки, поэтому имя, не являющееся допустимым
идентификатором 1С, **отвергается**, а не экранируется. Это честный ответ: экранировать нечем.

Относительные окна разрешаются в параметр при отрисовке, поскольку у 1С нет аналога `DATEADD`. Сам
запрос по-прежнему *хранит* смещение; закрепляет его только эта цель.

## Querio.Linq

Настоящие деревья выражений, а не интерпретатор.

```csharp
// Отдать EF Core то, что он умеет транслировать
var predicate = QueryPredicate.For<RequestRow>(spec, schema);
var rows = dbContext.Requests.Where(predicate).ToList();

// Либо выполнить весь запрос по объектам
var result = QueryExecutor.Execute(spec, schema, new QuerySources()
    .Add("requests", requests)
    .Add("apiKeys", apiKeys));
```

**Зависимости на Entity Framework нет и не нужно.** `System.Linq.Expressions` входит в базовую
библиотеку классов, а EF принимает эти деревья напрямую.

Деревья состоят из настоящих операторов: фиксированное значение сужается до CLR-типа члена ещё при
сборке, поэтому условие выходит как `x.DurationMs > 100` с типизированной константой, а принадлежность
множеству - одним узлом `List<T>.Contains`, а не цепочкой сравнений. Провайдер читает это целиком.

Не умеет: правое и полное внешние соединения. У последовательности объектов нет для них естественной
формы.

Агрегат по нулю строк даёт null, а не ноль, - как и хранилище.

У функций, объявленных схемой, нет реализации, пока вы её не дадите:

```csharp
var functions = new QueryFunctionLibrary()
    .Register<string, string>("upper", text => text.ToUpperInvariant())
    .RegisterTable("activeUsers", args => users.Where(u => u.LastSeen > (DateTime)args[0]!));
```

Вызов нереализованной функции падает. Схема говорит, что функция *существует*, но никогда - что она
делает.

## Querio.Http

Строка запроса, в обе стороны.

```csharp
var text = QueryHttp.Render(spec, schema);    // читаемая: хранить, логировать, показывать
var url  = QueryHttp.ToUri(spec, schema, "/reports");   // percent-encoded: класть в ссылку
var back = QueryHttp.ParseUri(url, schema);
```

```
from=requests:r
&select=r.route as route,count() as total
&where=r.timestamp ge -30d and r.error eq 'true'
&groupby=r.route
&orderby=total desc
&top=20
```

Ключи логические, поэтому тот же текст означает тот же запрос и в иначе названном хранилище. Значения
пишутся ровно в той форме, в какой хранятся, поэтому чтение возвращает то же значение, а не похожее
на вид: кавычки, запятые и скобки внутри значения переживают дорогу.

## Querio.Text

Запрос как фраза, в обе стороны.

```csharp
QueryDescriber.Describe(spec, schema);
// From Requests (r), showing Route and the number of rows called total,
// where (Timestamp is at least the last 30 days and Error is "true"),
// grouped by Route, ordered by total descending, first 20
```

Написано подписями, которые выбрал человек, а не именами хранилища. Все связки сменные, поэтому
запрос можно описать - и прочитать обратно - на другом языке:

```csharp
var labels = QueryDescriptionLabels.Default with { From = "из", Showing = "показываем", Where = "где" };
var said   = QueryDescriber.Describe(spec, schema, labels);
var read   = QueryDescriber.Parse(said, schema, labels);
```

Поля уточняются псевдонимом только когда запрос достаёт больше одного источника, поэтому
однотабличный случай (а отчёт обычно такой) остаётся коротким. Чтение применяет то же правило, так что
разойтись они не могут.

## Querio.Language

SQL-образный текст с тем единственным, чего SQL не умеет. См. [язык запросов](query-language.md).

## Что выбрать

- Хранить запрос там, где человек может его прочесть или поправить -> `Querio.Http` или `Querio.Text`
- Показать запрос на утверждение -> `Querio.Text`
- Дать кому-то написать запрос -> `Querio.Language`
- Выполнить в базе -> `Querio.Sql` или `Querio.OneC`
- Выполнить по объектам или через EF -> `Querio.Linq`

Они сочетаются: описать запрос словами для окна подтверждения, отрисовать в SQL для выполнения, а
ссылку положить в журнал аудита. Каждый раз - один и тот же объект.
