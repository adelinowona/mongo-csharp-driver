# Connection Pooling

This document explains how connection pooling works in the MongoDB C# driver.

## Overview

Connection pooling improves performance by reusing TCP connections to MongoDB servers instead of creating new connections for each operation. Each server in a cluster has its own independent connection pool.

## Core Architecture

### Main Implementation

**ExclusiveConnectionPool** (`src/MongoDB.Driver/Core/ConnectionPools/ExclusiveConnectionPool.cs`)

The sole production implementation of `IConnectionPool`. Each MongoDB server (`IServer`) has one pool.

**"Exclusive" means:**
- Each checked-out connection is exclusively owned by one thread/operation
- Connections cannot be shared concurrently (unlike multiplexed protocols)
- Connection returned to pool only after operation completes

### Lifecycle States

The pool uses a state machine with five states:

| State | Description | Can Checkout? | Can Pause? |
|-------|-------------|---------------|------------|
| Uninitialized | Initial state | No | No |
| Paused | Initialized but paused | No | N/A |
| Ready | Accepting connections | Yes | Yes |
| ReadyNonPausable | Accepting connections (load-balanced) | Yes | No |
| Disposed | Shut down | No | No |

**State Transitions:**
Managed by `PoolState` class with static transition matrix enforcing valid transitions.

**Typical Flow:**
```
Uninitialized → Initialize() → Paused → SetReady() → Ready → Dispose() → Disposed
```

### Key Components

**1. Connection Holder** (`ListConnectionHolder`)

Maintains two lists:
- **Available connections**: Ready for checkout (protected by `_lock`)
- **In-use connections**: Currently checked out (protected by `_lockInUse`)

**LIFO Strategy:**
Uses Last-In-First-Out for better CPU cache locality - most recently used connection likely still in cache.

**2. Semaphores** (`SemaphoreSlimSignalable`)

Controls concurrent access:
- `_maxConnectionsQueue`: Limits total pool size (max concurrent checked-out connections)
- `_maxConnectingQueue`: Limits concurrent connection establishment (default: 2)

**Signalable Variant:**
Custom semaphore that can interrupt waiting threads when connections become available (not just on timeout).

**3. Service States** (`ServiceStates`)

Tracks per-service generations for load-balanced clusters:
- Each service (mongos behind load balancer) has independent generation counter
- Used to detect stale connections
- Automatically cleaned up when all connections for a service are closed

## Connection Lifecycle

### Connection Creation

**Flow (ConnectionCreator helper):**

1. **Acquire connecting semaphore** - Wait for slot (limits concurrent creates)
2. **Create PooledConnection** - Wraps `BinaryConnection` with pool metadata
3. **Open connection** - TCP handshake, authentication, handshake
4. **Track in-use** - Add to in-use connections list
5. **Register service** - Increment service state counter if load-balanced

**Three-Layer Wrapping:**

```
BinaryConnection                 # Physical TCP connection
    ↓
PooledConnection                 # Adds pool generation tracking
    ↓
AcquiredConnection              # Reference-counted handle returned to users
```

**BinaryConnection** (`src/MongoDB.Driver/Core/Connections/BinaryConnection.cs`)
- Physical TCP connection
- Handles wire protocol
- Tracks idle time and lifetime

**PooledConnection** (in ExclusiveConnectionPool.Helpers.cs)
- Wraps BinaryConnection
- Adds pool generation (for invalidation)
- Implements expiration logic

**AcquiredConnection** (in ExclusiveConnectionPool.Helpers.cs)
- Reference-counted wrapper
- Supports `Fork()` for sharing connection across multiple handles
- Automatically returns to pool when reference count reaches zero

### Connection Checkout

**AcquireConnection Process:**

1. **Check pool state** - Must be Ready or ReadyNonPausable
2. **Acquire wait queue slot** - Check WaitQueueSize limit (deprecated feature)
3. **Wait for pool capacity** - `_maxConnectionsQueue.WaitSignaled(timeout)`
   - `Entered`: Capacity available
   - `Signaled`: Pool cleared (connection available signal)
   - `TimedOut`: Timeout exceeded → throw
