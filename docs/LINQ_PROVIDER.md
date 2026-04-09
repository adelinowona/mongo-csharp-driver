# LINQ Provider Deep Dive

This document explains how the MongoDB C# driver's LINQ provider (Linq3Implementation) works internally.

## Overview

The LINQ provider translates C# LINQ expressions into MongoDB aggregation pipelines. It follows a multi-stage compiler pattern:

```
C# LINQ Expression → Preprocessing → Pipeline Translation → AST Construction → Optimization → Execution
```

## Architecture Components

### 1. Entry Points

**MongoQueryProvider** (`src/MongoDB.Driver/Linq/Linq3Implementation/MongoQueryProvider.cs`)
- Implements `IQueryProvider`, the standard LINQ interface
- Entry point for all LINQ query execution
- Routes queries through the translation pipeline

**MongoQuery<TDocument>** (`src/MongoDB.Driver/Linq/Linq3Implementation/MongoQuery.cs`)
- Represents a queryable MongoDB collection
- Defers execution until enumeration (lazy evaluation)
- Created via `collection.AsQueryable()`

### 2. Expression Preprocessing

**LinqExpressionPreprocessor** (`src/MongoDB.Driver/Linq/Linq3Implementation/Misc/LinqExpressionPreprocessor.cs`)

Applies two critical transformations before translation:

1. **CLR Compatibility Rewriting** - Handles platform-specific expression differences
2. **Partial Evaluation** - Evaluates constant subexpressions
   - Example: `collection.Where(x => x.Age > myVariable)` evaluates `myVariable` to its value before translation
   - This produces `{ Age: { $gt: 25 } }` instead of trying to reference the C# variable

### 3. Translation Context

**TranslationContext** (`src/MongoDB.Driver/Linq/Linq3Implementation/Translators/TranslationContext.cs`)

Maintains state during translation:
- **SymbolTable** - Maps C# lambda parameters to MongoDB variables (`$$ROOT`, `$$var_0`, etc.)
- **NameGenerator** - Creates unique MongoDB variable names
- **TranslationOptions** - Configuration (e.g., client-side projections enabled)

**Symbol System** (`Misc/Symbol.cs`, `Misc/SymbolTable.cs`)
- Maps C# parameters (e.g., lambda parameter `x`) to MongoDB AST expressions
- Tracks serializers for proper BSON conversion
- Maintains "current" symbol concept representing `$$ROOT` or `$$CURRENT`

### 4. AST (Abstract Syntax Tree) Layer

The AST provides a type-safe representation of MongoDB's aggregation pipeline.

**Key AST Classes** (`src/MongoDB.Driver/Linq/Linq3Implementation/Ast/`)

| Class | Purpose | Example |
|-------|---------|---------|
| `AstExpression` | Aggregation expressions | `$add`, `$concat`, `$cond` |
| `AstFilter` | Query operators | `$eq`, `$gt`, `$and` |
| `AstStage` | Pipeline stages | `$match`, `$project`, `$group` |
| `AstPipeline` | Ordered list of stages | Full pipeline |

**AstExpression Features:**
- Factory methods: `Add()`, `And()`, `GetField()` provide fluent API
- Constant folding optimizations: `Add(0, x)` → `x`, `And(true, x)` → `x`
- Implicit conversions from primitive types to constants
- `Render()` method converts to BsonValue for MongoDB

### 5. Translator Layer

#### Pipeline Translation

**ExpressionToPipelineTranslator** (`Translators/ExpressionToPipelineTranslators/ExpressionToPipelineTranslator.cs`)

Top-level dispatcher that routes LINQ method calls to specialized translators:

| LINQ Method | Translator | MongoDB Stage |
|------------|------------|---------------|
| `Where` | WhereMethodToPipelineTranslator | `$match` |
| `Select` | SelectMethodToPipelineTranslator | `$project` |
| `GroupBy` | GroupByMethodToPipelineTranslator | `$group` |
| `OrderBy/ThenBy` | OrderByMethodToPipelineTranslator | `$sort` |
| `Join` | JoinMethodToPipelineTranslator | `$lookup` |
| `SelectMany` | SelectManyMethodToPipelineTranslator | `$unwind` + stages |

**TranslatedPipeline** (`Translators/ExpressionToPipelineTranslators/TranslatedPipeline.cs`)
- Immutable container for AST pipeline + output serializer
- Methods like `AddStage()`, `ReplaceLastStage()` return new instances
- Tracks serializer to ensure type safety through transformations

#### Expression Translation

**ExpressionToAggregationExpressionTranslator** (`Translators/ExpressionToAggregationExpressionTranslators/`)

Translates C# expressions to MongoDB aggregation expressions. Dispatches based on ExpressionType:

