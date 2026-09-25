using Microsoft.Extensions.Caching.Memory;

namespace NorthLife.Api.Identity;

/// <summary>
/// Caps wrong second-factor codes per account. The "auth" rate limiter bounds requests per client
/// address, but an attacker who knows the password could spread guesses over many addresses; this
/// counter follows the account instead. After <see cref="MaxFailures"/> wrong codes the second factor
/// stays locked until the window that began with the first failure ends.
/// With three valid codes per check (the ±1 step window) out of 10^6, one window gives an attacker at
/// most 5 × 3 / 10^6 = 1.5 × 10^-5 chance of success.
/// </summary>
public sealed class SecondFactorThrottle(IMemoryCache cache)
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public bool IsLocked(Guid userId) =>
        cache.TryGetValue(Key(userId), out Counter? counter) && counter!.Failures >= MaxFailures;

    public void RecordFailure(Guid userId)
    {
        var counter = cache.GetOrCreate(Key(userId), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Window;
            return new Counter();
        })!;
        Interlocked.Increment(ref counter.Failures);
    }

    public void Reset(Guid userId) => cache.Remove(Key(userId));

    private static string Key(Guid userId) => $"second-factor-failures:{userId:N}";

    private sealed class Counter
    {
        public int Failures;
    }
}

public sealed class SecondFactorLockedException : Exception;
