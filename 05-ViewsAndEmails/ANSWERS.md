# Assignment 05 — Answers

Student name:Mohamed Nabil
Group / ID: 3

## Failures you observed

- Multiple threads accessed the `Dictionary` and `Queue` at the same time, causing race conditions.
- `Views++` could lose increments, resulting in an incorrect view count.
- Using `Count` before `Dequeue` could cause `InvalidOperationException` when multiple workers accessed the queue.

## What you changed

- Replaced `Dictionary` with `ConcurrentDictionary`.
- Replaced `Queue` with `ConcurrentQueue` and used `TryDequeue`.
- Replaced `List` with `ConcurrentBag` for storing sent emails.
- Used `AddOrUpdate` to safely increment view counts.
- Removed the lock because `ConcurrentBag` is thread-safe.

## Collection safety vs business-rule safety

- Concurrent collections make individual collection operations thread-safe.
- They do not automatically make multiple operations a single atomic operation.
- Business rules that involve multiple steps may still require `lock` or other synchronization.
