using BenchmarkDotNet.Attributes;
using InvoiceHub.Application.Reports;
using InvoiceHub.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;

namespace InvoiceHub.Benchmarks;

/// <summary>
/// The same GST summary three ways: an EF Core LINQ query (COUNT DISTINCT), the same query reshaped to match
/// the stored procedure, and the usp_GstSummaryReport stored procedure called with Dapper. Each call uses a fresh DbContext, as a request would.
/// </summary>
[MemoryDiagnoser]
public class GstReportBenchmarks
{
    private Guid _tenantId;

    [Params("1 month", "12 months", "all 22 months")]
    public string Range { get; set; } = "";

    private (DateOnly From, DateOnly To) Dates => Range switch
    {
        "1 month" => (new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)),
        "12 months" => (new DateOnly(2025, 11, 1), new DateOnly(2026, 10, 31)),
        _ => (new DateOnly(2025, 1, 1), new DateOnly(2026, 10, 31)),
    };

    [GlobalSetup]
    public async Task Setup()
    {
        await using var db = BenchDatabase.CreateContext(null);
        _tenantId = (await db.Tenants.SingleAsync(t => t.Name == Seeder.TenantName)).Id;

        // Never time two queries that disagree.
        var (from, to) = Dates;
        var viaProc = await Dapper_StoredProcedure();
        var viaEf = await EfCore_Linq();
        var viaEfTwoStage = await EfCore_TwoStage();
        if (!viaProc.SequenceEqual(viaEf) || !viaProc.SequenceEqual(viaEfTwoStage))
            throw new InvalidOperationException($"The three readers disagree for range '{Range}'. Not benchmarking.");
        Console.WriteLine($"[{Range}] {from} to {to}: {viaProc.Sum(r => r.InvoiceCount):N0} invoice-slab rows, {viaProc.Count} slabs, results match.");
    }

    [Benchmark(Baseline = true)]
    public async Task<IReadOnlyList<GstSummaryRow>> EfCore_Linq()
    {
        var (from, to) = Dates;
        await using var db = BenchDatabase.CreateContext(_tenantId);
        return await new EfGstReportReader(db).GetSummaryAsync(_tenantId, from, to, default);
    }

    [Benchmark]
    public async Task<IReadOnlyList<GstSummaryRow>> EfCore_TwoStage()
    {
        var (from, to) = Dates;
        await using var db = BenchDatabase.CreateContext(_tenantId);
        return await new EfTwoStageReader(db).GetSummaryAsync(_tenantId, from, to, default);
    }

    [Benchmark]
    public async Task<IReadOnlyList<GstSummaryRow>> Dapper_StoredProcedure()
    {
        var (from, to) = Dates;
        await using var db = BenchDatabase.CreateContext(_tenantId);
        return await new DapperGstReportReader(db).GetSummaryAsync(_tenantId, from, to, default);
    }
}
