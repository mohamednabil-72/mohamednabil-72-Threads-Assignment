# Order Processing Mini Project

## Overview

A .NET 10 console application that demonstrates asynchronous file-based data access, concurrent order processing, payment and email integrations, cancellation, fault handling, shipping quotes, and CPU-bound parallel processing.

The application uses JSON files as its storage layer. External services are represented by interfaces and passed through constructors using manual dependency injection. No DI container is used.

## Requirements

- .NET 10 SDK.
- A terminal opened in the project directory.
- The project targets `net10.0`.
- The entry point is `static async Task Main(string[] args)`.
- Supported modes: `phase1`, `phase2`, `phase3`, `phase4`, and `all`.
- Running without arguments opens the numbered menu.

Check the installed SDK:

```powershell
dotnet --list-sdks
```

Build the project:

```powershell
dotnet build
```

## How to run

Run each phase from the project directory:

```powershell
dotnet run -- phase1
dotnet run -- phase2
dotnet run -- phase3
dotnet run -- phase4
dotnet run -- all
```

Run a phase with automatic cancellation after a specified number of milliseconds:

```powershell
dotnet run -- phase4 --cancel-after 1000
```

Run a phase and save its console output to a log file in PowerShell:

```powershell
dotnet run -- phase1 *> phase1-log.txt
dotnet run -- phase2 *> phase2-log.txt
dotnet run -- phase3 *> phase3-log.txt
dotnet run -- phase4 *> phase4-log.txt
```

The `Data` directory is created under the application's current working directory. Run the commands from the project directory so the generated `Data` folder is easy to locate. Phase runners reset or seed data as required by their demonstrations; do not treat the generated data as permanent input.

## Project structure and class responsibilities

The solution is separated by phase and responsibility. The names below describe the required responsibilities; the source files in the project remain the definitive reference for the exact class inventory.

```text
OrderProcessing/
├── Shared/
│   ├── Models/
│   ├── Storage/
│   ├── Logging/
│   └── Exceptions/
├── Phase1/
├── Phase2/
├── Phase3/
├── Phase4/
├── Data/                 # generated at runtime
├── sample-output/        # final JSON outputs for each phase
├── screenshots/          # console evidence for the submission
└── README.md
```

### Shared

- **Models** — customer, product, coupon, order, payment, email, shipping quote, dashboard, and report data structures.
- **Storage** — JSON file access, seeding, repositories, file paths, concurrency protection, and persistence.
- **Logging** — timestamped console output that includes the thread ID and the operation being performed.
- **Exceptions** — specific exceptions for expected domain and service failures, including `CustomerNotFoundException`, `InvalidCouponException`, `ShippingProviderUnavailableException`, `DatabaseWriteException`, `SmtpUnavailableException`, and `DataFileCorruptedException`.

### Phase 1

- **Synchronous repositories and validator** — provide the synchronous baseline using synchronous file methods and `Thread.Sleep`.
- **Asynchronous repositories and validator** — use asynchronous methods and `await` for simulated I/O and file access.
- **Phase1Runner** — resets and seeds data, validates the 20 incoming orders sequentially in both versions, times the runs, and reads the final status counts back from `orders.json`.

### Phase 2

- **PricingService / OrderPricer** — calculates subtotal, loyalty and coupon discount, shipping, and final total. The concurrent version overlaps discount and shipping calls after subtotal calculation.
- **RaceConditionDemo** — demonstrates an unsafe stock reservation and then the safe reservation.
- **OrderProcessorV2** — processes validated orders concurrently, compensates for failures, and persists each result.
- **InvoiceGenerator** — simulates CPU-bound invoice rendering and writes an invoice text file.
- **ReportGenerator** — creates a report from persisted orders and products.
- **Phase2Runner** — coordinates the race demonstration, pricing comparison, concurrent processing, faulted-task demonstration, and report generation.

### Phase 3

