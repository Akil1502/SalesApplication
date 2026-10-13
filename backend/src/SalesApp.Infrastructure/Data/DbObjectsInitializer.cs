using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace SalesApp.Infrastructure.Data;

/// <summary>
/// Runs the idempotent SQL scripts embedded under Sql/ (reporting views, database users and grants) after
/// migrations. Views live in scripts rather than migrations so each one is defined in exactly one readable
/// place and re-applied on every start, instead of being scattered across migration history.
/// </summary>
public static partial class DbObjectsInitializer
{
    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BatchSeparator();

    public static async Task ApplyAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var assembly = typeof(DbObjectsInitializer).Assembly;
        var scripts = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".Sql.", StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

        foreach (var script in scripts)
        {
            foreach (var batch in BatchSeparator().Split(await ReadAsync(assembly, script, cancellationToken)))
            {
                if (!string.IsNullOrWhiteSpace(batch))
                {
#pragma warning disable EF1002 // Trusted SQL shipped inside this assembly, not user input.
                    await db.Database.ExecuteSqlRawAsync(batch, cancellationToken);
#pragma warning restore EF1002
                }
            }
        }
    }

    private static async Task<string> ReadAsync(Assembly assembly, string resource, CancellationToken cancellationToken)
    {
        await using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded SQL script {resource} is missing.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
