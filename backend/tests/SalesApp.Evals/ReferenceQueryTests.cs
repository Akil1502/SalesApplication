using Microsoft.Extensions.DependencyInjection;
using SalesApp.AI.AskData;
using SalesApp.Core.Common;

namespace SalesApp.Evals;

/// <summary>
/// Every reference query runs against the demo data and has the columns its case requires, so the live evals can't
/// quietly break when the reporting views change. No Claude calls; always runs.
/// </summary>
public sealed class ReferenceQueryTests(ReferenceQueryHost host) : IClassFixture<ReferenceQueryHost>
{
    public static TheoryData<string> Cases => [.. AskDataCases.All.Select(c => c.Id)];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_reference_query_runs(string id)
    {
        var testCase = AskDataCases.All.Single(c => c.Id == id);
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var today = services.GetRequiredService<IBusinessClock>().Today;

        var result = await services.GetRequiredService<IReadOnlySqlExecutor>()
            .RunAsync(testCase.Sql(today), ReadOnlySqlExecutor.ManagerReader, TestContext.Current.CancellationToken);

        Assert.All(testCase.Required ?? [], group => Assert.All(group, column => Assert.InRange(column, 0, result.Columns.Count - 1)));
        Assert.NotEmpty(result.Rows);
    }
}