- **CustomerService / InventoryService / PricingService** — handle customer lookup, atomic stock reservation and release, and order pricing.
- **PaymentGateway (`IPaymentGateway`)** — simulates authorization, capture, decline, timeout, and refund outcomes.
- **EmailService (`IEmailService`)** — simulates email delivery and configured SMTP failures.
- **OrderProcessor** — coordinates the checkout steps and compensating actions, including releasing stock or refunding a captured payment when needed.
- **SyncOrderProcessor and synchronous services** — provide the ten-thread comparison.
- **Phase3Runner** — runs the checkout workflow, retry and fire-and-forget demonstrations, and sync-versus-async comparison.

### Phase 4

- **Live checkout progress** — starts all 20 checkout tasks and reports each completion as it occurs.
- **CustomerDashboardService** — loads customer profile, orders, payments, and emails concurrently and reports multiple load failures.
- **ShippingQuoteService / IShippingCarrier implementations** — query competing carriers, select the first successful quote, cancel losing requests, and enforce a time budget.
- **Fraud scoring** — compares sequential CPU work with parallel work across available CPU cores.
- **Email re-send** — sends 30 test emails with a maximum concurrency of four, using the chosen library's concurrency support.
- **Phase4Runner** — coordinates the scale-up demonstrations.

## Storage design

### JSON files

The storage layer uses these files:

| File | Purpose |
|---|---|
| `customers.json` | Customer profiles and active status |
| `products.json` | Product details, prices, weights, and stock quantities |
| `coupons.json` | Coupon codes, discounts, and expiry dates |
| `incoming_orders.json` | The 20 seeded orders awaiting processing |
| `orders.json` | Orders and their final processing status |
| `payments.json` | Captured, declined, and refunded payment transactions |
| `emails.json` | Successfully persisted email messages |
| `email_retry.json` | Emails that exhausted the configured retry attempts |
| `Data/invoices/` | Generated invoice text files |
| `Data/reports/` | Generated JSON reports |

JSON arrays are used for collection files. Property names use camel case, and enum values are serialized as strings. Missing files are treated as empty collections by the storage layer. Invalid JSON is reported through `DataFileCorruptedException` rather than being silently accepted.

### Concurrent access and file integrity

- Asynchronous file operations are awaited rather than synchronously blocking a thread.
- A per-file `SemaphoreSlim` serializes operations that would otherwise read and overwrite the same file concurrently.
- Updates are performed against the latest collection read under the file's synchronization mechanism, preventing concurrent writers from silently overwriting one another.
- Writes use a temporary file followed by replacement/move so readers do not normally observe a partially written JSON document.
- Safe stock reservation checks availability and updates stock as one protected operation. The unsafe demonstration intentionally omits that protection to show overselling.
- Stock is released when later checkout steps fail, and a payment is refunded if saving a completed order fails after capture.
- Cancellation tokens are passed to cancellable asynchronous operations. Cancellation is not recorded as a payment decline.
- The cancellation demonstration checks that the JSON files remain parseable after cancellation.

The file-level synchronization protects concurrent operations within this application process. It is not a substitute for a transactional database or a cross-process locking strategy.

## Pricing rules

The implementation follows these project rules:

- `UnitPrice` is copied from `products.json`.
- `SubTotal` is the sum of `UnitPrice × Quantity` for all order items.
- Loyalty discount: Bronze = 0%, Silver = 5%, Gold = 10%.
- Coupon discount is added to the loyalty discount, with the total discount percentage capped at 30%.
- `Discount = SubTotal × DiscountPercentage / 100`.
- `ShippingCost = 5.00 + 1.50 × total quantity + 2.00 × total weight in kg`; shipping is free when subtotal is at least 1000.
- `TotalAmount = SubTotal − Discount + ShippingCost`.
- Monetary values are rounded with `Math.Round(value, 2, MidpointRounding.AwayFromZero)`.

Required pricing checks from the specification:

| Order | Subtotal | Discount | Shipping | Total |
|---|---:|---:|---:|---:|
| #1 | 1225.00 | 245.00 | 0.00 | 980.00 |
| #2 | 800.00 | 40.00 | 7.30 | 767.30 |

