using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using InvoiceHub.Benchmarks;

// dotnet run -c Release --project benchmarks/InvoiceHub.Benchmarks -- seed [count]   (default 100000)
// dotnet run -c Release --project benchmarks/InvoiceHub.Benchmarks -- bench
switch (args.FirstOrDefault())
{
    case "seed":
        var target = args.Length > 1 ? int.Parse(args[1]) : 100_000;
        await Seeder.EnsureSeededAsync(target);
        break;

    case "bench":
        // In-process so BenchmarkDotNet does not generate and build its own project for each run.
        var config = DefaultConfig.Instance.AddJob(Job.Default.WithToolchain(InProcessNoEmitToolchain.Instance));
        BenchmarkRunner.Run<GstReportBenchmarks>(config);
        break;

    default:
        Console.WriteLine("Usage: seed [invoiceCount] | bench");
        break;
}
