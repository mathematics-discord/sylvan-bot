namespace SylvanBot;

using DotNetEnv;
using SylvanBot.Interactions;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Formatting.Json;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["commands", ..])
            return await CommandRegistrationTool.RunAsync(args[1..]);

        Env.TraversePath().Load();
        EnableSerilogSelfDiagnostics();

        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, configurationBuilder) => configurationBuilder.AddEnvironmentVariables())
            .UseSerilog((ctx, _, loggerConfiguration) => ConfigureSerilog(ctx.Configuration, loggerConfiguration))
            .ConfigureServices((ctx, services) => services.AddSylvanBot(ctx.Configuration))
            .Build();

        await host.RunAsync();
        return 0;
    }

    public static void ConfigureSerilog(
        IConfiguration config,
        LoggerConfiguration loggerConfiguration,
        string logDirectory = "logs")
    {
        var serviceInstanceId = Environment.MachineName;
        var jsonFormatter = new JsonFormatter(renderMessage: true);

        loggerConfiguration
            .MinimumLevel.Verbose()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service_name", "sylvan-bot")
            .Enrich.WithProperty("service_instance_id", serviceInstanceId)
            .Enrich.WithProperty("environment",
                config["ASPNETCORE_ENVIRONMENT"] ?? "production")
            .WriteTo.Logger(ordinary => ordinary
                .MinimumLevel.Verbose()
                .Filter.ByExcluding(Matching.WithProperty("MessageCacheSample"))
                .WriteTo.Console(
                    LogEventLevel.Debug,
                    "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff'Z'} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
                )
                .WriteTo.File(jsonFormatter, Path.Combine(logDirectory, "verbose-.jsonl"),
                    rollingInterval: RollingInterval.Day)
                .WriteTo.File(jsonFormatter, Path.Combine(logDirectory, "debug-.jsonl"),
                    LogEventLevel.Debug, rollingInterval: RollingInterval.Day)
                .WriteTo.File(jsonFormatter, Path.Combine(logDirectory, "info-.jsonl"),
                    LogEventLevel.Information, rollingInterval: RollingInterval.Day)
                .WriteTo.File(jsonFormatter, Path.Combine(logDirectory, "warning-.jsonl"),
                    LogEventLevel.Warning, rollingInterval: RollingInterval.Day)
                .WriteTo.File(jsonFormatter, Path.Combine(logDirectory, "error-.jsonl"),
                    LogEventLevel.Error, rollingInterval: RollingInterval.Day))
            .WriteTo.Logger(cache => cache
                .MinimumLevel.Debug()
                .Filter.ByIncludingOnly(Matching.WithProperty("MessageCacheSample"))
                .WriteTo.File(
                    new JsonFormatter(),
                    Path.Combine(logDirectory, "cache-.jsonl"),
                    restrictedToMinimumLevel: LogEventLevel.Debug,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 14));
    }

    private static void EnableSerilogSelfDiagnostics()
    {
        SelfLog.Enable(message => Console.Error.WriteLine($"[SerilogSelfLog] {message}"));
    }
}