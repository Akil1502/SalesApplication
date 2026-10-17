using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SalesApp.Core.Common;
using SalesApp.Core.Entities;
using SalesApp.Infrastructure.Data;

namespace SalesApp.Infrastructure.Services;

public interface IUserSettingsService
{
    /// <summary>All of a person's settings, by key.</summary>
    Task<IReadOnlyDictionary<string, JsonElement>> ListAsync(Guid userId, CancellationToken cancellationToken);

    Task SaveAsync(Guid userId, string key, JsonElement value, CancellationToken cancellationToken);
    Task DeleteAsync(Guid userId, string key, CancellationToken cancellationToken);
}

/// <summary>
/// Each person's own settings. Only they can read or change them; there's nothing to audit (they're preferences, not
/// business records), and sizes are capped so a setting can't grow without bound.
/// </summary>
public sealed partial class UserSettingsService(AppDbContext db, TimeProvider time) : IUserSettingsService
{
    public const int MaxValueBytes = 32 * 1024;
    public const int MaxSettingsPerUser = 100;

    [GeneratedRegex("^[a-z][a-z0-9._-]{0,63}$")]
    private static partial Regex KeyPattern();

    public async Task<IReadOnlyDictionary<string, JsonElement>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.UserSettings.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new { s.Key, s.ValueJson })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Key, r => JsonDocument.Parse(r.ValueJson).RootElement.Clone(), StringComparer.Ordinal);
    }

    public async Task SaveAsync(Guid userId, string key, JsonElement value, CancellationToken cancellationToken)
    {
        CheckKey(key);
        var json = value.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > MaxValueBytes)
        {
            throw new ValidationException("value", $"A setting can be at most {MaxValueBytes / 1024} KB.");
        }

        var setting = await db.UserSettings.SingleOrDefaultAsync(s => s.UserId == userId && s.Key == key, cancellationToken);
        if (setting is null)
        {
            if (await db.UserSettings.CountAsync(s => s.UserId == userId, cancellationToken) >= MaxSettingsPerUser)
            {
                throw new ValidationException("key", $"You can keep at most {MaxSettingsPerUser} settings.");
            }

            setting = new UserSetting { UserId = userId, Key = key, ValueJson = json };
            db.UserSettings.Add(setting);
        }

        setting.ValueJson = json;
        setting.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (db.Entry(setting).State == EntityState.Added)
        {
            // The same key saved from two tabs at once: the other one won, and saving again overwrites it.
            db.Entry(setting).State = EntityState.Detached;
            await db.UserSettings.Where(s => s.UserId == userId && s.Key == key)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.ValueJson, json).SetProperty(s => s.UpdatedAtUtc, setting.UpdatedAtUtc), cancellationToken);
        }
    }

    public async Task DeleteAsync(Guid userId, string key, CancellationToken cancellationToken)
    {
        CheckKey(key);
        await db.UserSettings.Where(s => s.UserId == userId && s.Key == key).ExecuteDeleteAsync(cancellationToken);
    }

    private static void CheckKey(string key)
    {
        if (!KeyPattern().IsMatch(key))
        {
            throw new ValidationException("key", "A setting's name is lower-case letters, digits, '.', '_' or '-', up to 64 characters, starting with a letter.");
        }
    }
}
