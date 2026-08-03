# API reference

Generated from the XML documentation in the source. Every public type and member is documented -
`CS1591` is a build error for the shipped packages, so an undocumented member cannot be released.

Start with:

- `QuerySchema` - what a query is allowed to refer to
- `QuerySpec` - the query itself, serializable and target-free
- `QueryBuilder` - the fluent way to compose one
- `QueryChoices` - what the query may be given next
- `QueryRenderer<TExpression>` - the shared walk every target renders through
