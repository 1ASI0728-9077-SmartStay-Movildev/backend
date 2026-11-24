namespace SmartStay.E2E.Tests.Infrastructure.Helpers;

/// <summary>
/// Asynchronous polling utility with configurable timeout and exponential backoff.
/// Used for awaiting asynchronous worker state transitions, outbox dispatch, and reconciliation.
/// </summary>
public static class PollingHelper
{
    /// <summary>
    /// Polls an asynchronous operation until the result satisfies the predicate or timeout expires.
    /// </summary>
    /// <typeparam name="T">Type of queried state.</typeparam>
    /// <param name="query">Function returning current state.</param>
    /// <param name="predicate">Condition indicating desired terminal or expected state.</param>
    /// <param name="timeout">Maximum duration to poll before throwing TimeoutException.</param>
    /// <param name="initialDelay">Initial delay before first retry (default 200ms).</param>
    /// <param name="maxInterval">Maximum delay between poll attempts (default 3s).</param>
    /// <param name="backoffFactor">Multiplier for exponential backoff (default 1.5).</param>
    /// <param name="description">Descriptive text included in exception if timed out.</param>
    /// <returns>The state satisfying the predicate.</returns>
    public static async Task<T> PollUntilAsync<T>(
        Func<Task<T?>> query,
        Func<T, bool> predicate,
        TimeSpan timeout,
        TimeSpan? initialDelay = null,
        TimeSpan? maxInterval = null,
        double backoffFactor = 1.5,
        string? description = null)
    {
        var delay = initialDelay ?? TimeSpan.FromMilliseconds(200);
        var maxDelay = maxInterval ?? TimeSpan.FromSeconds(3);
        var deadline = DateTime.UtcNow.Add(timeout);
        T? lastResult = default;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                lastResult = await query();
                if (lastResult != null && predicate(lastResult))
                {
                    return lastResult;
                }
            }
            catch
            {
                // Transient query exceptions ignored while polling
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            var currentWait = delay < remaining ? delay : remaining;
            await Task.Delay(currentWait);

            var nextDelayMs = delay.TotalMilliseconds * backoffFactor;
            delay = TimeSpan.FromMilliseconds(Math.Min(nextDelayMs, maxDelay.TotalMilliseconds));
        }

        var desc = description != null ? $" ({description})" : string.Empty;
        throw new TimeoutException($"Polling timed out after {timeout.TotalSeconds}s{desc}. Last observed value: {lastResult}");
    }

    /// <summary>
    /// Polls a boolean condition until true or timeout.
    /// </summary>
    public static async Task PollUntilConditionAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan? interval = null,
        string? description = null)
    {
        await PollUntilAsync<bool>(
            async () => await condition(),
            result => result,
            timeout,
            initialDelay: interval ?? TimeSpan.FromMilliseconds(300),
            maxInterval: interval ?? TimeSpan.FromMilliseconds(500),
            backoffFactor: 1.0,
            description: description
        );
    }
}
