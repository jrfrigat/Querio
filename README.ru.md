<p align="center">
  <a href="https://github.com/jrfrigat/Querio">
    <img src="assets/banner.svg" alt="Querio - модель реляционных запросов для .NET, не привязанная к хранилищу" width="860">
  </a>
</p>

# Querio - один запрос, любой бэкенд

<p align="center">🌐 <a href="README.md">English</a> · <b>Русский</b></p>

[![CI](https://github.com/jrfrigat/Querio/actions/workflows/ci.yml/badge.svg)](https://github.com/jrfrigat/Querio/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Querio.svg)](https://www.nuget.org/packages/Querio/)
[![Downloads](https://img.shields.io/nuget/dt/Querio.svg)](https://www.nuget.org/packages/Querio/)
[![.NET](https://img.shields.io/badge/.NET-standard2.0%20%7C%208%20%7C%209%20%7C%2010-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Querio строит запросы и никогда их не выполняет. Вы описываете схему - сущности, поля, внешние
ключи, функции - и собираете по ней запрос. На выходе обычный сериализуемый объект, а не строка
SQL. Во что он превратится, решает кто-то другой.

```csharp
var spec = QueryBuilder.From(schema, "requests", "r")
    .Select("r", "route")
    .CountRows("total")
    .Where(f => f.Since("r", "timestamp", 30, QueryTimeUnit.Day).Equal("r", "error", true))
    .GroupBy("r", "route")
    .OrderBySelectDescending("total")
    .Limit(20)
    .Build();
```

Этот один объект без изменений отрисовывается в любое из перечисленного:

| Цель | Пакет | Что получается |
| --- | --- | --- |
| **SQL** | `Querio.Sql` | Параметризованный SQL для SQL Server, PostgreSQL или SQLite |
| **1С** | `Querio.OneC` | Язык запросов 1С, который вовсе не SQL-образный |
| **LINQ** | `Querio.Linq` | Настоящие деревья выражений - отдайте их EF Core или выполните по объектам |
| **URL** | `Querio.Http` | Строка запроса, читающаяся обратно в тот же запрос |
| **Слова** | `Querio.Text` | Фраза, которую человек может проверить, и она тоже читается обратно |
| **Текст** | `Querio.Language` | SQL-образный текст с навигацией по внешним ключам и подсказками |

---

## Зачем это

Большинство построителей запросов - это построители SQL в чужом пальто. Они исходят из того, что
соединение выглядит как `JOIN`, постраничность - как `LIMIT`, а перцентиль - как вызов функции. Как
только бэкенд с этим не согласен (а язык запросов 1С не согласен почти ни с чем), абстракция рвётся.

Модель Querio **семантическая, а не синтаксическая**:

- **Значения путешествуют как данные.** Условие хранит значение, а не фрагмент текста, поэтому ни
  один рендерер не склеивает его с запросом. Параметризация принадлежит цели.
- **Имена логические.** `QueryEntity.Source` и `QueryField.Column` несут физические имена, поэтому
  один и тот же сохранённый запрос идёт и в `dbo.RequestLog` здесь, и в иначе названное там.
- **Агрегаты и периоды - перечисления**, а не имена функций. Каждая цель отображает `Percentile`
  или `Truncate(Day)` на то, что у неё действительно есть.
- **Относительное время остаётся относительным.** Сохранённые «последние 30 дней» и через год
  означают последние 30 дней, потому что хранится смещение, а не дата, в которую оно разрешилось.
- **Цель объявляет, чего она не умеет.** У диалекта SQLite нет перцентиля, у 1С нет `OFFSET`.
  Запрос такого падает с объяснением, а не отвечает тихо на более узкий вопрос.

Последний пункт и окупается. Абстракция, которая молча приближает, хуже отсутствия абстракции,
потому что узнаёте вы об этом в проде.

---

## Быстрый старт

```sh
dotnet add package Querio
dotnet add package Querio.Sql      # и те цели, которые нужны
```

Опишите, что можно запрашивать:

```csharp
var schema = new QuerySchema(
    [
        new QueryEntity("requests", "Запросы",
        [
            new QueryField("id",        "Id",          QueryFieldType.Guid),
            new QueryField("route",     "Маршрут",     QueryFieldType.Text),
            new QueryField("timestamp", "Момент",      QueryFieldType.DateTime),
            new QueryField("error",     "Ошибка",      QueryFieldType.Boolean),
            new QueryField("apiKeyId",  "Ключ API",    QueryFieldType.Guid) { Column = "api_key_id" },
        ])
        { Source = "dbo.RequestLog", PrimaryKey = ["id"] },

        new QueryEntity("apiKeys", "Ключи API",
        [
            new QueryField("id",   "Id",       QueryFieldType.Guid),
            new QueryField("name", "Название", QueryFieldType.Text),
        ])
        { Source = "dbo.ApiKeys", PrimaryKey = ["id"] },
    ],
    [
        QueryRelation.Simple("request_apiKey", "requests", "apiKeyId", "apiKeys", "id"),
    ]);
```

Отрисуйте:

```csharp
var result = SqlRenderer.Render(spec, schema, SqlServerDialect.Instance);
// result.Sql        -> SELECT TOP (20) [r].[route], COUNT(*) AS [total] FROM [dbo].[RequestLog] ...
// result.Parameters -> @p0 = True
```

---

## Язык запросов

`Querio.Language` читает SQL-образный текст. Он похож на SQL, потому что SQL людям уже знаком, но
это не SQL - и в этом весь смысл: поле достижимо **через внешний ключ**, на любую глубину:

```sql
select [r].[route], [r].[apiKeyId].[name] as [key], count(*) as [total]
from [dbo].[RequestLog] as [r]
where [r].[timestamp] >= now - 30 day and [r].[error] = true
group by [r].[route], [r].[apiKeyId].[name]
order by [total] desc
limit 20
```

Каждый переход превращается в соединение, а цель отрисовывает его как ей удобно: явным `JOIN` в SQL,
точечной ссылкой в 1С. Соединения намеренно внешние: проход по пустому ключу не должен убирать
строку.

Разбор не останавливается на первой ошибке. Каждая проблема возвращается с точным местом, плюс тот
запрос, который всё же удалось собрать из остального - именно это нужно редактору, пока человек
дописывает строку:

```csharp
var result = QueryLanguage.Read(text, schema);
result.Spec;         // частичный запрос или null
result.Diagnostics;  // все проблемы, каждая со Start/Length/Message
```

`QueryCompletion.Suggest(text, caret, schema)` отвечает, что можно написать дальше - так редактор
предлагает поля, функции и ключи, по которым можно пройти глубже.

---

## Что можно спросить следующим

Запрос собирается по шагу, и каждый шаг сужает следующий. На это отвечает `QueryChoices` в ядре -
чтобы визуальный конструктор, командная строка и инструмент, который вызывает модель, одинаково
понимали, что разрешено:

```csharp
var choices = QueryChoices.For(spec, schema, SqliteDialect.Instance);

choices.Fields;                 // что можно вывести, исходя из того, что запрос достаёт
choices.Joins;                  // что можно присоединить и по какой связи
choices.OperatorsFor(field);    // сужено до того, что цель умеет выразить
choices.SortTargets;            // поля плюс имена уже выбранного
```

Передайте `IQueryCapabilities` - и ответы сузятся под конкретный бэкенд: выберите SQLite, и
перцентиль перестанет предлагаться; выберите 1С - и пропадут перекрёстные соединения. Не предлагается
ничего, что упало бы только при отрисовке.

---

## Обратимость

Две цели читают обратно то, что записали, - поэтому в них безопасно хранить запрос:

```csharp
// Ссылка, переживающая закладку
var url  = QueryHttp.ToUri(spec, schema, "/reports");
var back = QueryHttp.ParseUri(url, schema);

// Фраза, которую человек может проверить до запуска
var said = QueryDescriber.Describe(spec, schema);
var read = QueryDescriber.Parse(said, schema);
```

Чтение намеренно **не** зеркально записи. Запись тотальна - записать можно любой запрос. Чтение
частично, потому что текст волен говорить то, для чего в модели нет места; поэтому читатель либо
восстанавливает ровно записанное, либо отказывается и говорит где. Он никогда не спасает то, что
понял наполовину.

---

## Пакеты

| Пакет | Зависит от | Назначение |
| --- | --- | --- |
| [`Querio`](https://www.nuget.org/packages/Querio/) | ничего | Схема, модель запроса, fluent-построитель, валидатор, `QueryChoices` |
| [`Querio.Sql`](https://www.nuget.org/packages/Querio.Sql/) | `Querio` | SQL Server / PostgreSQL / SQLite |
| [`Querio.OneC`](https://www.nuget.org/packages/Querio.OneC/) | `Querio` | Язык запросов 1С |
| [`Querio.Linq`](https://www.nuget.org/packages/Querio.Linq/) | `Querio` | Деревья выражений; работа по `IQueryable` или по объектам |
| [`Querio.Http`](https://www.nuget.org/packages/Querio.Http/) | `Querio` | Строка запроса, в обе стороны |
| [`Querio.Text`](https://www.nuget.org/packages/Querio.Text/) | `Querio` | Обычные слова, в обе стороны |
| [`Querio.Language`](https://www.nuget.org/packages/Querio.Language/) | `Querio` | SQL-образный текст и подсказки |

**Ни один пакет не тянет стороннюю зависимость.** Ни одной - весь набор ссылается только на базовую
библиотеку классов. Это проверяется тестом, а не намерением, чтобы модель запросов можно было
добавить в существующее приложение без спора о том, что она за собой тащит.

Обёртка под Entity Framework не нужна: `Querio.Linq` строит деревья выражений из BCL, и EF
принимает их напрямую. Dapper тоже не нуждается в обёртке - он ест текст и параметры, которые
`Querio.Sql` уже отдаёт.

---

## Целевые платформы

`netstandard2.0`, `net8.0`, `net9.0`, `net10.0`. Заход до netstandard2.0 сделан осознанно:
сохранённый запрос - это контракт, а читать его может старая служба, которую никто не собирается
переводить на новый рантайм.

---

## Документация

- [Быстрый старт](docs/ru/getting-started.md)
- [Архитектура](docs/ru/architecture.md) - почему модель семантическая, а не SQL-образная
- [Язык запросов](docs/ru/query-language.md)
- [Цели](docs/ru/targets.md) - что каждая умеет и чего не умеет
- [Работа с ИИ-агентами](docs/ru/ai-agents.md)
- [Справочник API](https://jrfrigat.github.io/Querio/)

---

## Участие в разработке

См. [CONTRIBUTING.md](CONTRIBUTING.md). Коротко: форк, ветка от `main`, тесты, зелёная сборка.

## Лицензия

[MIT](LICENSE)
