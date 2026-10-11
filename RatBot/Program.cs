using DotNetEnv;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace RatBot;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["commands", ..])
            return await CommandRegistrationTool.RunAsync(args[1..]);

        Env.TraversePath().Load();
        EnableSerilogSelfDiagnostics();

        using IHost host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, configurationBuilder) => configurationBuilder.AddEnvironmentVariables())
            .UseSerilog((ctx, _, loggerConfiguration) => ConfigureSerilog(ctx.Configuration, loggerConfiguration))
            .ConfigureServices((ctx, services) => services.AddRatBot(ctx.Configuration))
            .Build();

        LogOpenTelemetryStartupConfiguration(host.Services.GetRequiredService<IConfiguration>());
        Log.Information("OpenTelemetry logging pipeline startup test event");
        await host.RunAsync();
        return 0;
    }

    private static void ConfigureOpenTelemetryLogs(IConfiguration config, LoggerConfiguration loggerConfiguration)
    {
        string? endpoint = config["OTEL:Logs:Endpoint"];

        if (string.IsNullOrWhiteSpace(endpoint))
            return;

        string serviceName = config["OTEL:Resource:ServiceName"] ?? "ratbot";
        string serviceInstanceId = config["OTEL:Resource:ServiceInstanceId"] ?? Environment.MachineName;
        string environment = config["OTEL:Resource:Environment"] ?? config["ASPNETCORE_ENVIRONMENT"] ?? "production";
        string configuredProtocol = config["OTEL:Logs:Protocol"] ?? "grpc";

        OtlpProtocol protocol =
            configuredProtocol.Equals("http", StringComparison.OrdinalIgnoreCase)
            || configuredProtocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase)
                ? OtlpProtocol.HttpProtobuf
                : OtlpProtocol.Grpc;

        loggerConfiguration.WriteTo.OpenTelemetry(options =>
        {
            options.Endpoint = endpoint;
            options.Protocol = protocol;

            options.ResourceAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["service.name"] = serviceName,
                ["service.instance.id"] = serviceInstanceId,
                ["service_name"] = serviceName,
                ["service_instance_id"] = serviceInstanceId,
                ["environment"] = environment,
            };
        });
    }

    private static void ConfigureSerilog(IConfiguration config, LoggerConfiguration loggerConfiguration)
    {
        string serviceInstanceId = config["OTEL:Resource:ServiceInstanceId"] ?? Environment.MachineName;

        loggerConfiguration
            .MinimumLevel.Verbose()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service_name", config["OTEL:Resource:ServiceName"] ?? "ratbot")
            .Enrich.WithProperty("service_instance_id", serviceInstanceId)
            .Enrich.WithProperty("environment", config["OTEL:Resource:Environment"] ?? config["ASPNETCORE_ENVIRONMENT"] ?? "production")
            .WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Debug,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff'Z'} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File("logs/verbose-.log", rollingInterval: RollingInterval.Day)
            .WriteTo.File("logs/debug-.log", LogEventLevel.Debug, rollingInterval: RollingInterval.Day)
            .WriteTo.File("logs/info-.log", LogEventLevel.Information, rollingInterval: RollingInterval.Day)
            .WriteTo.File("logs/warning-.log", LogEventLevel.Warning, rollingInterval: RollingInterval.Day)
            .WriteTo.File("logs/error-.log", LogEventLevel.Error, rollingInterval: RollingInterval.Day);

        ConfigureOpenTelemetryLogs(config, loggerConfiguration);
    }

    private static void EnableSerilogSelfDiagnostics() => SelfLog.Enable(message => Console.Error.WriteLine($"[SerilogSelfLog] {message}"));

    private static void LogOpenTelemetryStartupConfiguration(IConfiguration config)
    {
        string? endpoint = config["OTEL:Logs:Endpoint"];

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Log.Warning("OpenTelemetry sink is disabled because OTEL:Logs:Endpoint is not configured");
            return;
        }

        string serviceName = config["OTEL:Resource:ServiceName"] ?? "ratbot";
        string serviceInstanceId = config["OTEL:Resource:ServiceInstanceId"] ?? Environment.MachineName;
        string environment = config["OTEL:Resource:Environment"] ?? config["ASPNETCORE_ENVIRONMENT"] ?? "production";
        string protocol = config["OTEL:Logs:Protocol"] ?? "grpc";

        Log.Information(
            "OpenTelemetry sink configured. Endpoint={OtelEndpoint} Protocol={OtelProtocol} ServiceName={ServiceName} ServiceInstanceId={ServiceInstanceId} Environment={Environment}",
            endpoint,
            protocol,
            serviceName,
            serviceInstanceId,
            environment
        );
    }
}