4. **Get or create connection:**
   - Try `_connectionHolder.Acquire()` for existing connection
   - If none available, wait on `_maxConnectingQueue`:
     - `Signaled`: Another thread returned connection, try acquire again
     - `Entered`: Create new connection
     - `TimedOut`: Throw timeout exception
5. **Open connection** if newly created
6. **Wrap in AcquiredConnection** with reference counting
7. **Publish CheckedOut event**

**Timeout Handling:**
- Uses `OperationContext.RemainingTimeoutOrDefault()` for CSOT (Client Side Operation Timeout)
- Recalculates remaining timeout after each wait
- Enhanced error messages for load-balanced clusters

### Connection Checkin

**ReleaseConnection Process:**

1. **Publish check-in events**
2. **Decrement checkout reason counter** (tracks cursors/transactions)
3. **Check if connection should be returned:**
   - If expired or pool disposed: Remove and dispose connection
   - Otherwise: Return to `_connectionHolder` for reuse (LIFO list)
4. **Release semaphore** - Allow another checkout

**Reference Counting:**
When user disposes `AcquiredConnection`, decrements reference count. When count reaches zero, triggers ReleaseConnection.

### Connection Expiration

**PooledConnection.IsExpired:**
Connection is expired if any:
- Pool generation mismatch (created before Clear() call)
- Connection marked as failed (network error)
- BinaryConnection expired (idle or lifetime exceeded)

**BinaryConnection Expiration Checks:**

```csharp
// MaxLifeTime: Default 30 minutes - connection has been alive too long
if (settings.MaxLifeTime > 0 && now > openedAt + MaxLifeTime)
    return true;

// MaxIdleTime: Default 10 minutes - connection has been idle too long
if (settings.MaxIdleTime > 0 && now > lastUsedAt + MaxIdleTime)
    return true;
```

**Maintenance Thread:**

**MaintenanceHelper** (`src/MongoDB.Driver/Core/ConnectionPools/MaintenanceHelper.cs`)

Background thread runs every `MaintenanceInterval` (default: 1 minute):
- **Prunes expired connections** from available pool
- **Ensures minimum pool size** by creating connections if needed
- Cancellation-aware for graceful shutdown

## Configuration

### ConnectionPoolSettings

**Location:** `src/MongoDB.Driver/Core/Configuration/ConnectionPoolSettings.cs`

| Setting | Default | Description |
|---------|---------|-------------|
| MaxConnections | 100 | Maximum connections in pool (hard limit) |
| MinConnections | 0 | Minimum connections to maintain |
| MaxConnecting | 2 | Maximum concurrent connection establishments |
| WaitQueueTimeout | 2 minutes | Maximum time to wait for available connection |
| WaitQueueSize | 500 | (Deprecated) Max waiters for connections |
| MaintenanceInterval | 1 minute | Frequency of background maintenance |

**Configuration Example:**
```csharp
var settings = MongoClientSettings.FromConnectionString(connectionString);
settings.MaxConnectionPoolSize = 200;
settings.MinConnectionPoolSize = 10;
settings.WaitQueueTimeout = TimeSpan.FromSeconds(30);

var client = new MongoClient(settings);
```

### ConnectionSettings

**Location:** `src/MongoDB.Driver/Core/Configuration/ConnectionSettings.cs`

| Setting | Default | Description |
|---------|---------|-------------|
| MaxLifeTime | 30 minutes | Maximum time a connection can exist |
| MaxIdleTime | 10 minutes | Maximum time a connection can be idle |
| ApplicationName | null | Application identifier sent in handshake |
| Compressors | empty | Wire protocol compression options |

### Pool Metrics

**Available at Runtime:**
- `AvailableCount`: MaxConnections - connections in use
- `CreatedCount`: Total connections created (used + dormant)
- `DormantCount`: Connections available in pool
- `UsedCount`: Connections currently checked out
- `PendingCount`: Connections being established

## Error Handling and Health

### Exception Handling During Checkout

**ConnectionCreator** handles all exceptions during creation:
- Calls `_connectionExceptionHandler.HandleExceptionOnOpen(ex)`
- Exception handler (implemented by Server) decides if pool should be cleared
- Connection automatically disposed on creation failure
- Authentication failures, network errors trigger pool clearing

### Connection Failure Detection

**BinaryConnection.ConnectionFailed**

