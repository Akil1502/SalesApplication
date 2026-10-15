using SalesApp.AI.AskData;

namespace SalesApp.Tests.Ai;

public sealed class SqlGuardTests
{
    private static readonly string[] Rep = ["rpt"];
    private static readonly string[] Manager = ["rpt", "rpt_mgr"];

    [Theory]
    [InlineData("SELECT TOP 5 CustomerName, SUM(TaxableValue) AS Sales FROM rpt.SalesLines GROUP BY CustomerName ORDER BY Sales DESC")]
    [InlineData("select count(*) from rpt.Invoices where Outstanding > 0")]
    [InlineData("SELECT * FROM rpt.Products WHERE StockStatus = 'Low'")]
    [InlineData("SELECT FinancialYear, SUM(TaxableValue) FROM rpt.SalesLines GROUP BY FinancialYear;")]
    [InlineData("WITH m AS (SELECT MonthStart, SUM(TaxableValue) AS V FROM rpt.SalesLines GROUP BY MonthStart) SELECT * FROM m ORDER BY MonthStart")]
    [InlineData("WITH a AS (SELECT 1 AS x), b AS (SELECT x FROM a) SELECT x FROM b")]
    [InlineData("SELECT s.StateName, COUNT(DISTINCT s.CustomerId) FROM rpt.SalesLines AS s GROUP BY s.StateName")]
    [InlineData("SELECT c.Name, i.InvoiceNumber FROM rpt.Customers c JOIN rpt.Invoices i ON i.CustomerId = c.CustomerId")]
    [InlineData("SELECT c.Name FROM rpt.Customers c LEFT JOIN rpt.Invoices i ON i.CustomerId = c.CustomerId WHERE i.InvoiceId IS NULL")]
    [InlineData("SELECT Name FROM rpt.Customers WHERE CustomerId IN (SELECT CustomerId FROM rpt.Invoices WHERE DaysOverdue > 90)")]
    [InlineData("SELECT x.CustomerName FROM (SELECT CustomerName, SUM(Outstanding) AS o FROM rpt.Invoices GROUP BY CustomerName) x WHERE x.o > 100000")]
    [InlineData("SELECT c.Name, t.Total FROM rpt.Customers c CROSS APPLY (SELECT SUM(TaxableValue) AS Total FROM rpt.SalesLines s WHERE s.CustomerId = c.CustomerId) t")]
    [InlineData("SELECT ProductName, RANK() OVER (ORDER BY SUM(TaxableValue) DESC) FROM rpt.SalesLines GROUP BY ProductName")]
    [InlineData("SELECT MonthStart, LAG(SUM(TaxableValue)) OVER (ORDER BY MonthStart) FROM rpt.SalesLines GROUP BY MonthStart")]
    [InlineData("SELECT DATEADD(month, -1, DATEFROMPARTS(2026, 10, 1)), EOMONTH('2026-09-01'), DATEDIFF(day, '2026-01-01', '2026-10-14')")]
    [InlineData("SELECT FORMAT(InvoiceDate, 'yyyy-MM'), YEAR(InvoiceDate), DATENAME(month, InvoiceDate) FROM rpt.Invoices")]
    [InlineData("SELECT LEFT(Name, 3), RIGHT(Code, 2), UPPER(City), CONCAT(Name, ' - ', City) FROM rpt.Customers")]
    [InlineData("SELECT CAST(TaxableValue AS int), CONVERT(varchar(10), InvoiceDate, 120), TRY_CAST('1' AS int) FROM rpt.Invoices")]
    [InlineData("SELECT IIF(Outstanding > 0, 'Due', 'Paid'), COALESCE(SalesRepName, 'Unassigned'), NULLIF(Status, '') FROM rpt.Invoices i JOIN rpt.Customers c ON c.CustomerId = i.CustomerId")]
    [InlineData("SELECT CASE WHEN DaysOverdue > 90 THEN 'Over 90' WHEN DaysOverdue > 0 THEN 'Overdue' ELSE 'Current' END, SUM(Outstanding) FROM rpt.Invoices GROUP BY CASE WHEN DaysOverdue > 90 THEN 'Over 90' WHEN DaysOverdue > 0 THEN 'Overdue' ELSE 'Current' END")]
    [InlineData("SELECT ROUND(AVG(UnitPrice), 2), STRING_AGG(Sku, ', ') FROM rpt.SalesLines")]
    [InlineData("SELECT Name FROM rpt.Products WHERE Name LIKE '%yarn%' UNION SELECT Name FROM rpt.Customers")]
    [InlineData("SELECT * FROM (VALUES (1, 'a'), (2, 'b')) AS v(n, s)")]
    [InlineData("SELECT [Name] FROM [rpt].[Customers] WHERE [City] = N'திருப்பூர்'")]
    [InlineData("SELECT TOP (10) PERCENT ProductName FROM rpt.SalesLines ORDER BY TaxableValue DESC")]
    [InlineData("SELECT InvoiceDate AT TIME ZONE 'India Standard Time' FROM rpt.Invoices")]
    public void Ordinary_reporting_queries_are_allowed(string sql)
    {
        var result = SqlGuard.Check(sql, Rep);

        Assert.True(result.Allowed, result.Problem);
    }

