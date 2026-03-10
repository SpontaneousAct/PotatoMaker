using Microsoft.Extensions.Logging;

namespace PotatoMaker.Core;

/// <summary>
/// Defines structured log event ids used by the pipeline.
/// </summary>
public static class PipelineEvents
{
    public static readonly EventId Success = new(1, "Success");
    public static readonly EventId Emphasis = new(2, "Emphasis");
}
