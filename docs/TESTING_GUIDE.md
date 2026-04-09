# Testing Guide

This document explains testing patterns and practices for the MongoDB C# driver codebase.

## Test Organization

### Test Projects

| Project | Purpose | Location |
|---------|---------|----------|
| MongoDB.Bson.Tests | BSON serialization unit tests | `/tests/MongoDB.Bson.Tests` |
| MongoDB.Driver.Tests | Main driver tests (unit + integration) | `/tests/MongoDB.Driver.Tests` |
| MongoDB.Driver.Core.Tests | Core functionality tests | `/tests/MongoDB.Driver.Core.Tests` |
| MongoDB.Driver.GridFS.Tests | GridFS tests | `/tests/MongoDB.Driver.GridFS.Tests` |
| MongoDB.Driver.Encryption.Tests | Encryption-specific tests | `/tests/MongoDB.Driver.Encryption.Tests` |

### Helper Projects

| Project | Purpose | Location |
|---------|---------|----------|
| MongoDB.Bson.TestHelpers | BSON-specific test utilities | `/tests/MongoDB.Bson.TestHelpers` |
| MongoDB.Driver.TestHelpers | Driver test utilities and fixtures | `/tests/MongoDB.Driver.TestHelpers` |
| MongoDB.Driver.Core.TestHelpers | Core test helpers | `/tests/MongoDB.Driver.Core.TestHelpers` |
| MongoDB.TestHelpers | Cross-cutting Xunit extensions | `/tests/MongoDB.TestHelpers` |

### Unit vs Integration Tests

**Unit Tests:**
- No `[Trait("Category", "Integration")]` attribute
- No MongoDB server required
- Fast execution
- Test individual components in isolation

**Integration Tests:**
- Must have `[Trait("Category", "Integration")]` attribute
- Require MongoDB server
- Test real MongoDB interactions
- Use `RequireServer.Check()` for conditional execution

## Key TestHelpers

### DriverTestConfiguration

**Location:** `/tests/MongoDB.Driver.TestHelpers/DriverTestConfiguration.cs`

Provides centralized test client and configuration.

**Usage:**
```csharp
// Get shared test client
var client = DriverTestConfiguration.Client;

// Get test database
var database = DriverTestConfiguration.GetDatabase();

// Get test collection namespace
var collectionNamespace = DriverTestConfiguration.CollectionNamespace;

// Create custom client
var client = DriverTestConfiguration.CreateMongoClient(settings =>
{
    settings.MaxConnectionPoolSize = 50;
});

// Create client with event capturing
var capturer = new EventCapturer();
var client = DriverTestConfiguration.CreateMongoClient(capturer);
```

### Reflector

**Location:** `/tests/MongoDB.Bson.TestHelpers/Reflector.cs`

Access private/internal members for testing.

**Usage:**
```csharp
// Get private field value
var fieldValue = Reflector.GetFieldValue(obj, "_privateField");

// Set private field value
Reflector.SetFieldValue(obj, "_privateField", newValue);

// Invoke private method with 1 argument
var result = Reflector.Invoke<TArg1, TResult>(obj, "MethodName", arg1);

// Invoke private static method
var result = Reflector.InvokeStatic<TArg1, TResult>(typeof(MyClass), "StaticMethod", arg1);

// Get private property
var value = (PrivateType)Reflector.GetProperty(obj, "PrivateProp");
```

**When to Use:**
- Testing internal logic that shouldn't be public API
- Validating private state after operations
- Accessing test-only functionality

**When NOT to Use:**
- Avoid if you can test through public API
- Don't use as workaround for poor public API design

### EventCapturer

**Location:** `/tests/MongoDB.Driver.TestHelpers/Core/EventCapturer.cs`

Captures driver events for verification.

**Usage:**
```csharp
// Create capturer
var eventCapturer = new EventCapturer();

// Capture specific command events
eventCapturer.CaptureCommandEvents("insert", "update", "delete");

// Create client with capturer
var client = DriverTestConfiguration.CreateMongoClient(eventCapturer);

// Perform operations
collection.InsertOne(new BsonDocument("x", 1));

// Wait for events with timeout
eventCapturer.WaitForOrThrowIfTimeout(
    events => events.OfType<CommandStartedEvent>().Count() >= 1,
    TimeSpan.FromSeconds(5));

// Access captured events
var events = eventCapturer.Events;
var commandStarted = events.OfType<CommandStartedEvent>().First();

Assert.Equal("insert", commandStarted.CommandName);
Assert.Equal(1, commandStarted.Command["documents"].AsBsonArray.Count);

// Clear events
eventCapturer.Clear();
```

