namespace SmartStay.E2E.Tests.Infrastructure.Helpers;

/// <summary>
/// Helper facilitating high-concurrency race condition testing using thread barriers.
/// Coordinates multiple workers (e.g. 20+ simultaneous threads) to dispatch requests at the exact same millisecond.
/// Uses an asynchronous barrier pattern (TaskCompletionSource + Interlocked.Increment) to avoid ThreadPool thread starvation.
/// </summary>
public static class ConcurrencyTestHelper
{
    /// <summary>
    /// Executes a concurrent workload across a specified number of workers using an asynchronous synchronization barrier.
    /// </summary>
    /// <typeparam name="T">Result type of each worker.</typeparam>
    /// <param name="workerCount">Number of simultaneous workers (e.g. 20, 50, 100).</param>
    /// <param name="workerAction">Async function executed by each worker receiving workerId (0..workerCount-1).</param>
    /// <param name="timeout">Optional maximum timeout for the entire batch.</param>
    /// <returns>Array of results from all workers.</returns>
    public static async Task<T[]> ExecuteConcurrentBarrierAsync<T>(
        int workerCount,
        Func<int, Task<T>> workerAction,
        TimeSpan? timeout = null)
    {
        if (workerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workerCount), "Worker count must be greater than zero.");
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;

        var tasks = Enumerable.Range(0, workerCount).Select(async workerId =>
        {
            // Signal arrival; once the final worker arrives, release all workers simultaneously
            if (Interlocked.Increment(ref arrived) == workerCount)
            {
                tcs.TrySetResult();
            }
            await tcs.Task;
            return await workerAction(workerId);
        }).ToArray();

        var aggregateTask = Task.WhenAll(tasks);
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);

        var completed = await Task.WhenAny(aggregateTask, Task.Delay(effectiveTimeout));
        if (completed != aggregateTask)
        {
            throw new TimeoutException($"Concurrent barrier execution timed out after {effectiveTimeout.TotalSeconds} seconds for {workerCount} workers.");
        }

        return await aggregateTask;
    }

    /// <summary>
    /// Executes a concurrent void-returning workload across workers using an asynchronous synchronization barrier.
    /// </summary>
    public static async Task ExecuteConcurrentBarrierAsync(
        int workerCount,
        Func<int, Task> workerAction,
        TimeSpan? timeout = null)
    {
        await ExecuteConcurrentBarrierAsync(workerCount, async id =>
        {
            await workerAction(id);
            return true;
        }, timeout);
    }
}
