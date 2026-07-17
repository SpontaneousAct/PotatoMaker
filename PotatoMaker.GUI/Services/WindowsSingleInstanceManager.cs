using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Enforces a single Windows desktop instance and forwards second-launch arguments
/// to the already running process.
/// </summary>
internal sealed class WindowsSingleInstanceManager : IDisposable
{
    private const int ConnectRetryDelayMilliseconds = 100;
    private static readonly TimeSpan ConnectRetryWindow = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions SerializerOptions = new();

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly ConcurrentQueue<string[]> _pendingRequests = new();
    private readonly object _handlerGate = new();
    private readonly Task? _listenerTask;
    private Action<IReadOnlyList<string>>? _activationHandler;
    private bool _disposed;

    private WindowsSingleInstanceManager(Mutex mutex, bool ownsMutex, string pipeName)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
        _pipeName = pipeName;
        IsPrimaryInstance = ownsMutex;

        if (IsPrimaryInstance)
            _listenerTask = Task.Run(() => ListenForActivationRequestsAsync(_shutdownCts.Token));
    }

    public bool IsPrimaryInstance { get; }

    [SupportedOSPlatform("windows")]
    public static WindowsSingleInstanceManager? Create(string[] args)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        ArgumentNullException.ThrowIfNull(args);

        string userScope = BuildUserScope();
        string mutexName = $@"Local\PotatoMaker_{userScope}_SingleInstance";
        string pipeName = $"PotatoMaker_{userScope}_Activation";

        var mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
        var manager = new WindowsSingleInstanceManager(mutex, createdNew, pipeName);
        if (!createdNew)
            manager.TryNotifyPrimaryInstance(args);

        return manager;
    }

    public void RegisterActivationHandler(Action<IReadOnlyList<string>> activationHandler)
    {
        ArgumentNullException.ThrowIfNull(activationHandler);

        lock (_handlerGate)
        {
            ThrowIfDisposed();
            _activationHandler = activationHandler;
        }

        while (_pendingRequests.TryDequeue(out string[]? request))
        {
            activationHandler(request);
        }
    }

    private bool TryNotifyPrimaryInstance(IReadOnlyList<string> args)
    {
        WindowsForegroundPermission.TryAllowExistingInstanceToActivate();

        string payload = JsonSerializer.Serialize(args, SerializerOptions);
        DateTime deadline = DateTime.UtcNow + ConnectRetryWindow;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                client.Connect(250);

                using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(payload);
                writer.Flush();
                return true;
            }
            catch (IOException)
            {
            }
            catch (TimeoutException)
            {
            }

            Thread.Sleep(ConnectRetryDelayMilliseconds);
        }

        return false;
    }

    private async Task ListenForActivationRequestsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(
                    server,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);

                string payload = await reader.ReadToEndAsync().ConfigureAwait(false);
                DispatchActivationRequest(DeserializePayload(payload));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // Ignore transient pipe failures and keep listening.
            }
        }
    }

    private void DispatchActivationRequest(string[] args)
    {
        Action<IReadOnlyList<string>>? activationHandler;
        lock (_handlerGate)
        {
            if (_disposed)
                return;

            activationHandler = _activationHandler;
            if (activationHandler is null)
            {
                _pendingRequests.Enqueue(args);
                return;
            }
        }

        activationHandler(args);
    }

    private static string[] DeserializePayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(payload, SerializerOptions)
                ?.Where(argument => !string.IsNullOrWhiteSpace(argument))
                .ToArray()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    [SupportedOSPlatform("windows")]
    private static string BuildUserScope()
    {
        string rawScope = WindowsIdentity.GetCurrent().User?.Value
            ?? Environment.UserName
            ?? "default";

        var builder = new StringBuilder(rawScope.Length);
        foreach (char c in rawScope)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return builder.Length > 0 ? builder.ToString() : "default";
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        lock (_handlerGate)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _shutdownCts.Cancel();
        try
        {
            _listenerTask?.Wait(millisecondsTimeout: 1000);
        }
        catch (AggregateException)
        {
        }

        _shutdownCts.Dispose();

        if (_ownsMutex)
            _mutex.ReleaseMutex();

        _mutex.Dispose();
    }

    private static class WindowsForegroundPermission
    {
        [DllImport("user32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int processId);

        public static void TryAllowExistingInstanceToActivate()
        {
            try
            {
                using Process currentProcess = Process.GetCurrentProcess();
                Process? existingProcess = Process
                    .GetProcessesByName(currentProcess.ProcessName)
                    .FirstOrDefault(process => process.Id != currentProcess.Id);

                if (existingProcess is not null)
                    AllowSetForegroundWindow(existingProcess.Id);
            }
            catch
            {
                // Best-effort only; activation still works in many cases without this.
            }
        }
    }
}
