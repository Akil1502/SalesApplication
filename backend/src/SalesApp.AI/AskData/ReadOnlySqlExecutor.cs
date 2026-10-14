using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace SalesApp.AI.AskData;

/// <summary>How the UI and Excel export should format a result column.</summary>
public enum QueryColumnKind
{
    Text,
    Money,
    Count,
    Number,
    Date,
    Boolean,
}

public sealed record QueryColumn(string Name, QueryColumnKind Kind);

public sealed record QueryResult(IReadOnlyList<QueryColumn> Columns, IReadOnlyList<object?[]> Rows, bool Truncated, int DurationMs);

public interface IReadOnlySqlExecutor
{
    Task<QueryResult> RunAsync(string sql, string runAs, CancellationToken cancellationToken);
}

/// <summary>
/// Runs a checked query as one of the reporting-only database users. Each query gets its own unpooled connection
/// that is impersonated with EXECUTE AS USER ... WITH NO REVERT, so the query can't switch back to the app's own
/// (powerful) identity and the connection is never reused for anything else.
/// </summary>
public sealed partial class ReadOnlySqlExecutor(IConfiguration configuration) : IReadOnlySqlExecutor
{
    public const string Reader = "ai_reader";
    public const string ManagerReader = "ai_reader_mgr";
    public const int MaxRows = 500;
    public const int TimeoutSeconds = 15;

    /// <summary>At most two AI queries at a time, so a burst of questions can't load the database.</summary>
    private static readonly SemaphoreSlim Gate = new(2, 2);

    public async Task<QueryResult> RunAsync(string sql, string runAs, CancellationToken cancellationToken)
    {
        // The user name is spliced into SQL, so it must be one of the two known constants.
        if (runAs is not (Reader or ManagerReader))
        {
            throw new ArgumentOutOfRangeException(nameof(runAs), runAs, "Unknown reporting user.");
        }

        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken))
        {
            throw new TimeoutException("The database is busy with other questions. Try again in a moment.");
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(configuration.GetConnectionString("Default"))
            {
                Pooling = false,
                ApplicationName = "SalesApp ask-your-data",
                ConnectTimeout = 10,
            };
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var check = new SqlCommand("SELECT snapshot_isolation_state FROM sys.databases WHERE database_id = DB_ID()", connection))
            {
                var snapshot = Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
                await using var setup = new SqlCommand(
                    $"SET LOCK_TIMEOUT 5000; SET TRANSACTION ISOLATION LEVEL {(snapshot ? "SNAPSHOT" : "READ COMMITTED")}; EXECUTE AS USER = '{runAs}' WITH NO REVERT;",
                    connection);
                await setup.ExecuteNonQueryAsync(cancellationToken);
            }

            var started = Stopwatch.GetTimestamp();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = TimeoutSeconds };
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken);

            var columns = Enumerable.Range(0, reader.FieldCount)
                .Select(i => new QueryColumn(ColumnName(reader.GetName(i), i), Classify(reader.GetName(i), reader.GetFieldType(i))))
                .ToList();
            var rows = new List<object?[]>();
            var truncated = false;
            while (await reader.ReadAsync(cancellationToken))
            {
                if (rows.Count == MaxRows)
                {
                    truncated = true;
                    break;
                }

                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = ToJsonValue(reader.IsDBNull(i) ? null : reader.GetValue(i));
                }

                rows.Add(row);
            }

            return new QueryResult(columns, rows, truncated, (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string ColumnName(string name, int index) => string.IsNullOrWhiteSpace(name) ? $"Column{index + 1}" : name;

    [GeneratedRegex("(value|amount|sales|revenue|turnover|total|price|outstanding|margin|cost|paid|due|tax|gst|limit)", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyName();

    /// <summary>Money is told apart from other decimals by its column name, since the database only knows "decimal".</summary>
    internal static QueryColumnKind Classify(string name, Type type)
    {
        if (type == typeof(bool))
        {
            return QueryColumnKind.Boolean;
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly))
        {
            return QueryColumnKind.Date;
        }

        // Money is always decimal(18,2) in this database, so whole numbers are counts (invoices, days, customers).
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
        {
            return QueryColumnKind.Count;
        }

        if (type == typeof(decimal) || type == typeof(double) || type == typeof(float))
        {
            return MoneyName().IsMatch(name) && !name.Contains("rate", StringComparison.OrdinalIgnoreCase) ? QueryColumnKind.Money : QueryColumnKind.Number;
        }

        return QueryColumnKind.Text;
    }

    /// <summary>Values the JSON serializer and the browser handle well: dates without time become plain dates.</summary>
    internal static object? ToJsonValue(object? value) => value switch
    {
        null => null,
        DateTime date when date.TimeOfDay == TimeSpan.Zero => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString(),
        byte[] => "(binary)",
        TimeSpan time => time.ToString(),
        _ => value,
    };
}
