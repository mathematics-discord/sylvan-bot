using System.Diagnostics;

namespace SylvanBot.Common;

// Timings include failed or cancelled work: disposal records elapsed time, not success.
internal sealed class OperationTiming : IDisposable
{
    private readonly ILogger _logger;
    private readonly long _started = Stopwatch.GetTimestamp();
    private long _stageStarted = Stopwatch.GetTimestamp();
    private string _stage;

    public OperationTiming(ILogger logger, string operation, string stage)
    {
        _logger = logger
           .ForContext("operation_name", operation)
           .ForContext("operation_id", Guid.NewGuid());

        _stage = stage;
    }

    public void Next(string stage)
    {
        WriteStage();
        _stage = stage;
        _stageStarted = Stopwatch.GetTimestamp();
    }

    private void WriteStage() => _logger
       .ForContext("event_kind", "operation.stage_timing")
       .Debug("Stage {Stage} elapsed {ElapsedMs} ms", _stage, Stopwatch.GetElapsedTime(_stageStarted).TotalMilliseconds);

    public void Dispose()
    {
        WriteStage();

        _logger
           .ForContext("event_kind", "operation.total_timing")
           .Debug("Operation elapsed {TotalMs} ms", Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
    }
}