Called on any I/O exception during send/receive:
- Transitions connection state to Failed
- Publishes `ConnectionFailedEvent`
- Failed connections marked as expired
- Removed on next checkin

**Network Error Wrapping:**
- Wraps non-Mongo exceptions in `MongoConnectionException`
- Preserves timeout exceptions when CSOT configured
- Never wraps: ThreadAbortException, StackOverflowException, MongoAuthenticationException, OutOfMemoryException

### Health Check Mechanisms

**Passive Health Checks:**

1. **Expiration on acquire** - Checks `IsExpired` before returning connection
2. **Idle time tracking** - Updates `lastUsedAt` after every send/receive
3. **Connection state validation** - All operations check state is not Failed or Disposed

**Active Monitoring:**

- Maintenance thread prunes expired connections every MaintenanceInterval
- Generation-based invalidation on pool Clear() operations

### Error Backpressure

**SystemOverloadedError Handling:**

Network errors during handshake get special error labels:
- `SystemOverloadedError` label added for:
  - Timeout exceptions
  - IOException (network failures)
- `RetryableError` label added
- Signals server connection rate limiter to back off

## Thread Safety

### Synchronization Primitives

**1. Pool-Level Locks**
- `_poolState` lock protects state transitions
- All state changes must hold this lock

**2. Connection Holder Locks**
- `_lock` - Protects available connections list
- `_lockInUse` - Protects in-use connections list (separate to avoid contention)
- LIFO access pattern minimizes lock duration

**3. Semaphore-Based Flow Control**
- `SemaphoreSlimSignalable` provides thread-safe capacity limiting
- Atomic signal/reset operations via internal lock
- Supports interruption via cancellation tokens

**4. Atomic Counters**
- CheckOutReasonCounter uses Interlocked for cursor/transaction tracking
- ReferenceCounted uses Interlocked for reference counting
- ServiceStates uses locks around dictionary access but atomic generation increment

### Lock-Free Patterns

**Generation Tracking:**
- Pool generation is simple int field - reads don't require locking
- Connections cache their generation at creation
- Avoids repeated lookups

**Connection State:**
- `InterlockedInt32` wrapper for BinaryConnection state
- `TryChange` operations are atomic compare-and-swap

### Concurrency Design Decisions

**1. Connection Acquisition Strategy:**
- Multiple threads can wait on semaphores concurrently
- Only one thread at a time creates connection (MaxConnecting limit)
- Available connections returned immediately without waiting

**2. LIFO vs FIFO:**
- LIFO (last-in-first-out) for better CPU cache locality
- Most recently used connection likely still in CPU cache
- Better for hot connections pattern

**3. Signal/Wait Pattern:**
- When connection returned, signals waiting threads
- Allows early wake-up rather than waiting for timeout
- Special handling to avoid resuming on signal thread

### Race Condition Handling

**1. Double-Checked Expiration:**
- Check expiration after acquiring lock
- Check again after acquiring from holder
- Prevents returning expired connection due to races

**2. Disposal Safety:**
- All operations check `_poolState.IsDisposed` before proceeding
- Cleanup operations handle already-disposed gracefully
- CancellationTokenSource disposal wrapped in try-catch

**3. Open Once Pattern:**
- Connection open uses `_openLock` with task-based coordination
- Multiple threads can call Open(), but only first actually opens
- Others wait on shared task completion

## Connection Pool Events

All events published via `EventLogger` for monitoring:

| Event | Description |
|-------|-------------|
| ConnectionPoolOpenedEvent | Pool initialized |
| ConnectionPoolClosedEvent | Pool disposed |
| ConnectionPoolClearedEvent | Pool cleared (generation incremented) |
| ConnectionCreatedEvent | New connection created |
| ConnectionOpenedEvent | Connection handshake completed |
| ConnectionClosedEvent | Connection disposed |
| ConnectionCheckOutStartedEvent | Checkout attempt started |
| ConnectionCheckOutFailedEvent | Checkout failed |
| ConnectionCheckedOutEvent | Connection successfully checked out |
| ConnectionCheckedInEvent | Connection returned to pool |

