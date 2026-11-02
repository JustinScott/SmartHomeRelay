namespace SmartHomeRelay.Core.Tests.TestSupport;

/// <summary>
/// Waits for background loops to reach a state. Each check yields to let other tasks run; the 5s budget
/// only exists so a broken test fails instead of hanging. Tests never sleep for fixed durations.
/// </summary>
internal static class Eventually
{
    private static readonly TimeSpan s_budget = TimeSpan.FromSeconds(5);

    public static async Task TrueAsync(Func<bool> condition, string because, Action? betweenChecks = null)
    {
        var deadline = DateTime.UtcNow + s_budget;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting until {because}.");
            }

            betweenChecks?.Invoke();
            await Task.Yield();
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
    }
}