**Common Patterns:**
```csharp
// Verify command was sent
var startedEvent = eventCapturer.Events
    .OfType<CommandStartedEvent>()
    .Single(e => e.CommandName == "find");

// Verify command succeeded
var succeededEvent = eventCapturer.Events
    .OfType<CommandSucceededEvent>()
    .Single(e => e.CommandName == "insert");

// Count command invocations
var insertCount = eventCapturer.Events
    .OfType<CommandStartedEvent>()
    .Count(e => e.CommandName == "insert");
```

### FailPoint

**Location:** `/tests/MongoDB.Driver.TestHelpers/Core/FailPoint.cs`

Configure server failpoints for testing error scenarios.

**Usage:**
```csharp
// Configure failpoint using IDisposable pattern
using (var failPoint = FailPoint.Configure(
    cluster,
    session,
    "failCommand",
    new BsonDocument
    {
        { "mode", "alwaysOn" },
        { "data", new BsonDocument
            {
                { "failCommands", new BsonArray { "insert" } },
                { "errorCode", 11000 } // Duplicate key error
            }
        }
    }))
{
    // Failpoint active - insert will fail
    var exception = Record.Exception(() => collection.InsertOne(doc));

    exception.Should().BeOfType<MongoWriteException>();
}
// Failpoint automatically disabled on dispose

// Failpoint that triggers once then succeeds
using (var failPoint = FailPoint.Configure(
    cluster,
    session,
    "failCommand",
    new BsonDocument
    {
        { "mode", new BsonDocument { { "times", 1 } } },
        { "data", new BsonDocument
            {
                { "failCommands", new BsonArray { "find" } },
                { "closeConnection", true }
            }
        }
    }))
{
    // First find fails, retry succeeds
    var result = collection.Find(filter).ToList();
}
```

**Common Failpoint Scenarios:**
```csharp
// Network error
"data": { "failCommands": ["insert"], "closeConnection": true }

// Timeout error
"data": { "failCommands": ["find"], "blockConnection": true, "blockTimeMS": 1000 }

// Specific error code
"data": { "failCommands": ["update"], "errorCode": 112 } // WriteConflict

// Error with labels
"data": { "failCommands": ["insert"], "errorLabels": ["TransientTransactionError"] }
```

## Test Fixtures

### Base Fixture: MongoDatabaseFixture

**Location:** `/tests/MongoDB.Driver.TestHelpers/MongoDatabaseFixture.cs`

Base class for database-level test fixtures.

**Features:**
- Lazy client/database initialization
- Per-test case initialization via `InitializeTestCase()`
- Automatic cleanup on dispose

**Usage:**
```csharp
public class MyTestFixture : MongoDatabaseFixture
{
    // Override for fixture-level setup
    protected override void InitializeFixture()
    {
        // Run once when fixture created
        base.InitializeFixture();
        // Custom initialization
    }

    // Override for test-level setup
    protected override void InitializeTestCase()
    {
        // Run before each test
        base.InitializeTestCase();
        // Custom setup
    }
}
```

### MongoCollectionFixture<TDocument>

**Location:** `/tests/MongoDB.Driver.TestHelpers/MongoCollectionFixture.cs`

Base class for collection-based tests with automatic data seeding.

**Usage:**
```csharp
public class MyTestFixture : MongoCollectionFixture<MyDocument>
{
    // Provide initial data
    protected override IEnumerable<MyDocument> InitialData => new[]
    {
        new MyDocument { Id = 1, Name = "Alice" },
        new MyDocument { Id = 2, Name = "Bob" }
    };

    // Optional: Reset data before each test
    public override bool InitializeDataBeforeEachTestCase => true;
}

public class MyTests : IntegrationTest<MyTestFixture>
{
    public MyTests(MyTestFixture fixture) : base(fixture) { }

    [Fact]
    public void Test_can_query_data()
    {
        var collection = Fixture.Collection;
        var count = collection.CountDocuments(FilterDefinition<MyDocument>.Empty);
        count.Should().Be(2);
    }
}
```

### IntegrationTest<TFixture>

**Location:** `/tests/MongoDB.Driver.TestHelpers/IntegrationTest.cs`

Base class for integration tests with fixture support.

**Features:**
- Automatic `[Trait("Category", "Integration")]` attribute
- Fixture lifecycle management
- Optional `RequireServer` checks

