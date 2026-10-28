# Assignment 03 — Answers

Student name:  Mohamed Nabil
Group / ID: 3

1. What is System.Threading.Monitor used for?

System.Threading.Monitor is used to synchronize threads and control access to shared resources. It allows only one thread at a time to enter a critical section protected by the same lock object.

It provides methods such as Enter, Exit, TryEnter, Wait, Pulse, and PulseAll.

2. What does the C# lock statement compile to?

The lock statement is implemented using monitor-based synchronization. Conceptually, it acquires a monitor lock before executing the critical section and releases it afterward.

The compiler uses try/finally-style protection to ensure the lock is released even if an exception occurs. The exact generated code depends on the C# version and target runtime.

3. List at least two differences between using lock and calling Monitor APIs directly.

Simplicity: lock has simpler syntax and automatically releases the lock. Direct Monitor.Enter and Monitor.Exit calls require careful handling, usually with try/finally.

Timeout support: Monitor.TryEnter allows a thread to attempt to acquire a lock for a limited time. A normal lock statement waits until the lock becomes available.



Thread coordination: Monitor.Wait, Monitor.Pulse, and Monitor.PulseAll allow threads to wait for and signal changes in shared state. A plain lock statement does not provide these operations directly.

4. When might a developer use Monitor.TryEnter, Monitor.Pulse, or Monitor.Wait instead of a plain lock?

Monitor.TryEnter: When a thread should attempt to acquire a lock without waiting indefinitely, such as when using a timeout or implementing a fallback action.

Monitor.Wait: When a thread must wait for a condition to become true. It releases the monitor while waiting and reacquires it before returning.

Monitor.Pulse: When a thread changes shared state and needs to notify one waiting thread that it may be able to proceed.

5. Write the same critical section twice: once with lock, once with Monitor.Enter / Monitor.Exit.

Using lock

private readonly object _lock = new object();
private int _counter = 0;

public void Increment()
{
    lock (_lock)
    {
        _counter++;
    }
}

Using Monitor.Enter / Monitor.Exit

using System.Threading;

private readonly object _lock = new object();
private int _counter = 0;

public void Increment()
{
    Monitor.Enter(_lock);
    try
    {
        _counter++;
    }
    finally
    {
        Monitor.Exit(_lock);
    }
}
Both examples protect the increment operation so that only one thread at a time can execute the critical section using the same lock object. The finally block ensures the monitor is released even if an exception occurs.

Links:

1. Microsoft Learn — Monitor Class
https://learn.microsoft.com/en-us/dotnet/api/system.threading.monitor

2. Microsoft Learn — Monitor.Enter Method
https://learn.microsoft.com/en-us/dotnet/api/system.threading.monitor.enter
