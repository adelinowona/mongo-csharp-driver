# Testing Guidelines

Testing standards for the MongoDB C# Driver.

## Test Naming

```csharp
// Pattern: MethodName_Scenario_ExpectedBehavior
[Fact]
public void InsertOne_WithDuplicateKey_ThrowsMongoWriteException()

[Fact]
public async Task FindAsync_WithCancellation_ThrowsOperationCanceledException()
```

## Test Structure

```csharp
[Fact]
public async Task InsertAndFind_WithValidDocument_RetrievesCorrectly()
{
    // Arrange
    var user = new User { Username = "test" };

    // Act
    await _collection.InsertOneAsync(user);
    var retrieved = await _collection
        .Find(u => u.Username == "test")
        .FirstOrDefaultAsync();

    // Assert
    Assert.NotNull(retrieved);
    Assert.Equal(user.Username, retrieved.Username);
}
```

## Integration Tests

Use real MongoDB instances, not mocks:

```csharp
public class MongoDbIntegrationTestFixture : IAsyncLifetime
{
    private MongoDbContainer _container;
    public IMongoClient Client { get; private set; }

    public async Task InitializeAsync()
    {
        _container = new MongoDbBuilder()
            .WithImage("mongo:7.0")
            .Build();

        await _container.StartAsync();
        Client = new MongoClient(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[Collection("MongoDB")]
public class UserTests : IClassFixture<MongoDbIntegrationTestFixture>
{
    private readonly IMongoCollection<User> _collection;

    public UserTests(MongoDbIntegrationTestFixture fixture)
    {
        var db = fixture.Client.GetDatabase($"test_{Guid.NewGuid()}");
        _collection = db.GetCollection<User>("users");
    }
}
```

## Testing Async Properly

```csharp
// Wrong: Sync test for async method
[Fact]
public void FindAsync_ReturnsUsers()
{
    var result = _collection.Find(_ => true).ToListAsync().Result;  // DON'T
}

// Right: Async test
[Fact]
public async Task FindAsync_ReturnsUsers()
{
    var result = await _collection.Find(_ => true).ToListAsync();
    Assert.NotNull(result);
}
```

## Testing Cancellation

```csharp
[Fact]
public async Task FindAsync_WithCancellation_Cancels()
{
    var cts = new CancellationTokenSource();
    var task = _collection.Find(_ => true).ToListAsync(cts.Token);
    cts.Cancel();

    await Assert.ThrowsAsync<OperationCanceledException>(() => task);
}
```

## Testing Errors

```csharp
[Fact]
public async Task InsertOne_WithDuplicateKey_ThrowsWithCorrectCategory()
{
    var user1 = new User { Username = "duplicate" };
    var user2 = new User { Username = "duplicate" };

    await _collection.InsertOneAsync(user1);

    var ex = await Assert.ThrowsAsync<MongoWriteException>(
        () => _collection.InsertOneAsync(user2));

    Assert.Equal(ServerErrorCategory.DuplicateKey, ex.WriteError.Category);
}
```

## When to Mock

**Mock for unit tests** - testing logic in isolation:
```csharp
var mockCollection = new Mock<IMongoCollection<User>>();
var mockCursor = new Mock<IAsyncCursor<User>>();
// Test service logic without database
```

**Don't mock for integration tests** - test real database interaction:
```csharp
// Use real MongoDB instance via TestContainers or local server
var collection = _database.GetCollection<User>("users");
```

## Anti-Patterns

### Test Interdependence

```csharp
// Bad: Tests depend on each other
private static User _sharedUser;

[Fact] public void Test1_Create() { _sharedUser = ...; }
[Fact] public void Test2_Update() { /* Uses _sharedUser */ }  // Fails if Test1 doesn't run first
```

### Testing Implementation Details

```csharp
// Bad: Tests how, not what
[Fact] public void InsertOne_CallsInternalMethod() { }

// Good: Tests behavior
[Fact] public void InsertOne_WithValidDocument_DocumentIsInserted() { }
```

### Non-Deterministic Tests

```csharp
// Bad: Timing dependency
await Task.Delay(100);
Assert.True(operation.IsComplete);

// Good: Wait with timeout
await operation.WaitAsync(TimeSpan.FromSeconds(5));
```
