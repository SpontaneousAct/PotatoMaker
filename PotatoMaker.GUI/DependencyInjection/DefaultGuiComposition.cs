using PotatoMaker.GUI.Services;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.DependencyInjection;

/// <summary>
/// Builds a small, consistent fallback object graph for design-time and tests
/// that do not use the full DI container.
/// </summary>
internal static class DefaultGuiComposition
{
    internal readonly record struct QueueGraph(
        IVideoEncodingService EncodingService,
        EncodeExecutionCoordinator ExecutionCoordinator,
        IEncodeCompletionNotifier EncodeCompletionNotifier,
        IProcessedVideoTracker ProcessedVideoTracker);

    internal sealed record WorkspaceGraph(
        IVideoAnalysisService AnalysisService,
        IVideoEncodingService EncodingService,
        VideoPlayerViewModel VideoPlayer,
        IEncoderCapabilityService EncoderCapabilityService,
        CompressionQueueViewModel CompressionQueue,
        EncodeExecutionCoordinator ExecutionCoordinator,
        IEncodeCompletionNotifier EncodeCompletionNotifier,
        IProcessedVideoTracker ProcessedVideoTracker);

    internal sealed record ShellGraph(
        EncodeWorkspaceViewModel Workspace,
        IThemeService ThemeService,
        IRecentVideoDiscoveryService RecentVideoDiscoveryService,
        IRecentVideoThumbnailService RecentVideoThumbnailService,
        IProcessedVideoTracker ProcessedVideoTracker,
        CompressionQueueViewModel CompressionQueue,
        IAppUpdateService UpdateService,
        IAppVersionService AppVersionService);

    public static QueueGraph CreateQueueGraph()
    {
        IVideoEncodingService encodingService = new VideoEncodingService();
        var executionCoordinator = new EncodeExecutionCoordinator();
        IEncodeCompletionNotifier completionNotifier = NoOpEncodeCompletionNotifier.Instance;
        IProcessedVideoTracker processedVideoTracker = DisabledProcessedVideoTracker.Instance;

        return new QueueGraph(
            encodingService,
            executionCoordinator,
            completionNotifier,
            processedVideoTracker);
    }

    public static WorkspaceGraph CreateWorkspaceGraph()
    {
        QueueGraph queueGraph = CreateQueueGraph();
        var compressionQueue = new CompressionQueueViewModel(
            null,
            queueGraph.EncodingService,
            queueGraph.ExecutionCoordinator,
            queueGraph.EncodeCompletionNotifier,
            queueGraph.ProcessedVideoTracker);

        return new WorkspaceGraph(
            new VideoAnalysisService(),
            queueGraph.EncodingService,
            new VideoPlayerViewModel(initializePlayer: false),
            new EncoderCapabilityService(),
            compressionQueue,
            queueGraph.ExecutionCoordinator,
            queueGraph.EncodeCompletionNotifier,
            queueGraph.ProcessedVideoTracker);
    }

    public static ShellGraph CreateShellGraph()
    {
        WorkspaceGraph workspaceGraph = CreateWorkspaceGraph();
        var workspace = new EncodeWorkspaceViewModel(
            workspaceGraph.AnalysisService,
            workspaceGraph.EncodingService,
            workspaceGraph.VideoPlayer,
            workspaceGraph.EncoderCapabilityService,
            null,
            workspaceGraph.CompressionQueue,
            workspaceGraph.ExecutionCoordinator,
            initializeEncoderSupport: true,
            workspaceGraph.EncodeCompletionNotifier,
            null,
            workspaceGraph.ProcessedVideoTracker);

        return new ShellGraph(
            workspace,
            new AvaloniaThemeService(),
            new RecentVideoDiscoveryService(),
            new RecentVideoThumbnailService(),
            workspaceGraph.ProcessedVideoTracker,
            workspaceGraph.CompressionQueue,
            new DisabledAppUpdateService(),
            new AssemblyAppVersionService());
    }
}
