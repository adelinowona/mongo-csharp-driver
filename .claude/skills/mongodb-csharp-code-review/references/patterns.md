# Driver Implementation Patterns

Patterns for implementing code within the MongoDB C# Driver library.

## Async/Await Patterns

### ConfigureAwait(false) - REQUIRED

All library code MUST use `ConfigureAwait(false)` on every await:

```csharp
public async Task<User> GetUserAsync(string id, CancellationToken cancellationToken = default)
{
    return await collection
        .Find(x => x.Id == id)
        .FirstOrDefaultAsync(cancellationToken)
        .ConfigureAwait(false);  // ALWAYS
}
```

**Why:** Prevents deadlocks, improves performance. Enforced by `UseConfigureAwait` analyzer.

### Sync and Async Versions

Provide both for all I/O operations:

```csharp
// Async (primary)
public async Task<User> GetUserAsync(string id, CancellationToken cancellationToken = default)
{
    return await collection
        .Find(x => x.Id == id)
        .FirstOrDefaultAsync(cancellationToken)
        .ConfigureAwait(false);
}

// Sync (uses sync driver APIs, NOT .Result)
public User GetUser(string id)
{
    return collection
        .Find(x => x.Id == id)
        .FirstOrDefault();
}
```

### Never Use .Result or .Wait()

```csharp
// WRONG - causes deadlocks
public User GetUser(string id)
{
    return GetUserAsync(id).Result;  // DON'T
}

// WRONG
task.Wait();  // DON'T
```

### CancellationToken Handling

```csharp
public async Task<List<TDocument>> ProcessAsync(CancellationToken cancellationToken = default)
{
    var results = new List<TDocument>();

    await using var cursor = await _collection
        .FindAsync(FilterDefinition<TDocument>.Empty, cancellationToken)
        .ConfigureAwait(false);

    while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
    {
        foreach (var doc in cursor.Current)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(doc);
        }
    }
    return results;
}
```

## BSON Serialization Patterns

### Custom Serializer

```csharp
public class CustomTypeSerializer : SerializerBase<CustomType>
{
    public override CustomType Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        var bsonType = reader.GetCurrentBsonType();

        switch (bsonType)
        {
            case BsonType.String:
                return ParseFromString(reader.ReadString());
            case BsonType.Document:
                return ParseFromDocument(context);
            default:
                throw new BsonSerializationException($"Cannot deserialize {bsonType} to CustomType");
        }
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, CustomType value)
    {
        context.Writer.WriteString(value.ToString());
    }
}

// Register at startup, not lazily
BsonSerializer.RegisterSerializer(new CustomTypeSerializer());
```

### Discriminators for Polymorphism

```csharp
[BsonDiscriminator(RootClass = true)]
[BsonKnownTypes(typeof(Dog), typeof(Cat))]
public abstract class Animal
{
    public string Name { get; set; }
}

[BsonDiscriminator("dog")]
public class Dog : Animal { public string Breed { get; set; } }

[BsonDiscriminator("cat")]
public class Cat : Animal { public bool IsIndoor { get; set; } }
```

### Class Map Registration

```csharp
// Register at startup, before any operations
static MyDriver()
{
    BsonClassMap.RegisterClassMap<User>(cm =>
    {
        cm.AutoMap();
        cm.MapIdMember(c => c.Id);
        cm.SetIgnoreExtraElements(true);
    });
}
```

**Note:** Class maps are frozen after first use. Cannot be modified at runtime.

## Error Handling Patterns

### Catch Specific Exceptions

```csharp
try
{
    await _collection.InsertOneAsync(user).ConfigureAwait(false);
}
catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
{
    throw new DuplicateUserException($"User {user.Email} already exists", ex);
}
catch (MongoConnectionException ex)
{
    _logger.LogError(ex, "Failed to connect to MongoDB");
    throw new DatabaseUnavailableException("Database connection failed", ex);
}
```

### Include Context in Errors

```csharp
catch (MongoException ex)
{
    throw new DataAccessException(
        $"Failed to update user {userId} in collection {_collectionName} " +
        $"on server {ex.ConnectionId?.ServerId?.EndPoint}",
        ex);
}
```

### MongoException Hierarchy

- `MongoException` - Base class
- `MongoConnectionException` - Connection/network failures
- `MongoWriteException` - Write failures (has `WriteError.Category`)
- `MongoCommandException` - Command execution failures
- `MongoCursorNotFoundException` - Cursor not found
- `MongoAuthenticationException` - Auth failures

## Operations Pattern

All MongoDB commands use the Operations pattern:

```csharp
// Operations implement IReadOperation<T> or IWriteOperation<T>
public class FindOperation<TDocument> : IReadOperation<IAsyncCursor<TDocument>>
{
    private readonly CollectionNamespace _collectionNamespace;
    private readonly IBsonSerializer<TDocument> _serializer;

    public FindOperation(CollectionNamespace ns, IBsonSerializer<TDocument> serializer)
    {
        _collectionNamespace = ns;
        _serializer = serializer;
    }

    public IAsyncCursor<TDocument> Execute(IReadBinding binding, CancellationToken ct)
    {
        // Sync implementation
    }

    public async Task<IAsyncCursor<TDocument>> ExecuteAsync(IReadBinding binding, CancellationToken ct)
    {
        await using var channelSource = await binding
            .GetReadChannelSourceAsync(ct)
            .ConfigureAwait(false);
        // Execute command...
    }
}
```

**Usage:**
```csharp
var operation = new FindOperation<TDocument>(_collectionNamespace, _serializer);
operation.Limit = options?.Limit;
operation.Skip = options?.Skip;

var cursor = await _operationExecutor
    .ExecuteReadOperationAsync(operation, cancellationToken)
    .ConfigureAwait(false);
```

## Resource Disposal

```csharp
// Always dispose cursors
await using var cursor = await _collection.FindAsync(filter);
await cursor.ForEachAsync(doc => Process(doc));

// Use using for IDisposable
using var session = client.StartSession();
```

## Thread Safety

```csharp
// Wrong: Race condition
private List<Connection> _connections = new();
public void Add(Connection c) => _connections.Add(c);  // Not thread-safe

// Right: Thread-safe collection
private readonly ConcurrentBag<Connection> _connections = new();
public void Add(Connection c) => _connections.Add(c);
```