**Usage:**
```csharp
[Trait("Category", "Integration")]
public class MyIntegrationTests : IntegrationTest<MyFixture>
{
    public MyIntegrationTests(MyFixture fixture)
        : base(fixture, requireServer =>
        {
            requireServer
                .VersionGreaterThanOrEqualTo("4.2.0")
                .Supports(Feature.AggregationMerge);
        })
    {
    }

    [Fact]
    public void Test_feature()
    {
        // Test only runs if requirements met
        var database = Fixture.Database;
        // ...
    }
}
```

### LinqIntegrationTest

**Location:** `/tests/MongoDB.Driver.TestHelpers/LinqIntegrationTest.cs`

Specialized base for LINQ tests.

**Features:**
- `Translate()` helper to get pipeline stages
- `AssertStages()` for pipeline verification

**Usage:**
```csharp
public class MyLinqTests : LinqIntegrationTest<MyLinqTests.ClassFixture>
{
    public MyLinqTests(ClassFixture fixture) : base(fixture) { }

    [Fact]
    public void Where_should_translate_to_match()
    {
        var collection = Fixture.Collection;

        var queryable = collection.AsQueryable()
            .Where(x => x.Age > 18);

        // Get translated pipeline
        var stages = Translate(collection, queryable);

        // Verify pipeline
        AssertStages(stages, "{ $match: { Age: { $gt: 18 } } }");

        // Execute and verify results
        var results = queryable.ToList();
        results.Should().NotBeEmpty();
    }

    public sealed class ClassFixture : MongoCollectionFixture<Person>
    {
        protected override IEnumerable<Person> InitialData => new[]
        {
            new Person { Id = 1, Age = 25 },
            new Person { Id = 2, Age = 15 }
        };
    }
}
```

## Test Patterns

### Pattern 1: Basic Unit Test

```csharp
public class MongoServerAddressTests
{
    [Fact]
    public void Constructor_should_initialize_properties()
    {
        var address = new MongoServerAddress("localhost", 27017);

        address.Host.Should().Be("localhost");
        address.Port.Should().Be(27017);
    }

    [Theory]
    [InlineData("localhost:27017", "localhost", 27017)]
    [InlineData("example.com:27018", "example.com", 27018)]
    public void Parse_should_parse_connection_string(
        string input, string expectedHost, int expectedPort)
    {
        var address = MongoServerAddress.Parse(input);

        address.Host.Should().Be(expectedHost);
        address.Port.Should().Be(expectedPort);
    }
}
```

### Pattern 2: Integration Test with Data

```csharp
public class FindTests : IntegrationTest<FindTests.TestFixture>
{
    public FindTests(TestFixture fixture) : base(fixture) { }

    [Fact]
    public void Find_should_return_matching_documents()
    {
        var collection = Fixture.Collection;
        var filter = Builders<Person>.Filter.Gt(x => x.Age, 20);

        var results = collection.Find(filter).ToList();

        results.Should().HaveCount(2);
        results.Should().OnlyContain(p => p.Age > 20);
    }

    public sealed class TestFixture : MongoCollectionFixture<Person>
    {
        protected override IEnumerable<Person> InitialData => new[]
        {
            new Person { Id = 1, Name = "Alice", Age = 25 },
            new Person { Id = 2, Name = "Bob", Age = 15 },
            new Person { Id = 3, Name = "Charlie", Age = 30 }
        };
    }
}
```

### Pattern 3: Event Verification

```csharp
[Fact]
public void Insert_should_emit_command_events()
{
    var eventCapturer = new EventCapturer().CaptureCommandEvents("insert");
    var client = DriverTestConfiguration.CreateMongoClient(eventCapturer);
    var collection = client.GetDatabase("test").GetCollection<BsonDocument>("test");

    collection.InsertOne(new BsonDocument("x", 1));

    var events = eventCapturer.Events;
    events.Should().ContainSingle(e => e is CommandStartedEvent);

    var commandStarted = events.OfType<CommandStartedEvent>().First();
    commandStarted.CommandName.Should().Be("insert");
    commandStarted.Command["documents"].AsBsonArray.Should().HaveCount(1);
}
```

### Pattern 4: Exception Testing

```csharp
[Fact]
public void Parse_should_throw_for_invalid_input()
{
    var exception = Record.Exception(() =>
        MongoServerAddress.Parse("invalid"));

    exception.Should().BeOfType<FormatException>();
    exception.Message.Should().Contain("not a valid server address");
}
```

### Pattern 5: Failpoint Testing

