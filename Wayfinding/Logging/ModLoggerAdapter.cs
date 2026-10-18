using Vintagestory.API.Common;
using Wayfinding.Infra;

namespace Wayfinding;

/// <summary>
/// Mod.Logger already writes "[" + modid + "] " in front of every message.
/// The game-free log adds [wayfinding] for tests and chat; strip it here so
/// the file keeps a single prefix.
/// </summary>
public sealed class ModLoggerAdapter : ILogSink
{
    private readonly ILogger logger;

    public ModLoggerAdapter(ILogger logger)
    {
        this.logger = logger;
    }

    public void Write(LogLevel level, string line)
    {
        var body = WayfindingLog.WithoutPrefix(line);
        switch (level)
        {
            case LogLevel.Error:
                logger.Error(body);
                break;
            case LogLevel.Warning:
                logger.Warning(body);
                break;
            case LogLevel.Debug:
            case LogLevel.Detail:
                logger.Debug(body);
                break;
            case LogLevel.Trace:
                logger.VerboseDebug(body);
                break;
            default:
                logger.Notification(body);
                break;
        }
    }
}
