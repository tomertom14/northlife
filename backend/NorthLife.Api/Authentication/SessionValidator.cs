using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NorthLife.Api.Data;

namespace NorthLife.Api.Authentication;

public interface ISessionValidator
{
    Task<bool> IsCurrentAsync(Guid userId, string securityStamp, CancellationToken cancellationToken);
    void Invalidate(Guid userId);
}

/// <summary>
/// Makes stateless JWTs revocable: a token is accepted only while its security stamp matches the
/// account and the account is not suspended. Lookups are cached briefly; changes on this instance
/// evict the entry immediately, other instances see them within <see cref="CacheLifetime"/>.
/// </summary>
public sealed class SessionValidator(IServiceScopeFactory scopes, IMemoryCache cache) : ISessionValidator
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private sealed record SessionState(string SecurityStamp, bool Suspended);

    public async Task<bool> IsCurrentAsync(Guid userId, string securityStamp, CancellationToken cancellationToken)
    {
        var state = await cache.GetOrCreateAsync(Key(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            await using var scope = scopes.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await dbContext.Users
                .AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => new SessionState(user.SecurityStamp, user.SuspendedAtUtc != null))
                .SingleOrDefaultAsync(cancellationToken);
        });

        return state is not null &&
            !state.Suspended &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(state.SecurityStamp),
                Encoding.ASCII.GetBytes(securityStamp));
    }

    public void Invalidate(Guid userId) => cache.Remove(Key(userId));

    private static string Key(Guid userId) => $"session:{userId:N}";
}