## Performance measurements

All timings below are from the recorded console runs provided during development. Timings are environment-dependent and can vary between runs. `ThreadPool.ThreadCount` is the runtime's current ThreadPool thread count at the point measured; it is not the same as the number of distinct threads observed by one operation.

| Phase / task | Approach | Time | Distinct threads observed | `ThreadPool.ThreadCount` |
|---|---|---:|---:|---:|
| Phase 1 validation | Sync, sequential | 53.8 s | Not recorded in the summary | Not recorded in the summary |
| Phase 1 validation | Async, sequential awaits | 53.5 s | Not recorded in the summary | Not recorded in the summary |
| Phase 2 pricing, order #1 | Sequential | 4.27 s | 2 | 5 |
| Phase 2 pricing, order #1 | Concurrent discount + shipping | 3.29 s | 1 | 5 |
| Phase 2 processing | Concurrent validated-order processing | 9.43 s | 6 | 12 |
| Phase 3 comparison | Sync, 10 dedicated threads | 1.46 s | 10 | 4 |
| Phase 3 comparison | Async, 10 tasks | 9.23 s | 4 | 5 |
| Phase 4.1 | Live checkout for 20 orders | 16.90 s | Not printed as a summary value | Not printed as a summary value |
| Phase 4.2 | Customer #3 dashboard load | 0.79 s | 3 | 5 |
| Phase 4.3 | Shipping quote, order #1 | 0.861 s | 1 | 5 |
| Phase 4.3 | Shipping quote, order #2 | 1.212 s | 1 | 5 |
| Phase 4.3 | Shipping quote, order #3 | 0.774 s | 2 | 4 |
| Phase 4.3 | Shipping quote, order #4 | 1.208 s | 1 | 4 |
| Phase 4.3 | Shipping quote, order #5 | 0.591 s | 2 | 4 |
| Phase 4.4 A | Fraud scoring, sequential | 21.273 s | Not printed for sequential run | Not printed for sequential run |
| Phase 4.4 A | Fraud scoring, parallel | 7.668 s | 4 | 4 |
| Phase 4.4 B | Email re-send, 30 emails | 8.36 s | 5 | 5 |

### Interpreting the measurements

- Phase 1 times are nearly identical because both versions validate orders one after another. Async improves how threads are used while waiting; it does not make a sequential workflow concurrent.
- Phase 2 pricing improved from 4.27 s to 3.29 s because discount and shipping requests overlap.
- Phase 2 concurrent processing completed the work in 9.43 s in the recorded run.
- The Phase 3 sync comparison used ten dedicated threads and completed in 1.46 s; the async comparison completed in 9.23 s in this run. This measurement does not mean async is inherently slower: the simulated operations, scheduling, and amount of work affect the result. The async approach avoids dedicating a blocked thread to each I/O wait and is intended to scale better as the number of orders grows.
- The Phase 4 dashboard took 0.79 s for four concurrent loads, close to the slowest load rather than the sum of all four.
- Phase 4 shipping quote requests are time-bounded; the flat-rate fallback appears when no successful quote arrives within the budget.
- Fraud scoring is CPU-bound. Parallel scoring was about 2.77× faster on this four-core environment, and both runs found the same 1005 flagged IDs.
- Email re-send is I/O-bound. The recorded run took 8.36 s, close to the expected `30 / 4 × 1 second ≈ 7.5 seconds` plus simulated template, connection, persistence, and scheduling overhead. The observed maximum simultaneous sends was exactly four.

## Expected outcomes and failure handling

### Phase 1

The 20-order validation should produce 15 validated and 5 rejected orders:

- #4 — `Customer is inactive`
- #9 — `Insufficient stock for 'Wireless Mouse'`
- #12 — `Coupon 'OLD20' has expired`
- #15 — `Customer #99 not found`
- #18 — `Coupon 'FAKE50' does not exist`

The final `orders.json` should contain 20 records. The status counts are read back from the stored file.

### Phase 2

The race demonstration should show both unsafe reservations succeeding against the last keyboard, then show the safe version returning one `true` and one `false`, with stock at zero.

