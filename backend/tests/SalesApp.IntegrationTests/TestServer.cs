namespace SalesApp.IntegrationTests;

/// <summary>
/// The SQL Server the throwaway test databases are created on: LocalDB unless SALESAPP_TEST_SERVER names another,
/// e.g. the instance's pipe name (np:\\.\pipe\LOCALDB#...\tsql\query) when LocalDB was started from a different
/// Windows session and "(localdb)\MSSQLLocalDB" can't find it. Windows sign-in is used unless SALESAPP_TEST_SQL_USER
/// and SALESAPP_TEST_SQL_PASSWORD are set, as on the CI server's SQL Server container.
/// </summary>
internal static class TestServer
{
    private static readonly string Server =
        Environment.GetEnvironmentVariable("SALESAPP_TEST_SERVER") is { Length: > 0 } custom ? custom : @"(localdb)\MSSQLLocalDB";

    private static readonly string Credentials =
        Environment.GetEnvironmentVariable("SALESAPP_TEST_SQL_USER") is { Length: > 0 } user
            ? $"User ID={user};Password={Environment.GetEnvironmentVariable("SALESAPP_TEST_SQL_PASSWORD")}"
            : "Trusted_Connection=True";

    public static string ConnectionString(string database) =>
        $"Server={Server};Database={database};{Credentials};TrustServerCertificate=True";
}
