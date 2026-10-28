# Assignment 01 — Answers

Student name:  Mohamed Nabil 
Group / ID: 3


## Problems with the current design

- Each pending order was loaded again using `GetOrderById`, even though all pending orders had already been loaded. This caused unnecessary database calls.
- Customer files were read repeatedly when multiple orders belonged to the same customer.
- The invoice output directory was created for every invoice instead of once when the writer was initialized.
- The export process performed unnecessary work that increased the total execution time when processing many orders.

## What you changed

- Removed the unnecessary `GetOrderById` call and used the orders returned by `GetPendingOrders` directly.
- Added a `Dictionary<int, CustomerProfile>` cache in `InvoiceExportService` to avoid reading the same customer's file more than once during an export.
- Moved `Directory.CreateDirectory` to the `InvoiceFileWriter` constructor so the output directory is created once.
- Removed the unused `GetOrderById` method from `OrderDatabase`.
- Kept the business flow unchanged: load orders, read customer profiles, and write invoices.
- Preserved the original invoice content. All five generated invoices were checked and matched the original invoices.
- Compared the execution time before and after the changes. The execution time decreased after removing redundant operations.
## Console output before changes

Export finished in 6.0 s

## Console output after changes

Export finished in 3.6 s

## Performance comparison

- Original execution time: 6.0 seconds.
- Updated execution time: 3.6 seconds.
- Time saved: 2.4 seconds.
- Execution time reduction: 40%.
