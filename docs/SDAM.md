# Server Discovery And Monitoring (SDAM)

This document explains how the MongoDB C# driver implements Server Discovery And Monitoring (SDAM) according to the MongoDB specification.

## Overview

SDAM is the driver's mechanism for:
- Discovering MongoDB servers in a cluster
- Monitoring their health and topology changes
- Selecting appropriate servers for operations
- Reacting to failures and elections

The implementation strictly follows the official [MongoDB SDAM specification](https://github.com/mongodb/specifications/blob/master/source/server-discovery-and-monitoring/server-discovery-and-monitoring.rst).

## Core Architecture

### Key Abstractions

**ICluster** (`src/MongoDB.Driver/Core/Clusters/ICluster.cs`)
- Top-level interface for cluster management
- Exposes: `ClusterId`, `Description`, `Settings`
- Internal `IClusterInternal` adds: `SelectServer`, `Initialize`, event handling

**IServer** (`src/MongoDB.Driver/Core/Servers/IServer.cs`)
- Represents a single MongoDB server
- `IClusterableServer` extends with: initialization, invalidation, heartbeat requests
- Manages its own `IConnectionPool`
- Tracks `OutstandingOperationsCount` for load balancing

### State Objects (Immutable)

**ClusterDescription** (`src/MongoDB.Driver/Core/Clusters/ClusterDescription.cs`)
- Immutable snapshot of entire cluster state
- Contains collection of `ServerDescription` objects (sorted by endpoint)
- Calculates derived properties: `LogicalSessionTimeout` (minimum across servers)
- Provides `With*` methods for creating modified copies
- Key properties:
  - `ClusterType`: Unknown, Standalone, ReplicaSet, Sharded, LoadBalanced
  - `ClusterState`: Connected or Disconnected
  - `DirectConnection`: Whether using direct connection mode
  - `Servers`: Collection of server descriptions

**ServerDescription** (`src/MongoDB.Driver/Core/Servers/ServerDescription.cs`)
- Immutable snapshot of single server state (1000+ lines)
- Rich metadata:
  - `ServerType`: Unknown, Standalone, ReplicaSetPrimary, ReplicaSetSecondary, ShardRouter, etc.
  - `ServerState`: Connected or Disconnected
  - `AverageRoundTripTime`: For latency-based selection
  - `ReplicaSetConfig`: Replica set name, election info
  - `TopologyVersion`: Prevents processing stale responses
  - `WireVersionRange`: Server capabilities
- `SdamEquals()`: Compares only spec-relevant fields for event publishing
- `IsDataBearing`: Identifies servers that can serve data
- `ReasonChanged`: Debugging information

## Cluster Implementations

### Three Cluster Types

**1. MultiServerCluster** (`src/MongoDB.Driver/Core/Clusters/MultiServerCluster.cs`)

Handles replica sets and sharded clusters with dynamic server management.

**SDAM Logic:**
- `ProcessReplicaSetChange()`: Validates membership, tracks elections, handles primary changes
- `ProcessShardedChange()`: Validates shard routers
- `ProcessStandaloneChange()`: Handles standalone discovery (only in SRV mode)

**Election Tracking:**
- Maintains `_maxElectionInfo` to detect stale primaries
- Compares `(setVersion, electionId)` tuples
- Invalidates old primaries when new primary discovered

**Server Management:**
- Dynamically adds servers from `hosts` field in hello response
- Removes servers not in primary's member list
- Removes servers with mismatched replica set names

**DNS SRV Support:**
- `DnsMonitor` for dynamic endpoint discovery
- Periodically resolves SRV records (default 60s, respects DNS TTL)
- Validates hosts are in parent domain
- Respects `srvMaxHosts` setting

**2. SingleServerCluster** (`src/MongoDB.Driver/Core/Clusters/SingleServerCluster.cs`)

Used for direct connections (`directConnection=true`):
- Simpler implementation - single server management
- Validates replica set name if specified
- No dynamic server discovery

**3. LoadBalancedCluster**

Specialized cluster for MongoDB load-balanced mode.

### ClusterFactory

**ClusterFactory** (`src/MongoDB.Driver/Core/Clusters/ClusterFactory.cs`)

Determines cluster type based on settings:
```
Decision tree: LoadBalanced → SingleServer (DirectConnection) → MultiServer
```

Detects external environments (CosmosDB, DocumentDB) and logs warnings.

## Server Discovery and Monitoring

### ServerMonitor - The Heartbeat Engine

**ServerMonitor** (`src/MongoDB.Driver/Core/Servers/ServerMonitor.cs`)

Core responsibility: Runs on dedicated background thread, continuously monitors server health.

**Two Monitoring Modes:**

1. **Polling Mode**: Sends hello/isMaster commands at regular intervals
2. **Streaming Mode**: Uses exhaustCursor with `topologyVersion` and `maxAwaitTimeMS` for awaitable hello
   - More efficient - server only responds when topology changes or timeout occurs
   - Reduces network traffic and latency

**Mode Selection:**
- Auto-detects FaaS environments (AWS Lambda, Azure Functions, GCP, Vercel)
- FaaS → Polling (connections may be paused/resumed)
- Normal → Streaming (more efficient)

**Heartbeat Flow:**
1. `InitializeConnection()`: Opens connection and sends initial hello
2. `GetHelloResult()`: Executes hello protocol with timeout
3. Updates `ServerDescription` based on response or exception
4. `SetDescription()`: Atomically updates description and fires events
5. `HeartbeatDelay`: Implements metronome-based timing (min 500ms interval)

**RTT Monitoring:**

**RoundTripTimeMonitor** (`src/MongoDB.Driver/Core/Servers/RoundTripTimeMonitor.cs`)
- Separate thread when streaming mode is active
- Maintains exponentially weighted moving average (EWMA with alpha=0.2)
- Used for latency-based server selection
- Formula: `newAverage = (alpha * measurement) + ((1 - alpha) * oldAverage)`

### Server Invalidation

**DefaultServer** (`src/MongoDB.Driver/Core/Servers/DefaultServer.cs`)

Handles exceptions and invalidates server state.

**Invalidation Triggers:**

**Before Handshake:**
- Authentication failures
- Network errors
- Connection pool errors
→ Invalidate server and clear pool

**After Handshake:**
- State change errors: NotPrimary, ShutdownInProgress, InterruptedAtShutdown
- Checked against TopologyVersion for staleness
- Stale errors ignored (already handled by newer heartbeat)
- Fresh errors trigger invalidation

**Coordination:**
- Uses `ServerMonitor.Lock` to prevent race conditions
- Requests immediate heartbeat after invalidation
- Updates connection pool generation

## Server Selection Process

### Selection Pipeline

**Cluster.SelectServer/SelectServerAsync** (`src/MongoDB.Driver/Core/Clusters/Cluster.cs`)

**Flow:**
1. **Begin Selection**: Logs event, constructs selector chain
2. **Selector Chain** (applied in order):
   - `PreServerSelector` (optional, user-defined)
   - Primary selector (e.g., `ReadPreferenceServerSelector`)
   - `PostServerSelector` (optional, user-defined)
   - `LatencyLimitingServerSelector` (filters by localThreshold)
   - `OperationsCountServerSelector` (load balancing by outstanding ops)
3. **Selection Loop**:
   - Apply selectors to current `ClusterDescription`
   - If server found: return wrapped as `SelectedServer`
   - If not found: enter `ServerSelectionWaitQueue`
   - Wait for cluster description to change (triggered by heartbeat updates)
   - Retry with updated description
4. **Timeout**: Throws `TimeoutException` with detailed cluster state

### Server Selectors

**IServerSelector** (`src/MongoDB.Driver/Core/Clusters/ServerSelectors/IServerSelector.cs`)

Simple interface:
```csharp
IEnumerable<ServerDescription> SelectServers(
    ClusterDescription cluster,
    IEnumerable<ServerDescription> servers)
```

**ReadPreferenceServerSelector** (`src/MongoDB.Driver/Core/Clusters/ServerSelectors/ReadPreferenceServerSelector.cs`)

Implements MongoDB read preference logic:

| Read Preference | Behavior |
|----------------|----------|
| Primary | Primary only |
| PrimaryPreferred | Primary if available, else secondary |
| Secondary | Secondary only |
| SecondaryPreferred | Secondary if available, else primary |
| Nearest | Any server (lowest latency) |

**Tag Set Matching:**
- Supports targeting specific servers: `{ "region": "us-east", "dc": "ny" }`
- Filters servers matching all tags in a tag set
- Tries tag sets in order until servers found

**MaxStaleness Support:**

Filters secondaries based on lastWriteTimestamp:

**With primary:**
```
staleness = (s.lastUpdate - s.lastWrite) - (p.lastUpdate - p.lastWrite) + s.heartbeatInterval
```

**Without primary:**
Uses secondary with most recent write as reference.

**Validation:**
- maxStaleness >= 90 seconds
- maxStaleness >= heartbeatInterval + 10 seconds

**Other Selectors:**
- `LatencyLimitingServerSelector`: Filters servers within localThreshold of fastest
- `CompositeServerSelector`: Chains multiple selectors
- `WritableServerSelector`: Filters to primary/mongos/standalone
- `EndPointServerSelector`: Selects specific endpoint

### Wait Queue Mechanism

**ServerSelectionWaitQueue** (nested in Cluster.cs)

Coordinates threads waiting for suitable servers:
- Tracks clients waiting when no suitable server available
- **Rapid Heartbeat**: When queue has waiters, triggers heartbeats at min 500ms intervals
- Respects `MaxServerSelectionWaitQueueSize` to prevent resource exhaustion
- Publishes `ClusterEnteredSelectionQueueEvent` for monitoring

## Cluster State Management

### Immutable State Updates

**UpdateClusterDescription Flow:**
1. Create new `ClusterDescription` with changes
2. Use `Interlocked.Exchange` to atomically swap `_expirableClusterDescription`
3. Fire `DescriptionChanged` event
4. Mark old description as expired (releases waiting threads)

**ExpirableClusterDescription** (nested class in Cluster.cs)
- Wraps `ClusterDescription` with expiration mechanism
- Lazy initialization of `ConnectedServers` list (filtered view)
- `Expired` Task used for wait queue synchronization

### Topology Changes

**MultiServerCluster Change Processing:**

**Entry Point:** `ServerDescriptionChangedHandler`
- Handles all server updates
- Validates server is still in cluster (handles removal race conditions)

**Cluster Type Discovery:**
Transitions from Unknown → ReplicaSet/Sharded/Standalone based on first response.

**Replica Set Specific:**
- Tracks replica set name, validates all members match
- Adds new members from `hosts` field in hello response
- Removes servers not in primary's member list
- Detects stale primaries via `(setVersion, electionId)` tuple comparison
- Invalidates old primaries when new primary discovered

**SDAM Event Publishing:**
Uses `SdamEquals()` to suppress duplicate events (only publishes when spec-relevant fields change).

### DNS SRV Monitoring

**DnsMonitor** (`src/MongoDB.Driver/Core/Clusters/DnsMonitor.cs`)

Background thread for `mongodb+srv://` connections:
- Periodically resolves SRV records (default 60s, respects DNS TTL)
- Validates hosts are in parent domain
- Dynamically adds/removes endpoints in MultiServerCluster
- Respects `srvMaxHosts` setting with Fisher-Yates shuffle for randomness
- Stops monitoring when cluster type becomes ReplicaSet or Sharded

## Event Flow and Notifications

### SDAM Events

**Key Event Types:**
- `ServerDescriptionChangedEvent`: Server state changed
- `ClusterDescriptionChangedEvent`: Cluster topology changed
- `ServerHeartbeatStartedEvent` / `SucceededEvent` / `FailedEvent`
- `ClusterAddingServerEvent` / `AddedServerEvent` / `RemovingServerEvent` / `RemovedServerEvent`
- `ClusterSelectingServerEvent` / `SelectedServerEvent` / `SelectingServerFailedEvent`
- `ClusterEnteredSelectionQueueEvent`

**Event Publishing Pattern:**
```csharp
_eventLogger.LogAndPublish(new SomeEvent(...))
```
- Logs to ILogger infrastructure
- Publishes to IEventSubscriber for custom handling

**Change Propagation:**
1. `ServerMonitor` detects change → fires `DescriptionChanged` event
2. `DefaultServer.OnMonitorDescriptionChanged()` → validates TopologyVersion, updates server state
3. `DefaultServer` fires `ServerDescriptionChangedEvent` → handled by cluster
4. `MultiServerCluster.ServerDescriptionChangedHandler()` → processes SDAM logic
5. `Cluster.UpdateClusterDescription()` → fires `ClusterDescriptionChangedEvent`
6. External subscribers receive notification

## MongoDB SDAM Specification Compliance

### Test Suite

**Location:** `specifications/server-discovery-and-monitoring/tests/`

100+ JSON test files validating spec compliance:
- Replica set topology changes
- Election scenarios
- Sharded cluster monitoring
- Standalone server handling
- Error handling with TopologyVersion
- Streaming protocol behavior
- Load balancing mode

### Key Spec Features Implemented

**1. TopologyVersion**

Prevents processing stale responses:
- `IsStalerThanOrEqualTo()` / `IsFresherThanOrEqualTo()` comparisons
- Used in error handling and heartbeat processing
- Format: `{ processId: ObjectId, counter: NumberLong }`

**2. ElectionId Priority**

- `Feature.ElectionIdPriorityInSDAM` feature flag
- Prioritizes electionId over setVersion for detecting stale primaries (wire version ≥ 17)
- Ensures correct primary detection during elections

**3. Connection Pool Management**

- Clear pool on server state changes
- Generation numbers to ignore stale errors
- `closeInUseConnections` flag for timeout errors

**4. Server Types (Enum)**

- Unknown
- Standalone
- ShardRouter
- ReplicaSetPrimary
- ReplicaSetSecondary
- ReplicaSetArbiter
- ReplicaSetOther
- ReplicaSetGhost
- LoadBalanced

**5. Cluster Types (Enum)**

- Unknown
- Standalone
- ReplicaSet
- Sharded
- LoadBalanced

**6. Streaming Protocol**

- Awaitable hello with exhaustCursor
- Auto-detection of FaaS environments for polling fallback
- `ServerMonitoringMode`: Auto, Stream, Poll

**7. Error Handling**

- State change exceptions: NotPrimary, InterruptedAtShutdown, etc.
- Network error vs timeout distinction
- Stale error detection via generation numbers
- SystemOverloadedError for connection rate limiting

## Architecture Diagram

```
MongoClient
    ↓
ICluster (MultiServerCluster / SingleServerCluster / LoadBalancedCluster)
    ↓
    ├── ClusterDescription (immutable state)
    ├── ServerSelectionWaitQueue (coordination)
    └── IClusterableServer[] (tracked servers)
            ↓
            ├── DefaultServer
            │       ├── ServerDescription (immutable state)
            │       ├── IConnectionPool
            │       └── IServerMonitor (ServerMonitor)
            │               ├── Background thread
            │               ├── Hello/IsMaster commands
            │               ├── Streaming/Polling mode
            │               └── IRoundTripTimeMonitor (optional)
            │
            └── Events → Cluster → Updates ClusterDescription → Notifies waiters
```

## Key Takeaways

1. **Thread-Safety**: Heavy use of immutable state with atomic swaps
2. **Event-Driven**: Changes propagate through event handlers
3. **Specification-First**: Code closely follows MongoDB SDAM spec with extensive test coverage
4. **Performance**: Streaming protocol reduces latency, RTT monitoring enables latency-aware selection
5. **Resilience**: TopologyVersion prevents stale updates, generation numbers prevent stale error handling
6. **Flexibility**: Support for replica sets, sharded clusters, standalone, load balanced, and DNS SRV discovery

## Working with SDAM

### Subscribing to SDAM Events

```csharp
var settings = MongoClientSettings.FromConnectionString(connectionString);
settings.ClusterConfigurator = builder =>
{
    builder.Subscribe<ServerDescriptionChangedEvent>(e =>
    {
        Console.WriteLine($"Server {e.NewDescription.EndPoint} changed from " +
                          $"{e.OldDescription.Type} to {e.NewDescription.Type}");
    });
};
var client = new MongoClient(settings);
```

### Custom Server Selection

```csharp
// Custom selector to prefer specific data center
public class DataCenterSelector : IServerSelector
{
    public IEnumerable<ServerDescription> SelectServers(
        ClusterDescription cluster,
        IEnumerable<ServerDescription> servers)
    {
        // Filter to servers in preferred DC
        var preferred = servers.Where(s =>
            s.Tags.Contains(new Tag("dc", "us-east")));
        return preferred.Any() ? preferred : servers;
    }
}
```

### Debugging SDAM Issues

Enable logging:
```csharp
var settings = MongoClientSettings.FromConnectionString(connectionString);
settings.LoggingSettings = new LoggingSettings(loggerFactory);
```

Look for these log categories:
- `MongoDB.ServerSelection`
- `MongoDB.SDAM`
- `MongoDB.Connection`

Check events:
- `ClusterDescriptionChangedEvent` - Topology changes
- `ServerDescriptionChangedEvent` - Individual server changes
- `ServerHeartbeatFailedEvent` - Monitoring failures