**Subscribing to Events:**
```csharp
var settings = MongoClientSettings.FromConnectionString(connectionString);
settings.ClusterConfigurator = builder =>
{
    builder.Subscribe<ConnectionPoolClearedEvent>(e =>
    {
        Console.WriteLine($"Pool cleared for {e.ServerId}");
    });
};
```

## Best Practices

### Recommended Settings

**High-Throughput Applications:**
```csharp
MaxConnectionPoolSize = 200;
MinConnectionPoolSize = 50;
WaitQueueTimeout = TimeSpan.FromSeconds(10);
```

**Low-Latency Applications:**
```csharp
MaxConnectionPoolSize = 100;
MinConnectionPoolSize = 25;
MaxIdleTime = TimeSpan.FromMinutes(5);
```

**Serverless/FaaS:**
```csharp
MaxConnectionPoolSize = 10;  // Lower to reduce cost
MinConnectionPoolSize = 0;   // No idle connections
MaxIdleTime = TimeSpan.FromMinutes(1);
```

### Common Issues

**Issue: TimeoutException during checkout**

Causes:
- Pool exhausted (all connections in use)
- Slow operations holding connections too long
- MaxConnectionPoolSize too low

Solutions:
- Increase MaxConnectionPoolSize
- Optimize slow operations
- Implement operation timeouts
- Check for connection leaks (not disposing cursors)

**Issue: Too many connections to server**

Causes:
- Multiple MongoClient instances (each has own pools)
- MaxConnectionPoolSize too high
- MinConnectionPoolSize too high

Solutions:
- Use singleton MongoClient
- Lower MaxConnectionPoolSize
- Set MinConnectionPoolSize = 0

**Issue: Connection errors after network issues**

Causes:
- Stale connections in pool
- Generation not properly incremented

Solutions:
- Pool should automatically clear on network errors
- Check SDAM events for proper invalidation
- Reduce MaxIdleTime to expire stale connections faster

### Performance Tips

1. **Reuse MongoClient** - Never create per-request, use singleton
2. **Configure pool size** based on workload:
   - Formula: `MaxConnectionPoolSize ≥ max concurrent operations`
3. **Set MinConnectionPoolSize** for latency-sensitive apps (pre-warms connections)
4. **Dispose cursors** properly to return connections quickly
5. **Use async methods** for better concurrency
6. **Monitor pool metrics** via events
7. **Adjust timeouts** based on operation characteristics

## Debugging Connection Pool Issues

### Enable Logging

```csharp
var settings = MongoClientSettings.FromConnectionString(connectionString);
settings.LoggingSettings = new LoggingSettings(loggerFactory);
```

Look for log categories:
- `MongoDB.ConnectionPool`
- `MongoDB.Connection`

### Monitor Events

Subscribe to connection pool events to track:
- Pool clears
- Checkout failures
- Connection creation rate
- Average checkout time

### Check Pool Metrics

```csharp
// Access via reflection or custom monitoring
var pool = GetConnectionPool(server); // Implementation-specific
var metrics = new
{
    AvailableCount = pool.AvailableCount,
    UsedCount = pool.UsedCount,
    DormantCount = pool.DormantCount,
    CreatedCount = pool.CreatedCount
};
```

### Common Diagnostic Patterns

**Pattern 1: Connection leak detection**
```csharp
// Monitor UsedCount over time
// If it grows without bound → leak (cursors not disposed)
```

**Pattern 2: Pool exhaustion detection**
```csharp
// Monitor checkout failures and WaitQueueTimeout errors
// High rate → increase MaxConnectionPoolSize or optimize operations
```

**Pattern 3: Stale connection detection**
```csharp
// Monitor ConnectionClosedEvent with reason "Stale"
// High rate → network instability or firewall dropping connections
```

## Summary

The MongoDB C# driver's connection pooling provides:

- **Efficient resource utilization** through LIFO caching and background maintenance
- **Thread-safe operations** using fine-grained locking and atomic operations
- **Robust error handling** with network failure detection and automatic recovery
- **Flexible configuration** supporting diverse workloads
- **Observable behavior** through comprehensive event instrumentation
- **Per-server isolation** with independent pools per MongoDB server

The architecture effectively separates concerns: ExclusiveConnectionPool manages pool logic, BinaryConnection handles wire protocol, and helper classes encapsulate complex operations like checkout and connection creation.
