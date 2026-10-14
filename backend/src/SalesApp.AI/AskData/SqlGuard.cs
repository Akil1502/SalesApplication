using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SalesApp.AI.AskData;

public sealed record SqlCheckResult(bool Allowed, string? Problem)
{
    public static readonly SqlCheckResult Ok = new(true, null);

    public static SqlCheckResult Reject(string problem) => new(false, problem);
}

/// <summary>
/// The first line of defence for AI-written SQL: parses it with Microsoft's T-SQL parser and accepts exactly one
/// SELECT that reads only the allowed reporting schemas, using built-in functions from an allow-list. Everything
/// else is refused with a message Claude can act on. The database user the query runs as is the second line: even a
/// query that got past this check could read nothing but the reporting views.
/// </summary>
public static class SqlGuard
{
    public const int MaxLength = 6000;

    /// <summary>Aggregate, window, date, text, maths and logic functions: what reporting questions need, nothing that
    /// reaches outside the query (no metadata, session, security or system functions).</summary>
    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Aggregates
        "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP", "STRING_AGG", "APPROX_COUNT_DISTINCT",
        // Window and ranking
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE", "PERCENT_RANK", "CUME_DIST",
        "PERCENTILE_CONT", "PERCENTILE_DISC",
        // Dates
        "GETDATE", "SYSDATETIME", "SYSDATETIMEOFFSET", "GETUTCDATE", "SYSUTCDATETIME", "DATEADD", "DATEDIFF", "DATEDIFF_BIG",
        "DATEPART", "DATENAME", "DATEFROMPARTS", "DATETIMEFROMPARTS", "DATETRUNC", "DATE_BUCKET", "EOMONTH", "YEAR", "MONTH",
        "DAY", "ISDATE", "SWITCHOFFSET", "TODATETIMEOFFSET",
        // Text
        "LEN", "LOWER", "UPPER", "LTRIM", "RTRIM", "TRIM", "SUBSTRING", "REPLACE", "CONCAT", "CONCAT_WS", "CHARINDEX", "PATINDEX",
        "FORMAT", "STR", "REPLICATE", "REVERSE", "STUFF", "TRANSLATE", "SPACE", "CHAR", "NCHAR", "ASCII", "UNICODE", "SOUNDEX", "DIFFERENCE",
        // Maths
        "ABS", "ROUND", "CEILING", "FLOOR", "POWER", "SQRT", "SQUARE", "SIGN", "EXP", "LOG", "LOG10", "PI",
        // Logic
        "ISNULL", "CHOOSE", "GREATEST", "LEAST", "ISNUMERIC",
    };

    /// <param name="sql">The query as Claude wrote it.</param>
    /// <param name="allowedSchemas">Schemas the user may read: rpt, plus rpt_mgr for managers.</param>
    public static SqlCheckResult Check(string? sql, IReadOnlyCollection<string> allowedSchemas)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return SqlCheckResult.Reject("The query is empty.");
        }

        if (sql.Length > MaxLength)
        {
            return SqlCheckResult.Reject($"The query is too long (over {MaxLength:N0} characters). Simplify it.");
        }

        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out var errors);
        if (errors.Count > 0)
        {
            var first = errors[0];
            return SqlCheckResult.Reject($"The SQL doesn't parse: {first.Message} (line {first.Line}, column {first.Column}).");
        }

        if (fragment is not TSqlScript { Batches: [{ Statements: [var statement] }] })
        {
            return SqlCheckResult.Reject("Send exactly one SELECT statement, without GO or semicolon-separated extra statements.");
        }

        if (statement is not SelectStatement select)
        {
            return SqlCheckResult.Reject($"Only SELECT queries are allowed ({statement.GetType().Name.Replace("Statement", "", StringComparison.Ordinal)} is not).");
        }

        if (select.Into is not null || select.On is not null)
        {
            return SqlCheckResult.Reject("SELECT ... INTO isn't allowed; just return the rows.");
        }

        if (select.OptimizerHints.Count > 0)
        {
            return SqlCheckResult.Reject("Query hints (OPTION ...) aren't allowed.");
        }

        if (select.ComputeClauses.Count > 0 || select.WithCtesAndXmlNamespaces?.XmlNamespaces is not null)
        {
            return SqlCheckResult.Reject("COMPUTE and WITH XMLNAMESPACES aren't allowed.");
        }

        var cteNames = select.WithCtesAndXmlNamespaces?.CommonTableExpressions
            .Select(cte => cte.ExpressionName.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var visitor = new GuardVisitor(new HashSet<string>(allowedSchemas, StringComparer.OrdinalIgnoreCase), cteNames);
        select.Accept(visitor);
        return visitor.Problem is null ? SqlCheckResult.Ok : SqlCheckResult.Reject(visitor.Problem);
    }

    /// <summary>
    /// Walks every node of the query. ScriptDom calls Visit for each level of a node's type hierarchy, so the
    /// TableReference override sees every kind of FROM-clause source, including ones added in future parser versions.
    /// </summary>
    private sealed class GuardVisitor(HashSet<string> allowedSchemas, HashSet<string> cteNames) : TSqlFragmentVisitor
    {
        public string? Problem { get; private set; }

        private void Fail(string problem) => Problem ??= problem;

        public override void Visit(TableReference node)
        {
            // Plain tables/views, subqueries, joins and VALUES lists. Everything else (OPENROWSET, OPENQUERY,
            // OPENJSON, table-valued functions, full-text tables, CHANGETABLE...) reaches outside the views.
            if (node is not (NamedTableReference or QueryDerivedTable or QualifiedJoin or UnqualifiedJoin
                or JoinParenthesisTableReference or InlineDerivedTable))
            {
                Fail($"{Describe(node)} isn't allowed. Query only the reporting views.");
            }
        }

        public override void Visit(NamedTableReference node)
        {
            var name = node.SchemaObject;
            var display = string.Join('.', name.Identifiers.Select(i => i.Value));
            if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
            {
                Fail($"{display}: only the reporting views in this database can be queried; don't name a server or database.");
            }
            else if (name.SchemaIdentifier is null)
            {
                if (!cteNames.Contains(name.BaseIdentifier.Value))
                {
                    Fail($"Write the schema with every view, for example rpt.{name.BaseIdentifier.Value}.");
                }
            }
            else if (!allowedSchemas.Contains(name.SchemaIdentifier.Value))
            {
                Fail($"{display} isn't available. Only these schemas can be queried: {string.Join(", ", allowedSchemas.Order())}.");
            }

            if (node.TableHints.Count > 0)
            {
                Fail("Table hints such as WITH (NOLOCK) aren't allowed.");
            }

            if (node.TableSampleClause is not null || node.TemporalClause is not null)
            {
                Fail("TABLESAMPLE and FOR SYSTEM_TIME aren't allowed.");
            }
        }

        public override void Visit(FunctionCall node)
        {
            var name = node.FunctionName.Value;
            if (node.CallTarget is not null)
            {
                Fail($"Only built-in functions can be used ({name} is called on an object).");
            }
            else if (!AllowedFunctions.Contains(name))
            {
                Fail($"The function {name.ToUpperInvariant()} isn't allowed. Use standard aggregate, date, text or maths functions.");
            }
        }

        public override void Visit(GlobalVariableExpression node) => Fail($"System variables such as {node.Name} aren't allowed.");

        public override void Visit(VariableReference node) => Fail($"Variables ({node.Name}) aren't allowed; write values directly in the query.");

        public override void Visit(NextValueForExpression node) => Fail("NEXT VALUE FOR isn't allowed.");

        public override void Visit(OdbcFunctionCall node) => Fail("ODBC function calls aren't allowed.");

        public override void Visit(FullTextPredicate node) => Fail("Full-text search (CONTAINS, FREETEXT) isn't available.");

        public override void Visit(ForClause node) => Fail("FOR XML, FOR JSON and FOR BROWSE aren't allowed; return plain rows.");

        public override void Visit(ColumnReferenceExpression node)
        {
            // schema.view.column is the most a column ever needs; four parts would name a database.
            if (node.MultiPartIdentifier?.Count > 3)
            {
                Fail("Column names can't include a database name.");
            }
        }

        private static string Describe(TableReference node) => node switch
        {
            OpenRowsetTableReference or AdHocTableReference => "OPENROWSET/OPENDATASOURCE",
            OpenQueryTableReference => "OPENQUERY",
            OpenJsonTableReference => "OPENJSON",
            OpenXmlTableReference => "OPENXML",
            SchemaObjectFunctionTableReference or GlobalFunctionTableReference => "A table-valued function",
            _ => node.GetType().Name.Replace("TableReference", "", StringComparison.Ordinal),
        };
    }
}