The concurrent processing run should leave 20 orders in `orders.json`: 13 Completed, 6 Rejected, and 1 Failed (#7, shipping provider unavailable). Either #10 or #11 may get the final keyboard; the other is rejected. The report includes total orders, status counts, revenue from completed orders, average order value, and top-selling product. The expected top-selling product is **USB-C Hub, 5 units**.

### Phase 3

The configured failures demonstrate the required compensation and retry behavior:

- Order #15 is rejected because customer #99 does not exist.
- Order #5 times out during payment and its stock is released; it is not treated as a normal card decline.
- Order #8 is declined by the payment gateway and its stock is released.
- Order #13 fails to save after payment capture; the payment is refunded, stock is released, and the order is marked Failed.
- Order #3's first two email attempts fail and the third succeeds.
- Order #17 exhausts four attempts and is placed in `email_retry.json`; this does not change the order's successful status.

The required end state is 20 orders: 11 Completed, 6 Rejected, 2 PaymentFailed, and 1 Failed. `payments.json` should contain 13 records: 11 Captured, 1 Declined (#8), and 1 Refunded (#13). `email_retry.json` should contain order #17 with four attempts and a last error. The sync/async comparison uses orders #1–#10 with failures disabled and expects 8 Completed and 2 Rejected (#4 and #9) for both versions.

### Phase 4

- Live checkout processes 20 orders and prints each completion in completion order. The recorded status counts were 11 Completed, 6 Rejected, 2 PaymentFailed, and 1 Failed.
- Dashboard for customer #3 returns two orders (#3 Completed and #13 Failed), two payments, and one email. Customer #99 demonstrates both `Customer #99 not found` and `Payments store timeout`.
- Shipping quotes query all three carriers concurrently, cancel losing carriers when a quote is selected, use a flat rate when the time budget expires, and demonstrate the all-carriers-failed exception.
- Fraud scoring must return exactly 1005 flagged orders in both modes; the first five IDs are 4, 8, 12, 29, and 33.
- Email re-send must save each of the 30 test emails (IDs 1001–1030) exactly once and never exceed four simultaneous sends.

## Answers to the phase questions

### Phase 1

**1. Why are sync and async times almost the same when orders are awaited one after another?**

Because the orders are still processed sequentially. Each order waits for the previous order to finish. Async can release a thread during I/O waits, but it does not overlap the orders unless work is started concurrently.

**2. Is the thread ID before an `await` always the same as after it? Why?**

No. When an awaited operation is incomplete, the continuation may resume on a different ThreadPool thread. The log includes thread IDs so this can be observed in the run output. An `await` does not promise a fixed thread.

**3. What does a thread do during `Task.Delay()` versus `Thread.Sleep()`?**

`Task.Delay()` asynchronously waits without keeping a thread blocked for the delay. `Thread.Sleep()` blocks the current thread for the duration, so that thread cannot do other work during the sleep.

**4. Why should a repository method return `Task<T>` instead of `T`?**

`Task<T>` represents a result that becomes available asynchronously. Callers can `await` it without blocking a thread while I/O is pending, and can compose multiple asynchronous operations.

**5. What is the difference between `File.ReadAllText` and `File.ReadAllTextAsync`? Does the async version occupy a thread while the disk is reading?**

`File.ReadAllText` is synchronous and blocks the calling thread until the read completes. `File.ReadAllTextAsync` returns a task that can be awaited. Async file I/O is designed not to block a worker thread for the entire wait, although the exact underlying implementation depends on the platform and runtime.

### Phase 2

**1. What is the difference between starting a task and awaiting it?**

Starting a task schedules or begins the represented operation. `await` asynchronously waits for its result and propagates its exception or cancellation. If tasks are started and immediately awaited one at a time, their work does not overlap in the intended way.

**2. Does every `Task` create a new thread? Does `Task.Delay()` use a thread while waiting?**

No. A task represents an operation, not a thread. `Task.Delay()` uses a timer-based asynchronous wait and does not need to occupy a thread for the whole delay.

**3. What happens if `.Result` or `.Wait()` is used on an unfinished task instead of `await`? Why is it dangerous?**

The calling thread blocks. This wastes threads and can cause deadlocks when a continuation needs a blocked context or thread. `await` suspends the asynchronous method without synchronously blocking the caller. The specification allows `.Result` / `.Wait()` only in the separate demonstration method.

**4. What happens to an exception thrown inside a task that is never awaited?**

The exception is stored in the faulted task. If the task is never observed, the caller may not handle the failure at the point it occurs, and the operation may appear to have succeeded. Await tasks whose outcome matters and explicitly observe background-operation failures.

**5. When should `Task.Run()` be used, and when should it be avoided?**

Use `Task.Run()` to move CPU-bound work, such as the invoice-rendering loop, off the current thread. Avoid wrapping naturally asynchronous I/O calls in `Task.Run()`; await their asynchronous APIs directly.

**6. What is the difference between `Task` and `Task<T>`?**

`Task` represents an asynchronous operation with no result value. `Task<T>` represents an asynchronous operation that produces a value of type `T`.

**7. Why can a `lock` block not contain `await`?**

A C# `lock` requires synchronous entry and exit, and the lock must be released on the same thread that acquired it. An awaited continuation may resume on another thread, so `await` is not allowed inside a `lock` block. Use an asynchronous coordination primitive such as `SemaphoreSlim.WaitAsync` when asynchronous mutual exclusion is needed.

**8. Why did the unprotected stock reservation oversell, and how was it prevented?**

Both operations could read the same stock value before either wrote its update. Each then believed the last unit was available. The safe reservation protects the read-check-update sequence so only one operation can reserve the final unit; the other receives `false`.

### Phase 3

**1. Does async make one order's workflow faster?**

Not necessarily. Async does not eliminate the latency of an individual external call. It allows the application to make progress on other orders while an order waits for file, database, payment, or email I/O.

**2. Why does async scale better for many orders?**

I/O waits do not need to block one dedicated thread per order. The application can keep many operations in flight while using fewer threads than a design that creates a dedicated blocking thread for every order.

**3. What happens to the synchronous version with 1,000 orders?**

A dedicated-thread-per-order design would require a very large number of threads, increasing memory use, context switching, scheduling overhead, and the risk of thread-resource exhaustion. A bounded or asynchronous design is more suitable for large I/O-heavy workloads.

**4. Why is async essential for applications that call databases, files, and APIs?**

Those operations spend much of their time waiting for external resources. Async allows the application to release threads during those waits, improve throughput, and remain responsive under concurrent load.

**5. What is the problem with fire-and-forget email, and how is it fixed?**

If the email task is not awaited, the method can report completion before the email finishes. The caller may exit before delivery completes, and exceptions from the email operation may go unobserved. Awaiting the email makes completion and failures part of the method's observable result.

**6. When is `await` necessary, when can fire-and-forget be acceptable, and why is `async void` dangerous?**

Await an operation when its result, completion, or exception matters to the caller. Fire-and-forget is only appropriate when the work is deliberately independent, its lifetime is managed elsewhere, and failures are explicitly observed and logged. `async void` cannot be awaited and its exceptions cannot be caught by a caller in the usual task-based way. It is acceptable mainly for event handlers because event-handler delegate signatures often require `void`; other asynchronous methods should generally return `Task` or `Task<T>`.

### Phase 4

**1. For each of Tasks 10.1–10.4, which tool is appropriate, and why can't “start, then await one by one” meet the requirement?**

- **10.1 Live checkout progress:** use a completion-order mechanism such as `Task.WhenAny` over the active tasks or a completion queue. Awaiting tasks in start order delays reporting a completed later order until earlier tasks finish.
- **10.2 Customer dashboard:** start all four loads and use `Task.WhenAll`. It waits for all loads together, and the task collection can be inspected to report every failure. Awaiting each load one by one makes total latency closer to the sum of the individual loads.
- **10.3 Fastest shipping quote:** race the carrier tasks and timeout using a first-completion/first-success strategy, with cancellation tokens linked to the caller and time budget. Awaiting carriers one by one can miss the first successful quote and exceed the time budget. Cancel losing carriers as soon as a winner is chosen.
- **10.4 A Fraud scoring:** use CPU parallelism, such as `Parallel.For` / `Parallel.ForEach` with the cancellation token and a degree of parallelism no greater than `Environment.ProcessorCount`. Sequential awaiting does not spread CPU-bound calculations over cores.
- **10.4 B Email re-send:** use a concurrency-limited asynchronous library operation such as `Parallel.ForEachAsync` with a maximum degree of parallelism of four. This provides up to four overlapping I/O sends without manually creating threads or implementing the limit with a semaphore. Starting every send without a concurrency limit can exceed four; awaiting one at a time fails to reach the required concurrency.

**2. In 10.2, when several loads fail, how do you get every exception? What does a plain `await` give you?**

Retain the task returned by `Task.WhenAll`, then inspect the faulted tasks or the aggregate task's `Exception` to enumerate all failures. A plain `await` on the aggregate task normally throws one of the underlying exceptions to the awaiting code; it does not automatically print every exception.

**3. In 10.3, what happens to losing carriers if they are never cancelled?**

They continue consuming time and resources until they finish or fail, even though their results are no longer needed. Cancellation requests let the losing operations stop promptly and prevent unnecessary work.

**4. Why can 10.4 A get faster with more CPU cores, while 10.4 B does not depend on core count?**

Fraud scoring is CPU-bound, so multiple cores can perform calculations at the same time. Email sending is I/O-bound: most of each send is waiting for simulated SMTP operations, so overlapping a limited number of sends is more important than adding CPU cores.

**5. What is the difference between parallel work (10.4 A) and concurrent asynchronous work (10.1)?**

Parallel work executes CPU instructions at the same time on multiple cores. Concurrent asynchronous work keeps multiple operations in progress and switches among them while they wait for I/O; it does not require them to execute CPU instructions simultaneously.

**6. What happened in the cancelled run?**

The recorded command was `dotnet run -- phase4 --cancel-after 1000`. Cancellation occurred while incoming orders were being loaded, before any order finished processing: the output reported `0 orders finished, 0 cancelled`. Therefore this run did not demonstrate any completed order, stock reservation, or payment operation. The program then reported that every data file remained valid JSON. Since cancellation occurred early, this run proves cancellation handling and JSON integrity, but it does not by itself demonstrate rollback after stock reservation or payment capture.

## Problems found and solutions

- **Race condition / overselling:** two reservations could read the same stock before either update was written. The safe reservation serializes the critical read-check-update operation and the demo verifies one success and one failure.
- **Concurrent JSON writes:** overlapping read-modify-write operations can lose updates. Per-file asynchronous synchronization and temporary-file replacement protect the file updates within the process.
- **Faulted tasks:** a task can fail after it has been started. Tasks are observed and their exceptions are handled so one failed order does not stop the remaining batch.
- **Payment timeout:** the payment operation uses a cancellation-aware timeout. On timeout the order is marked `PaymentFailed` and reserved stock is released; cancellation of the whole run is not misclassified as a payment decline.
- **Failure after payment capture:** if saving the completed order fails, the workflow compensates by refunding the transaction, releasing stock, and saving the order as `Failed`.
- **Email retry failures:** retry attempts use increasing delays. After the fourth failed attempt, the message is stored in `email_retry.json`. Email failure does not change a successfully paid order's status.
- **Fire-and-forget email:** the broken example returned before email delivery finished. The fixed example awaits the email task so the method does not report completion early.
- **Shipping requests that finish after a winner:** linked cancellation is sent to the losing carriers; a time budget returns the flat-rate fallback when no successful quote arrives in time.
- **Cancellation and data integrity:** the cancelled-run demonstration checks that JSON files remain valid after cancellation. The recorded cancellation happened before any order finished, so it does not prove mid-transaction compensation.


