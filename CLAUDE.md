# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Project Is

The official MongoDB .NET/C# Driver - a library that enables C# applications to communicate with MongoDB databases. This driver implements the MongoDB wire protocol, handles connection pooling, server discovery, and provides both low-level and high-level APIs for database operations.

## Architecture: The Big Picture

### Why This Layered Design?

The driver separates concerns into distinct layers to allow:
- **Independent BSON usage** without the full driver
- **Low-level control** via Core when needed
- **High-level convenience** via the main Driver API
- **Flexibility** for different use cases (sync/async, typed/untyped)

### Layer Structure

**MongoDB.Bson** (Foundation Layer)
- Pure BSON implementation - can be used independently of the driver
- Handles serialization between C# objects and BSON documents
- `BsonClassMap` provides convention-based mapping (similar to Entity Framework)
- Why: BSON is MongoDB's data format; this layer makes it usable in C#

**MongoDB.Driver/Core** (Low-Level Driver)
- Implements the MongoDB wire protocol
- Manages cluster topology via Server Discovery And Monitoring (SDAM) spec
- Handles connection pooling per-server
- Operations are defined here (`IReadOperation<T>`, `IWriteOperation<T>`)
- Why: Encapsulates MongoDB protocol complexity and connection management

**MongoDB.Driver** (High-Level API)
- Public API: `MongoClient`, `IMongoDatabase`, `IMongoCollection<T>`
- LINQ provider for type-safe queries (Linq3 implementation)
- Builders for fluent filter/update/projection construction
- Why: Provides idiomatic C# experience over the low-level protocol

### Critical Patterns

**Operations Pattern**: All MongoDB commands are represented as operation objects that can be executed synchronously or asynchronously. This allows operations to be composed, retried, and tested independently. Find operations in `Core/Operations/`.

**Serialization Convention System**: Instead of requiring attributes on every property, the driver uses conventions (CamelCaseElementNameConvention, IgnoreExtraElementsConvention, etc.) that can be registered globally. This is why you'll see `BsonClassMap.RegisterClassMap<T>()` - it's configuring serialization without polluting domain models.

**SDAM (Server Discovery And Monitoring)**: The driver maintains a real-time view of cluster topology. When servers go down or elections happen, SDAM updates the cluster description. This is why connection logic is in `Core/Clusters/` and `Core/Servers/` - it's implementing the official SDAM specification.

**Async-First Design**: All I/O operations are inherently async. Sync methods often delegate to async implementations. Library code uses `ConfigureAwait(false)` to avoid capturing sync context.

## Deep Dive Documentation

For comprehensive implementation details on key subsystems:

- **[LINQ Provider](docs/LINQ_PROVIDER.md)** - How LINQ queries are translated to MongoDB aggregation pipelines
- **[SDAM (Server Discovery And Monitoring)](docs/SDAM.md)** - How the driver discovers and monitors MongoDB topology
- **[Connection Pooling](docs/CONNECTION_POOLING.md)** - How connection pools are managed per server
- **[Testing Guide](docs/TESTING_GUIDE.md)** - Testing patterns, fixtures, and TestHelpers usage

## How To Work With This Codebase

### Building and Testing
See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines.

For detailed build and test commands, see the `evergreen/` directory scripts:
- `evergreen/compile-sources.sh` - Build the solution
- `evergreen/run-unit-tests.sh` - Run unit tests
- `evergreen/execute-tests.sh` - Run tests with filtering

Quick start:
```bash
dotnet build CSharpDriver.sln -c Release
dotnet test tests/MongoDB.Driver.Tests/MongoDB.Driver.Tests.csproj -c Release
```

Tests use category attributes: `[Category("Integration")]` for tests requiring MongoDB, unit tests for everything else.

### Finding What You Need

| What You're Looking For | Where To Look |
|------------------------|---------------|
| Serialization (BSON ↔ C#) | `src/MongoDB.Bson/Serialization/` |
| LINQ query translation | `src/MongoDB.Driver/Linq/Linq3Implementation/` |
| Connection/pooling logic | `src/MongoDB.Driver/Core/Connections/` and `Core/ConnectionPools/` |
| Server selection/topology | `src/MongoDB.Driver/Core/Clusters/` and `Core/Servers/` |
| Database operations/commands | `src/MongoDB.Driver/Core/Operations/` |
| Public API entry points | `src/MongoDB.Driver/MongoClient.cs`, `IMongoDatabase.cs`, `IMongoCollection.cs` |

### MongoDB Specifications

The `specifications/` directory contains JSON test files from MongoDB's official driver specifications. When implementing features or fixing bugs related to standard driver behavior, consult these specs:
- **CRUD**: How database operations should work
- **SDAM**: Server discovery and monitoring requirements
- **Connection Pooling**: Pool management rules
- **Retryable Reads/Writes**: Retry behavior
- **Transactions**: Multi-document transaction semantics

These specs define *why* certain patterns exist in the code.

### Common Development Tasks

**Adding a new serializer:**
Implement `IBsonSerializer<T>` (see existing serializers in `MongoDB.Bson/Serialization/Serializers/` as examples). Register it via `BsonSerializer.RegisterSerializer<T>()` or use `[BsonSerializer(typeof(YourSerializer))]` attribute.

**Adding a new operation:**
Create a class in `Core/Operations/` implementing `IReadOperation<T>` or `IWriteOperation<T>`. Look at existing operations like `FindOperation.cs` for the pattern. Wire it up through the appropriate collection/database method.

**Modifying LINQ translation:**
LINQ expression translation happens in `Linq/Linq3Implementation/Translators/`. The system converts C# expressions to MongoDB aggregation pipeline stages or filter documents.

### Code Conventions

Commits must start with JIRA ticket: `CSHARP-XXXX: Description`

Code analysis rules are enforced via `.ruleset` files. Follow existing patterns - the driver has been developed over many years with consistent style.

Test projects have corresponding TestHelpers (e.g., `MongoDB.Driver.TestHelpers`) - use these for shared test utilities.

## Project Dependencies

- **MongoDB.Driver** → MongoDB.Bson (the high-level driver uses the BSON layer)
- **MongoDB.Driver.Encryption** → MongoDB.Driver (encryption builds on the driver)
- Shared utilities in `src/MongoDB.Shared/` are linked into projects (not a separate assembly)

## Key Context for AI Agents

This is an **official MongoDB driver** that must implement MongoDB specifications exactly. When in doubt about behavior, check `specifications/` directory for the authoritative spec.

The codebase supports multiple .NET frameworks (net472, netstandard2.0, net6.0+) so avoid using APIs not available in older frameworks without conditionals.

Performance matters - this is a critical library for many production applications. Be mindful of allocations, async state machine overhead, and hot paths.
