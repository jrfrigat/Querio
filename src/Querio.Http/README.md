# Querio.Http

Writes a [Querio](https://www.nuget.org/packages/Querio/) query as an HTTP query string, and reads
one back.

```csharp
var url  = QueryHttp.ToUri(spec, schema, "/reports");
var back = QueryHttp.ParseUri(url, schema);
```

```
from=requests:r&select=r.route as route,count() as total
&where=r.timestamp ge -30d&groupby=r.route&orderby=total desc&top=20
```

The round trip is exact, so a query survives a link, a bookmark, a saved report or a request from a
client. Keys are logical rather than physical, so the same text still means the same query against a
differently-named store.

Reading is deliberately partial: text can say things the model has no room for, so a reader either
recovers exactly what was written or refuses and says where, with a position into the text. It never
salvages what it half understood.

Targets `netstandard2.0`, `net8.0`, `net9.0`, `net10.0`. Depends only on `Querio`.

[Documentation](https://github.com/jrfrigat/Querio) - [MIT](https://github.com/jrfrigat/Querio/blob/main/LICENSE)
