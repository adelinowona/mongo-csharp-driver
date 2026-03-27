# Performance Guidelines

Performance considerations for the MongoDB C# Driver.

## Hot Paths

Methods called very frequently that require zero/minimal allocations:
- Serialization/deserialization
- BSON reading/writing
- Connection pool checkout/checkin
- Message encoding/decoding
- Server selection

## Key Rules

### No Allocations in Hot Paths

```csharp
// Bad: Allocates on every call
public byte[] SerializeMessage(BsonDocument doc)
{
    var buffer = new byte[4096];  // Heap allocation
    // ...
}

// Good: Use ArrayPool
public byte[] SerializeMessage(BsonDocument doc)
{
    var buffer = ArrayPool<byte>.Shared.Rent(4096);
    try
    {
        // ...
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

### No Boxing in Hot Paths

Avoid casting value types to `object`, using non-generic collections, or string formatting with composite format strings.

```csharp
// Bad: Boxing
object obj = myInt;
ArrayList list = new();
Console.WriteLine("Value: {0}", myInt);

// Good: Generics, interpolation
List<int> list = new();
Console.WriteLine($"Value: {myInt}");
```

### No Exceptions for Control Flow

```csharp
// Bad: Expensive
try { return _dict[key]; }
catch (KeyNotFoundException) { return null; }

// Good: TryGet pattern
_dict.TryGetValue(key, out var value);
return value;
```

### Cache Reflection Results

```csharp
// Bad: Reflection on every call
var props = typeof(T).GetProperties();

// Good: Cache at class level
private static readonly PropertyInfo[] Props = typeof(T).GetProperties();
```

### Register Serializers at Startup

```csharp
// Bad: Lazy registration causes performance hit on first use
// Good: Register once at startup
public static void Initialize()
{
    BsonSerializer.RegisterSerializer(new CustomTypeSerializer());
    BsonClassMap.RegisterClassMap<User>(ConfigureUserClassMap);
}
```

## Benchmarking

Use BenchmarkDotNet for all performance work:

```csharp
[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private BsonDocument _document;

    [GlobalSetup]
    public void Setup()
    {
        _document = new BsonDocument { { "field", "value" } };
    }

    [Benchmark(Baseline = true)]
    public byte[] OldSerializer() => OldMethod(_document);

    [Benchmark]
    public byte[] NewSerializer() => NewMethod(_document);
}
```

**Every performance PR must include:**
1. Benchmark code comparing old vs new
2. Results showing improvement
3. No regressions in other areas

**Key metrics:**
- Mean/Median execution time
- Memory allocations (Gen0, Gen1, Gen2)
- Standard deviation

Example results:
```
|        Method |      Mean |  Gen 0 | Allocated |
|-------------- |----------:|-------:|----------:|
|  OldSerializer | 150.2 us | 15.234 |  62.5 KB  |
|  NewSerializer |  45.3 us |  3.125 |  12.8 KB  |
```