    [Fact]
    public void Managers_can_read_margins_and_reps_cannot()
    {
        const string sql = "SELECT ProductName, SUM(GrossMargin) FROM rpt_mgr.Margins GROUP BY ProductName";

        Assert.True(SqlGuard.Check(sql, Manager).Allowed);
        var rep = SqlGuard.Check(sql, Rep);
        Assert.False(rep.Allowed);
        Assert.Contains("rpt_mgr.Margins isn't available", rep.Problem, StringComparison.Ordinal);
    }

    [Theory]
    // Not a single SELECT
    [InlineData("DELETE FROM rpt.Invoices", "Only SELECT")]
    [InlineData("UPDATE rpt.Products SET ListPrice = 0", "Only SELECT")]
    [InlineData("INSERT INTO rpt.Customers (Name) VALUES ('x')", "Only SELECT")]
    [InlineData("DROP VIEW rpt.SalesLines", "Only SELECT")]
    [InlineData("TRUNCATE TABLE dbo.Payments", "Only SELECT")]
    [InlineData("EXEC sp_who", "Only SELECT")]
    [InlineData("EXECUTE AS LOGIN = 'sa'", "Only SELECT")]
    [InlineData("REVERT", "Only SELECT")]
    [InlineData("DECLARE @x int = 1", "Only SELECT")]
    [InlineData("SET NOCOUNT ON", "Only SELECT")]
    [InlineData("WAITFOR DELAY '00:00:10'", "Only SELECT")]
    [InlineData("GRANT SELECT ON SCHEMA::dbo TO ai_reader", "Only SELECT")]
    [InlineData("ALTER USER ai_reader WITH DEFAULT_SCHEMA = dbo", "Only SELECT")] // the one self-change SQL Server can't deny
    [InlineData("BEGIN TRAN", "Only SELECT")]
    [InlineData("SELECT 1; SELECT 2", "exactly one")]
    [InlineData("SELECT 1; DELETE FROM rpt.Invoices", "exactly one")]
    [InlineData("SELECT 1\nGO\nSELECT 2", "exactly one")]
    [InlineData("REVERT; SELECT * FROM dbo.Customers", "exactly one")]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("SELEC * FROM rpt.Customers", "doesn't parse")]
    // Outside the reporting views
    [InlineData("SELECT * FROM dbo.Customers", "dbo.Customers isn't available")]
    [InlineData("SELECT PasswordHash FROM dbo.AspNetUsers", "isn't available")]
    [InlineData("SELECT * FROM Customers", "Write the schema")]
    [InlineData("SELECT name FROM sys.objects", "sys.objects isn't available")]
    [InlineData("SELECT name FROM sys.databases", "isn't available")]
    [InlineData("SELECT * FROM INFORMATION_SCHEMA.TABLES", "isn't available")]
    [InlineData("SELECT name FROM master.sys.sql_logins", "don't name a server or database")]
    [InlineData("SELECT * FROM msdb.dbo.backupset", "don't name a server or database")]
    [InlineData("SELECT * FROM SalesAppDb.rpt.Customers", "don't name a server or database")]
    [InlineData("SELECT * FROM otherserver.SalesAppDb.rpt.Customers", "don't name a server or database")]
    [InlineData("SELECT * FROM rpt.Customers c JOIN dbo.Customers d ON d.Id = c.CustomerId", "dbo.Customers isn't available")]
    [InlineData("SELECT * FROM rpt.Invoices WHERE CustomerId IN (SELECT Id FROM dbo.Customers)", "dbo.Customers isn't available")]
    [InlineData("WITH x AS (SELECT * FROM dbo.Payments) SELECT * FROM x", "dbo.Payments isn't available")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=.;Trusted_Connection=yes', 'SELECT 1')", "OPENROWSET")]
    [InlineData("SELECT * FROM OPENQUERY(linked, 'SELECT 1')", "OPENQUERY")]
    [InlineData("SELECT * FROM OPENDATASOURCE('SQLNCLI', 'Data Source=.').master.sys.objects", "OPENROWSET/OPENDATASOURCE")]
    [InlineData("SELECT * FROM OPENJSON('[1,2]')", "OPENJSON")]
    [InlineData("SELECT * FROM STRING_SPLIT('a,b', ',')", "table-valued function")]
    [InlineData("SELECT * FROM sys.dm_exec_sessions", "isn't available")]
    [InlineData("SELECT * FROM rpt.fn_secret(1)", "table-valued function")]
    // Smuggling things in through expressions
    [InlineData("SELECT * INTO #copy FROM rpt.Customers", "INTO")]
    [InlineData("SELECT * INTO dbo.Leak FROM rpt.Customers", "INTO")]
    [InlineData("SELECT @@VERSION", "System variables")]
    [InlineData("SELECT @@SERVERNAME", "System variables")]
    [InlineData("SELECT * FROM rpt.Customers WHERE Name = @name", "Variables")]
    [InlineData("SELECT SUSER_SNAME()", "SUSER_SNAME isn't allowed")]
    [InlineData("SELECT USER_NAME(), DB_NAME()", "isn't allowed")]
    [InlineData("SELECT OBJECT_DEFINITION(OBJECT_ID('rpt.SalesLines'))", "isn't allowed")]
    [InlineData("SELECT HAS_PERMS_BY_NAME('dbo.Customers', 'OBJECT', 'SELECT')", "isn't allowed")]
    [InlineData("SELECT dbo.fn_secret()", "Only built-in functions")]
    [InlineData("SELECT * FROM rpt.Customers WITH (NOLOCK)", "Table hints")]
    [InlineData("SELECT * FROM rpt.Customers OPTION (MAXDOP 1)", "Query hints")]
    [InlineData("SELECT * FROM rpt.Customers FOR JSON PATH", "FOR XML, FOR JSON")]
    [InlineData("SELECT * FROM rpt.Customers FOR XML AUTO", "FOR XML, FOR JSON")]
    [InlineData("SELECT * FROM rpt.Products WHERE CONTAINS(Name, 'yarn')", "Full-text")]
    [InlineData("SELECT {fn CURDATE()}", "ODBC")]
    public void Anything_else_is_refused_with_a_reason(string sql, string expectedProblem)
    {
        var result = SqlGuard.Check(sql, Manager);

        Assert.False(result.Allowed, $"should have been refused: {sql}");
        Assert.Contains(expectedProblem, result.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Very_long_queries_are_refused()
    {
        var sql = "SELECT 1 AS a" + string.Concat(Enumerable.Repeat(", 1 AS a", 1000));

        Assert.Contains("too long", SqlGuard.Check(sql, Rep).Problem, StringComparison.Ordinal);
    }
}