```csharp
[Fact]
public void Insert_should_retry_on_transient_error()
{
    RequireServer.Check().Supports(Feature.FailPointsFailCommand);

    var eventCapturer = new EventCapturer().CaptureCommandEvents("insert");
    var client = DriverTestConfiguration.CreateMongoClient(eventCapturer);
    var collection = client.GetDatabase("test").GetCollection<BsonDocument>("test");

    using (var failPoint = FailPoint.Configure(
        client.Cluster,
        NoCoreSession.NewHandle(),
        "failCommand",
        new BsonDocument
        {
            { "mode", new BsonDocument("times", 1) },
            { "data", new BsonDocument
                {
                    { "failCommands", new BsonArray { "insert" } },
                    { "errorCode", 112 }, // WriteConflict
                    { "errorLabels", new BsonArray { "RetryableWriteError" } }
                }
            }
        }))
    {
        collection.InsertOne(new BsonDocument("x", 1));
    }

    // Verify retry occurred
    var insertCommands = eventCapturer.Events
        .OfType<CommandStartedEvent>()
        .Where(e => e.CommandName == "insert");
    insertCommands.Should().HaveCount(2); // Original + retry
}
```

## RequireServer Pattern

**Location:** `/tests/MongoDB.Driver.TestHelpers/Core/XunitExtensions/RequireServer.cs`

Conditional test execution based on server capabilities.

**Usage:**
```csharp
[Fact]
public void Test_requires_specific_features()
{
    RequireServer.Check()
        .Supports(Feature.AggregationMerge)
        .ClusterType(ClusterType.ReplicaSet)
        .VersionGreaterThanOrEqualTo("4.2.0")
        .StorageEngine("wiredTiger")
        .Authentication(authentication: true);

    // Test runs only if all requirements met
}
```

**Available Checks:**

| Method | Description |
|--------|-------------|
| `Supports(Feature)` | Requires feature support |
| `DoesNotSupport(Feature)` | Requires feature NOT supported |
| `ClusterType(ClusterType)` | Requires specific cluster type |
| `ClusterTypes(params ClusterType[])` | Requires one of cluster types |
| `VersionGreaterThanOrEqualTo(string)` | Requires server version >= |
| `VersionLessThan(string)` | Requires server version < |
| `StorageEngine(string)` | Requires specific storage engine |
| `Authentication(bool)` | Requires auth enabled/disabled |
| `Tls(bool)` | Requires TLS enabled/disabled |
| `LoadBalancing(bool)` | Requires load balancing |
| `SupportsSessions()` | Requires session support |

**What Happens When Requirements Not Met:**
- Test is **skipped** (not failed) via `SkipException`
- Test results show as "Skipped" in test runners

## Test Categories

Tests are categorized using `[Trait]` attributes:

| Category | Usage | Requirements |
|----------|-------|--------------|
| Integration | `[Trait("Category", "Integration")]` | MongoDB server |
| OCSP | `[Trait("Category", "OCSP")]` | OCSP testing |
| Authentication | `[Trait("Category", "Authentication")]` | Auth tests |
| CSFLE | `[Trait("Category", "CSFLE")]` | Client-side encryption |
| MongoDbOidc | `[Trait("Category", "MongoDbOidc")]` | OIDC auth |
| SDAM | `[Trait("Category", "SDAM")]` | SDAM-specific tests |

**Running Filtered Tests:**
```bash
# Run only unit tests
dotnet test --filter "Category!=Integration"

# Run integration tests
dotnet test --filter "Category=Integration"

# Run specific category
dotnet test --filter "Category=Authentication"

# Exclude categories
dotnet test --filter "Category!=OCSP&Category!=Authentication"
```

## FluentAssertions

The driver uses FluentAssertions extensively.

**Basic Assertions:**
```csharp
result.Should().Be(expected);
result.Should().NotBeNull();
result.Should().BeOfType<ExpectedType>();
collection.Should().Contain(item);
collection.Should().HaveCount(3);
collection.Should().BeEmpty();
string.Should().StartWith("prefix");
exception.Should().BeNull();
```

**Collection Assertions:**
```csharp
results.Should().HaveCount(3);
results.Should().Contain(x => x.Id == 1);
results.Should().OnlyContain(x => x.Age > 18);
results.Select(x => x.Id).Should().Equal(1, 2, 3);
results.Should().BeInAscendingOrder(x => x.Name);
```

**BSON Assertions:**
```csharp
// BsonDocument equality (MongoDB.Bson.TestHelpers)
bsonDocument.Should().Be("{ x: 1, y: 2 }");
bsonDocument.Should().BeEquivalentTo(expectedDoc);
bsonDocument.Should().Contain("fieldName");

// BsonArray equality
bsonArray.Should().Equal(new[] { value1, value2 });
```

## Specification-Based Testing

JSON-driven tests from MongoDB specifications.

**Location:** `/specifications/` directory

