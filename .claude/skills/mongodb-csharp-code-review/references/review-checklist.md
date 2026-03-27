# Code Review Guide

How to conduct effective code reviews for the MongoDB C# Driver.

## Before You Approve

**Ask yourself:**
- Can I explain how this code works to someone else?
- Do I understand the purpose of each significant change?
- Have I asked questions about anything unclear?

**If you answer "no" to any of these, request clarification.**

## Spec Compliance

When there's an associated MongoDB specification:
- Review the relevant spec in `specifications/` directory
- Verify implementation matches spec requirements exactly
- Check that any deviations are documented and justified

**For spec tests:**
- Were all relevant spec tests pulled from `specifications/`?
- Are spec test inputs correctly interpreted?
- Do prose test scenarios have corresponding tests?

## Impact Analysis

Consider what else might be affected:
- **CRUD operations**: Does this change affect insert/update/delete/find?
- **Connection pooling**: Could this impact connection management?
- **Serialization**: Does this affect BSON serialization/deserialization?
- **Server selection**: Could this impact topology or server selection?
- **Async/sync duality**: If async changed, is sync version updated too?

**Race conditions to check:**
- Is shared state properly synchronized?
- Are there time-of-check-time-of-use (TOCTOU) issues?
- Could async operations interleave incorrectly?

## Anti-Patterns to Avoid

### Rubber-Stamp Approval
- "Looks good to me" without reading the code
- Assuming tests pass means code is correct
- Not asking questions when something is unclear

### Scope Creep
**Don't request:**
- Refactoring unrelated code
- Features not in acceptance criteria
- Style changes to untouched code

**Do request:**
- Changes directly related to the PR's purpose
- Fixes for bugs introduced by the PR
- Tests for functionality added by the PR

### Bikeshedding
**Don't focus on:**
- Variable naming preferences (unless truly confusing)
- Formatting (should be handled by formatters)
- Personal style preferences

**Do focus on:**
- Correctness and maintainability
- Performance implications
- API design consistency
- Test coverage

### Not Checking the Ticket
Always review the JIRA ticket:
- Acceptance criteria might explain design decisions
- Spec requirements might justify complexity
- Performance targets might explain optimizations
