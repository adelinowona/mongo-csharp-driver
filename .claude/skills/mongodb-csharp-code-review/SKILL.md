---
name: mongodb-csharp-code-review
description: Comprehensive code review guidance for the MongoDB C# Driver library. Use when reviewing pull requests, conducting code reviews, analyzing C# code for the MongoDB driver, checking driver API design, or evaluating MongoDB C# driver contributions. Covers driver-specific patterns, performance, thread safety, async/await, BSON serialization, connection pooling, and testing standards.
---

# MongoDB C# Driver Code Review

Conduct thorough code reviews for the MongoDB C# Driver library with focus on driver-specific concerns, best practices, and maintaining API consistency.

## Review Workflow

Follow this sequential process:

### Phase 1: Understanding (Required First)
1. **Review JIRA ticket** - Read acceptance criteria, spec references, and requirements
2. **Understand the change** - Can you explain this code to someone else?
3. **Ask questions** - Clarify anything unclear before proceeding

**STOP if you don't fully understand. Request clarification from the author.**

### Phase 2: Correctness and Compliance
4. **Acceptance criteria** - Verify all criteria from the ticket are met
5. **Spec compliance** - Check implementation matches MongoDB specs (see `specifications/` directory)
6. **Testing verification** - Ensure tests match requirements (see [testing.md](references/testing.md))

### Phase 3: Technical Review
7. **API design** - Check public API consistency (see [api-design.md](references/api-design.md))
8. **Driver patterns** - Verify patterns are followed (see [patterns.md](references/patterns.md))
9. **Performance** - Assess performance implications (see [performance.md](references/performance.md))
10. **Impact analysis** - Consider adjacent functionality and race conditions

### Phase 4: Housekeeping
11. **Better alternatives** - Can you think of a more performant/maintainable approach?
12. **PR metadata** - Title format `CSHARP-XXXX: Description`, breaking changes documented
13. **Documentation** - XML docs complete, internal docs updated, external docs tracked in JIRA

## Review Checklist

Use this for every code review:

### Correctness
- [ ] I fully understand this implementation (can explain to others)
- [ ] Code meets all acceptance criteria from JIRA ticket
- [ ] Spec compliance verified (if applicable)
- [ ] All explicitly requested tests implemented
- [ ] Test names accurately describe what they assert

### Code Quality
- [ ] Public API changes maintain backward compatibility
- [ ] XML documentation complete: `<summary>`, `<param>`, `<returns>` (if non-void)
- [ ] Async methods use `ConfigureAwait(false)` and have `Async` suffix
- [ ] No `.Result` or `.Wait()` calls
- [ ] Thread safety requirements met for shared state
- [ ] BSON types and serialization are correct
- [ ] Error messages are clear and include context
- [ ] Follows existing driver patterns
- [ ] No better implementation comes to mind

### Testing
- [ ] Unit tests cover new/changed code
- [ ] Integration tests verify behavior with real MongoDB
- [ ] Spec tests implemented (if applicable)
- [ ] Adjacent functionality impact considered
- [ ] Race conditions considered and tested

### Housekeeping
- [ ] PR title: `CSHARP-XXXX: Description`
- [ ] Breaking changes documented in PR description
- [ ] New TODOs have JIRA tickets (`// TODO CSHARP-XXXX: ...`)
- [ ] Symbol visibility correct (prefer `internal` over `public`)
- [ ] Internal documentation updated
- [ ] External docs tracked in JIRA

### Performance
- [ ] No allocations in hot paths (or justified)
- [ ] No boxing in hot paths
- [ ] Resources properly disposed (`using`/`await using`)
- [ ] Benchmark results included for optimizations

## Common Issues

**Connection Management:**
- MongoClient should be singleton (too many causes connection pool exhaustion)
- Cursors must be disposed
- Check connection string handling

**Performance:**
- N+1 query patterns
- Missing projections (fetching unnecessary fields)
- Indexes not considered

**Testing:**
- Tests must be deterministic (no timing dependencies)
- Tests must be isolated (no shared state between tests)

## References

- [api-design.md](references/api-design.md) - API design principles and naming conventions
- [patterns.md](references/patterns.md) - Driver implementation patterns (async/await, BSON, errors, Operations)
- [performance.md](references/performance.md) - Hot paths, allocations, benchmarking
- [testing.md](references/testing.md) - Testing standards and infrastructure
