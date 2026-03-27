# API Design Guidelines

API design principles for the MongoDB C# Driver.

## Core Principles

1. **Consistency is paramount** - Follow existing patterns. Similar operations should have similar APIs.
2. **Ease of use over flexibility** - Common cases should be simple. Advanced cases should be possible.
3. **Type safety when possible** - Use `IMongoCollection<T>`. Provide `BsonDocument` overloads when needed.
4. **No breaking changes** - Use `[Obsolete]` for deprecation. Breaking changes require major version bump.

## Naming Conventions

| Element | Convention | Examples |
|---------|------------|----------|
| Classes | PascalCase nouns | `MongoClient`, `FilterDefinition` |
| Methods | PascalCase verbs | `InsertOne`, `UpdateMany` |
| Async methods | `Async` suffix | `FindAsync`, `InsertOneAsync` |
| Properties | PascalCase nouns | `DatabaseName`, `ServerAddress` |
| Parameters | camelCase | `filter`, `cancellationToken` |
| Private fields | `_camelCase` | `_client`, `_database` |
| Booleans | `Is`/`Has`/`Can`/`Should` prefix | `IsActive`, `HasIndex` |

## Method Signatures

### Parameter Ordering

1. Primary input (document, filter, etc.)
2. Options object (if applicable)
3. CancellationToken (always last, always optional with `default`)

```csharp
public Task<ReplaceOneResult> ReplaceOneAsync(
    FilterDefinition<TDocument> filter,           // 1. Primary input
    TDocument replacement,                        // 1. Primary input
    ReplaceOptions options = null,                // 2. Options
    CancellationToken cancellationToken = default // 3. CancellationToken last
)
```

### Use Options Objects

For methods with 3+ optional parameters, use an options object:

```csharp
// Good: Options object
public Task<List<TDocument>> FindAsync(
    FilterDefinition<TDocument> filter,
    FindOptions<TDocument> options = null,
    CancellationToken cancellationToken = default)

// Bad: Overload explosion
public Task<List<TDocument>> FindAsync(FilterDefinition<TDocument> filter)
public Task<List<TDocument>> FindAsync(FilterDefinition<TDocument> filter, int limit)
public Task<List<TDocument>> FindAsync(FilterDefinition<TDocument> filter, int limit, int skip)
```

## Builder Pattern

Use builder pattern for complex query construction:

```csharp
// Access via static Builders class
var filter = Builders<User>.Filter.And(
    Builders<User>.Filter.Eq(u => u.IsActive, true),
    Builders<User>.Filter.Gte(u => u.Age, 18)
);

var update = Builders<User>.Update
    .Set(u => u.LastLogin, DateTime.UtcNow)
    .Inc(u => u.LoginCount, 1);
```

## Versioning and Deprecation

**Semantic versioning:**
- **Major (X.0.0)**: Breaking changes allowed
- **Minor (1.X.0)**: Additive changes only (new methods, optional parameters)
- **Patch (1.0.X)**: Bug fixes only

**Deprecation pattern:**
```csharp
[Obsolete("Use FindAsync with FindOptions parameter instead. Will be removed in version 3.0.")]
public Task<List<TDocument>> FindAsync(FilterDefinition<TDocument> filter, int limit)
{
    return FindAsync(filter, new FindOptions<TDocument> { Limit = limit });
}
```

## XML Documentation

**Required for all public APIs:**
- `<summary>` - Brief description (start with verb: "Inserts", "Gets", "Updates")
- `<param>` - Description of each parameter
- `<returns>` - Description of return value (non-void methods)
- `<typeparam>` - Description of generic type parameters

**Optional:**
- `<remarks>` - Important contextual information (use sparingly)

**Not used in this codebase:** `<exception>`, `<example>`

```csharp
/// <summary>
/// Inserts a single document into the collection.
/// </summary>
/// <param name="document">The document to insert.</param>
/// <param name="options">The options for the insert operation.</param>
/// <param name="cancellationToken">The cancellation token.</param>
/// <returns>A task.</returns>
public Task InsertOneAsync(
    TDocument document,
    InsertOneOptions options = null,
    CancellationToken cancellationToken = default)
```

## Anti-Patterns

```csharp
// Bad: CancellationToken not last
public Task SaveAsync(CancellationToken cancellationToken, User user)

// Bad: Missing Async suffix
public async Task<User> FindUser(string id)

// Bad: Inconsistent naming
public Task<User> GetUserAsync(string id)
public Task<Order> FetchOrder(string id)      // Should be GetOrderAsync
public Task<Product> RetrieveProductAsync(string id)  // Should be GetProductAsync

// Bad: Breaking change in minor version
// v1.0: public Task SaveAsync(User user, bool validate)
// v1.1: public Task SaveAsync(User user)  // Removed parameter = breaking!
```