**Test Runner Pattern:**
```csharp
[Trait("Category", "Integration")]
public class UnifiedTestSpecRunner : LoggableTestClass
{
    [UnifiedTestsTheory("crud.tests.unified")]
    public void Crud(JsonDrivenTestCase testCase) => Run(testCase);

    [UnifiedTestsTheory("change_streams.tests.unified")]
    public void ChangeStreams(JsonDrivenTestCase testCase) => Run(testCase);

    private void Run(JsonDrivenTestCase testCase)
    {
        var runner = new UnifiedTestRunner();
        runner.Run(testCase);
    }
}
```

**Purpose:**
- Ensure driver behavior matches MongoDB standards
- Cross-driver consistency
- Comprehensive coverage of edge cases

## Best Practices

### DO

1. **Use descriptive test names**
   ```csharp
   // Good
   public void Find_should_return_empty_when_collection_is_empty()

   // Bad
   public void Test1()
   ```

2. **Use Theory for parameterized tests**
   ```csharp
   [Theory]
   [InlineData(1, 2, 3)]
   [InlineData(10, 20, 30)]
   public void Add_should_return_sum(int a, int b, int expected)
   ```

3. **Use fixtures for shared setup**
   ```csharp
   public class MyTests : IClassFixture<MyFixture>
   {
       private readonly MyFixture _fixture;

       public MyTests(MyFixture fixture)
       {
           _fixture = fixture;
       }
   }
   ```

4. **Use RequireServer for conditional tests**
   ```csharp
   RequireServer.Check().VersionGreaterThanOrEqualTo("4.2.0");
   ```

5. **Clean up resources**
   ```csharp
   using (var session = client.StartSession())
   {
       // Use session
   } // Automatically disposed
   ```

6. **Test both sync and async**
   ```csharp
   [Fact]
   public void Find_sync_should_work() { }

   [Fact]
   public async Task FindAsync_should_work() { }
   ```

### DON'T

1. **Don't use mocking frameworks**
   - Use custom mocks or test doubles
   - Use FailPoints for server errors
   - Use MockConnection for unit tests

2. **Don't mix unit and integration tests**
   - Keep them in separate classes
   - Use `[Trait("Category", "Integration")]` consistently

3. **Don't hardcode server addresses**
   - Use `DriverTestConfiguration` for configuration
   - Support running against different environments

4. **Don't leave commented-out tests**
   - Either fix and enable, or delete
   - Use `[Fact(Skip = "reason")]` if temporarily disabled

5. **Don't test mocked behavior**
   - Test real logic, not mock setup
   - If you need mocks, verify real behavior separately

6. **Don't ignore test failures**
   - All test failures are blocking issues
   - Fix immediately or understand why failing

## Running Tests

### Command Line

```bash
# Run all tests
dotnet test

# Run specific project
dotnet test tests/MongoDB.Driver.Tests/MongoDB.Driver.Tests.csproj

# Run unit tests only
dotnet test --filter "Category!=Integration"

# Run integration tests
dotnet test --filter "Category=Integration"

# Run specific test
dotnet test --filter "FullyQualifiedName~FindTests.Find_should_return_matching_documents"

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run in parallel
dotnet test --parallel
```

### Visual Studio / Rider

1. Open Test Explorer
2. Filter by category, trait, or name
3. Right-click and select "Run" or "Debug"
4. View test output in Test Explorer

## Debugging Tests

### Enable Driver Logging

```csharp
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

var settings = MongoClientSettings.FromConnectionString(connectionString);
settings.LoggingSettings = new LoggingSettings(loggerFactory);

var client = new MongoClient(settings);
```

### Capture Events

```csharp
var eventCapturer = new EventCapturer();
var client = DriverTestConfiguration.CreateMongoClient(eventCapturer);

// Perform operations

// Inspect captured events
foreach (var evt in eventCapturer.Events)
{
    Console.WriteLine($"{evt.GetType().Name}: {evt}");
}
```

### Use Debugger

- Set breakpoints in test or driver code
- Step through execution
- Inspect variables and state
- Use conditional breakpoints for specific scenarios

## Summary

Key testing practices:
- Use TestHelpers for common patterns (EventCapturer, Reflector, FailPoint)
- Use fixtures for shared setup (MongoCollectionFixture, IntegrationTest)
- Use RequireServer for conditional execution
- Use FluentAssertions for readable assertions
- Categorize tests properly (Integration vs unit)
- Don't use mocking frameworks - use real components or custom test doubles
- Run specification tests to ensure compliance
- Test both success and failure paths
- Clean up resources properly
