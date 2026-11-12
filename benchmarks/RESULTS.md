# GST report benchmark

The same report, "GST collected per slab for invoices issued in a date range", three ways, on a seeded database.

## Setup

| | |
|---|---|
| Data | 100,000 invoices, 300,063 lines, one tenant, issue dates 2025-01-01 to 2026-10-31 (seeded with fixed random seed 42) |
| Database | SQL Server 2019 LocalDB, local PC (8 logical CPUs, 15.7 GB RAM) |
| Runtime | .NET 8.0.34, Release build, BenchmarkDotNet 0.14.0 (in-process toolchain) |
| What is timed | One full call with a fresh `DbContext`, the way a request would make it. Results are checked equal before timing starts |
| Run | `dotnet run -c Release --project benchmarks/InvoiceHub.Benchmarks -- seed` then `-- bench` |

## Results (one run, 2026-11-12)

| Date range | EF Core LINQ (baseline) | EF Core, procedure's query shape | Dapper + stored procedure |
|---|---:|---:|---:|
| 1 month | 87.7 ms | 64.6 ms (0.75x) | 61.4 ms (0.71x) |
| 12 months | 424.3 ms | 269.7 ms (0.64x) | 258.2 ms (0.61x) |
| all 22 months | 771.7 ms | 435.9 ms (0.57x) | 421.5 ms (0.55x) |

Memory allocated per call on the .NET side: about 131 KB for EF Core, 100 KB for Dapper (24% less).

## What this shows

- **The stored procedure is 29% to 45% faster than the first EF Core query**, depending on the range.
- **Almost all of that comes from the query shape, not from Dapper.** The first EF query and the first version of the procedure both used `COUNT(DISTINCT invoice)`, which made SQL Server spill to a worktable. Summing per invoice and slab first, then counting rows, halves the server time. Written as an EF Core query, that same shape runs within 3% to 5% of the stored procedure.
- **Dapper's own advantage is small:** roughly 3% to 5% in time and 24% less allocation. Both are real but modest.
- Both versions return identical rows. The integration tests check this too (`GstReportTests`).

## Caveats

- One machine, one run, LocalDB. Treat the ratios as indicative, not as a universal claim.
- The PC was short on free memory (about 0.9 GB free of 15.7 GB, with a browser and other apps open). LocalDB's memory pool shrank during runs and produced stalls of up to 20 seconds in the first EF query. BenchmarkDotNet removed those as outliers (16 and 4 for the first EF query, 5 and 4 for the two-stage EF query, none for Dapper; only the first EF query's were multi-second) and the table reports means with those removed. Earlier runs aborted on 30-second command timeouts for the same reason, so the benchmark now uses a 300-second command timeout.
- The first run, before the procedure was rewritten, showed no real difference: Dapper 0.73x to 1.05x of EF across the three ranges.
