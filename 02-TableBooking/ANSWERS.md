# Assignment 02 — Answers

Student name: Mohamed Nabil  
Group / ID: 3


## What is wrong

- Multiple threads can execute the `Book` method simultaneously.
- A race condition can occur because multiple threads may check `TablesLeft` before any thread updates it.
- This can cause more than five tables to be booked and incorrect counter values.

## How you fixed it

- Added a private lock object to synchronize access to the booking operation.
- Used `lock` to protect the availability check and the updates to `TablesLeft` and `Booked`.
- Ensured that only one thread can book a table at a time.
- Verified that the final result is `Booked=5, TablesLeft=0`.

