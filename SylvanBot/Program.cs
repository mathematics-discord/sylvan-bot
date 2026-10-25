namespace SylvanBot;

using DotNetEnv;
using SylvanBot.Interactions;
using Serilog.Debugging;
using Serilog.Events;

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

    private static void ConfigureSerilog(IConfiguration config, LoggerConfiguration loggerConfiguration)
    {
        var serviceInstanceId = Environment.MachineName;

        loggerConfiguration
            .MinimumLevel.Verbose()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service_name", "sylvan-bot")
            .Enrich.WithProperty("service_instance_id", serviceInstanceId)
            .Enrich.WithProperty("environment",
                config["ASPNETCORE_ENVIRONMENT"] ?? "production")
            .WriteTo.Console(
                LogEventLevel.Debug,
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff'Z'} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File("logs/verbose-.log", rollingInterval: RollingInterval.Day)
            .WriteTo.File("logs/debug-.log", LogEventLevel.Debug, rollingInterval: RollingInterval.Day)
            .WriteTo.File("logs/info-.log", LogEventLevel.Information, rollingInterval: RollingInterval.Day)
            .WriteTo.File("logs/warning-.log", LogEventLevel.Warning, rollingInterval: RollingInterval.Day)
            .WriteTo.File("logs/error-.log", LogEventLevel.Error, rollingInterval: RollingInterval.Day);
    }

    private static void EnableSerilogSelfDiagnostics()
    {
        SelfLog.Enable(message => Console.Error.WriteLine($"[SerilogSelfLog] {message}"));
    }
}