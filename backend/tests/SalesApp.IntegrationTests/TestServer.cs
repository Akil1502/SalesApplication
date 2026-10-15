namespace SalesApp.IntegrationTests;

/// <summary>
/// The SQL Server the throwaway test databases are created on: LocalDB unless SALESAPP_TEST_SERVER names another,
/// e.g. the instance's pipe name (np:\\.\pipe\LOCALDB#...\tsql\query) when LocalDB was started from a different
/// Windows session and "(localdb)\MSSQLLocalDB" can't find it.
/// </summary>
internal static class TestServer
{
    private static readonly string Server =
        Environment.GetEnvironmentVariable("SALESAPP_TEST_SERVER") is { Length: > 0 } custom ? custom : @"(localdb)\MSSQLLocalDB";

    public static string ConnectionString(string database) =>
        $"Server={Server};Database={database};Trusted_Connection=True;TrustServerCertificate=True";
}
