using BenchmarkDotNet.Running;
using Moirai.Benchmarks;

// Run with:  dotnet run -c Release --project Moirai.Benchmarks
// Filter:    dotnet run -c Release --project Moirai.Benchmarks -- --filter '*WsgBenchmark*'
// Quick dev: dotnet run -c Release --project Moirai.Benchmarks -- --job short
BenchmarkSwitcher.FromAssembly(typeof(WsgBenchmark).Assembly).Run(args);