| C# Expression | Translator | MongoDB Expression |
|--------------|------------|-------------------|
| `x + y` | BinaryExpressionTranslator | `{ $add: [x, y] }` |
| `x.Method()` | MethodCallExpressionTranslator | Various operators |
| `x.Property` | MemberExpressionTranslator | `{ $getField: "Property" }` |
| `42` | ConstantExpressionTranslator | `42` |

**Method Translators** (`ExpressionToAggregationExpressionTranslators/MethodTranslators/`)

60+ specialized translators for specific methods:
- String: `Contains`, `StartsWith`, `Substring`, `ToLower` → `$regexMatch`, `$substr`, `$toLower`
- Math: `Abs`, `Ceiling`, `Floor`, `Sqrt` → `$abs`, `$ceil`, `$floor`, `$sqrt`
- Array: `Count`, `Sum`, `Average`, `Select` → `$size`, `$sum`, `$avg`, `$map`
- Date: `AddDays`, `Year`, `Month` → `$dateAdd`, `$year`, `$month`

Example:
```csharp
// C#: array.Select(x => x.ToUpper())
// MongoDB: { $map: { input: <array>, as: "var_0", in: { $toUpper: "$$var_0" } } }
```

#### Filter Translation

**ExpressionToFilterTranslator** (`Translators/ExpressionToFilterTranslators/ExpressionToFilterTranslator.cs`)

Two-phase translation strategy:

1. **Query Operators** (preferred): Uses efficient MongoDB query operators
   - `x.Age > 18` → `{ Age: { $gt: 18 } }`
2. **Aggregation Operators** (fallback): Uses `$expr` with aggregation expressions
   - Complex expressions that can't use query operators
   - Less efficient but more powerful

### 6. Executable Query Layer

**ExpressionToExecutableQueryTranslator** (`Translators/ExpressionToExecutableQueryTranslators/`)

Handles terminal operations:
- **Cursor-returning**: `ToList()`, `ToArray()`, iteration
- **Scalar-returning**: `First()`, `Count()`, `Any()`, `Sum()`, `Average()`

**ExecutableQuery** (`Translators/ExpressionToExecutableQueryTranslators/ExecutableQuery.cs`)

Encapsulates:
- The optimized pipeline
- The collection/database to query
- Options (timeouts, collation, etc.)
- Finalizer (post-processing logic)

Executes via `IMongoCollection.Aggregate()` API, rendering AST to BsonDocument array.

**Finalizers** handle post-processing:
- `FirstFinalizer` - Gets first document or throws
- `CountFinalizer` - Counts documents in cursor
- `SingleFinalizer` - Ensures exactly one result

### 7. Serialization Integration

**Serializer Tracking** (`src/MongoDB.Driver/Linq/Linq3Implementation/Serializers/`)

Every `TranslatedExpression` carries its `IBsonSerializer` to ensure correct BSON encoding/decoding.

Special serializers:
- `IGroupingSerializer` - Serializes `IGrouping<TKey, TElement>` from GroupBy
- `WrappedValueSerializer` - Wraps single values: `{ _v: 42 }`
- `NestedAsQueryableSerializer` - Handles nested LINQ queries
- `IEnumerableSerializer` - Serializes collections

**ProjectionHelper** (`Misc/ProjectionHelper.cs`)
- Creates projection stages with proper serializers
- Handles include/exclude and computed projections

## Translation Process: Example

Let's trace: `collection.Where(x => x.Age > 18).Select(x => x.Name)`

### Step 1: Preprocessing
```
LinqExpressionPreprocessor.Preprocess()
  → PartialEvaluator evaluates constants
  → ClrCompatExpressionRewriter fixes platform differences
```

### Step 2: Pipeline Translation
```
ExpressionToPipelineTranslator.Translate()
  → Recognizes "Select" MethodCallExpression
    → Delegates to SelectMethodToPipelineTranslator
      → Translates source: Where(x => x.Age > 18)
        → Delegates to WhereMethodToPipelineTranslator
          → Translates predicate to filter: { Age: { $gt: 18 } }
          → Adds $match stage
      → Translates selector: x => x.Name
        → Result: { $getField: "Name" }
      → Creates $project stage: { Name: 1, _id: 0 }
```

### Step 3: AST Construction
```
Pipeline:
  [
    { $match: { Age: { $gt: 18 } } },
    { $project: { Name: 1, _id: 0 } }
  ]
```

### Step 4: Optimization
```
AstPipelineOptimizer.Optimize()
  → Combines adjacent stages when possible
  → Removes redundant operations
```

### Step 5: Execution
```
ExecutableQuery.Execute()
  → Renders AST to BsonDocument[]
  → Calls collection.Aggregate(pipeline)
  → Returns IAsyncCursor<string>
```

## Common Patterns

