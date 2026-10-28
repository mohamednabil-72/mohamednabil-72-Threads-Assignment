# Assignment 04 — Answers

Student name: Mohamed Nabil
Group / ID: 3

## Why Expected and Actual disagree

Multiple threads update the Views counter at the same time.

Views++ is not an atomic operation, which causes a race condition.

Some increments are lost, so the actual value can be less than the expected value.

## What you changed

Replaced Views++ with Interlocked.Increment(ref Views).

This makes the increment operation atomic and thread-safe.

The actual result now matches the expected value of 400000.

## When would you protect the update differently

I would use lock if I needed to update multiple shared variables together.

I would also use lock if the critical section contained multiple operations that must execute together.

Interlocked is better for simple atomic operations like incrementing a counter.
