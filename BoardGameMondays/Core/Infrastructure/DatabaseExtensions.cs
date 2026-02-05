using BoardGameMondays.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardGameMondays.Core.Infrastructure;

/// <summary>
/// Extension methods for IDbContextFactory to reduce boilerplate across service layers.
/// Centralizes the common pattern of creating, using, and disposing DbContext instances.
/// </summary>
public static class DatabaseExtensions
{
    /// <summary>
    /// Executes an async action within a properly scoped and disposed DbContext.
    /// Reduces "await using var db = await _dbFactory.CreateDbContextAsync(ct);" boilerplate.
    /// </summary>
    /// <typeparam name="TResult">The return type of the operation.</typeparam>
    /// <param name="factory">The DbContext factory.</param>
    /// <param name="action">The async action to execute with the DbContext.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    public static async Task<TResult> ExecuteInDbContextAsync<TResult>(
        this IDbContextFactory<ApplicationDbContext> factory,
        Func<ApplicationDbContext, CancellationToken, Task<TResult>> action,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await action(db, ct);
    }

    /// <summary>
    /// Executes an async action within a properly scoped and disposed DbContext (void return).
    /// </summary>
    /// <param name="factory">The DbContext factory.</param>
    /// <param name="action">The async action to execute with the DbContext.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task ExecuteInDbContextAsync(
        this IDbContextFactory<ApplicationDbContext> factory,
        Func<ApplicationDbContext, CancellationToken, Task> action,
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await action(db, ct);
    }
}