### Pattern 1: Symbol Management
```csharp
// Create a symbol for lambda parameter
var symbol = context.CreateSymbol(parameter, serializer);
var lambdaContext = context.WithSymbol(symbol);

// Translate body with new context
var body = Translate(lambdaContext, lambda.Body);
```

### Pattern 2: Recursive Pipeline Building
```csharp
// Translate source first
var pipeline = ExpressionToPipelineTranslator.Translate(context, sourceExpression);

// Translate operation
var filter = ExpressionToFilterTranslator.TranslateLambda(...);

// Add stage (creates new immutable pipeline)
pipeline = pipeline.AddStage(AstStage.Match(filter), serializer);
```

### Pattern 3: Two-Phase Filter Translation
```csharp
try {
    // Try query operators first (more efficient)
    filter = TranslateUsingQueryOperators(context, expression);
} catch (ExpressionNotSupportedException) {
    // Fall back to $expr with aggregation operators
    filter = TranslateUsingAggregationOperators(context, expression);
}
```

### Pattern 4: Wrapped Value Handling
```csharp
// Many operations wrap single values in documents
// { _v: <value> } with WrappedValueSerializer
if (serializer is IWrappedValueSerializer wrapped) {
    var unwrapped = GetField(ast, wrapped.FieldName);
    return new TranslatedExpression(expr, unwrapped, wrapped.ValueSerializer);
}
```

## Important Implementation Details

### Client-Side vs Server-Side Projections
- Some projections can't be translated to MongoDB (e.g., calling external methods)
- `ClientSideProjectionHelper` detects these cases
- When `EnableClientSideProjections` is true, driver downloads documents and projects in memory
- Default behavior throws `ExpressionNotSupportedException`

### Identity Projection Optimization
```csharp
// Select(x => x) is detected and eliminated
if (selectorLambda.Body == selectorLambda.Parameters[0]) {
    return pipeline; // no $project stage added
}
```

### Discriminator Handling
- When using polymorphic types, MongoDB stores discriminator fields
- `DiscriminatorAstFilter` and `DiscriminatorAstExpression` handle these automatically
- `OfType<T>()` translates to filters on discriminator values

### Constant Folding in AST
```csharp
// AstExpression factory methods perform constant folding
Add(2, 3) → Constant(5)
Add(0, x) → x
And(true, x) → x
```

### Safe Field Names
- Field names containing dots or starting with $ require special handling
- `AstGetFieldExpression.HasSafeFieldName()` checks validity
- Unsafe names use `$getField` operator instead of dot notation

### Partial Evaluation
- Captured variables are evaluated before translation
- Example: `var min = 18; collection.Where(x => x.Age > min)`
  - `min` is evaluated to `18` before translation
  - Results in `{ Age: { $gt: 18 } }` not `{ Age: { $gt: $$min } }`

## Directory Structure

```
Linq3Implementation/
├── Ast/                                    # MongoDB AST representation
│   ├── Expressions/                        # Aggregation expressions ($add, $concat, etc.)
│   ├── Filters/                            # Query filters ($eq, $gt, $and, etc.)
│   ├── Stages/                             # Pipeline stages ($match, $project, $group, etc.)
│   ├── Optimizers/                         # AST optimization passes
│   └── Visitors/                           # AST traversal support
├── Translators/
│   ├── ExpressionToPipelineTranslators/    # LINQ methods → Pipeline stages
│   ├── ExpressionToAggregationExpressionTranslators/  # C# expressions → MongoDB expressions
│   │   ├── MethodTranslators/              # Specific method translations
│   │   └── PropertyTranslators/            # Property access translations
│   ├── ExpressionToFilterTranslators/      # C# expressions → Query filters
│   ├── ExpressionToExecutableQueryTranslators/  # Terminal operations (First, Count, etc.)
│   └── ExpressionToSetStageTranslators/    # $set stage translations
├── Serializers/                            # LINQ-specific serializers
├── Misc/                                   # Utilities (Symbol, ProjectionHelper, etc.)
└── Reflection/                             # Reflection helpers for method matching
```

## Working with the LINQ Provider

### Adding a New Method Translator

1. Create translator in `ExpressionToAggregationExpressionTranslators/MethodTranslators/`
2. Implement `IMethodToAggregationExpressionTranslator` interface
3. Register in `MethodToAggregationExpressionTranslatorResolver.cs`
4. Add tests in `MongoDB.Driver.Tests/Linq/Linq3Implementation/`

### Debugging Translation

Add logging or use debugger to inspect:
- `TranslationContext.SymbolTable` - Current variable mappings
- `TranslatedPipeline.Stages` - Pipeline stages being built
- `AstNode.Render()` - See MongoDB representation at any point

### Understanding Translation Failures

`ExpressionNotSupportedException` is thrown when:
- Expression uses unsupported C# features
- Method has no translator
- Expression requires client-side evaluation

Check exception message for specific expression that failed, then look for corresponding translator.
