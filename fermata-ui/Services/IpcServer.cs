/// IPC server that receives InterceptEvent messages from fermata-monitor and
/// sends back InterceptResponse. On Windows: Windows Named Pipe. On macOS: Unix
/// domain socket at $TMPDIR/fermata.sock. Does not relaunch processes.
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace FermataUI.Services;

public record InterceptEvent(
    string AppName, string ExePath, string Args,
    string WorkingDir, string Timestamp);

public class IpcServer : IDisposable
{
    // Windows named pipe name
    public const string PipeName = "FermataIPC";

    // macOS Unix socket path — matches socket_path() in ipc.rs
    public static string UnixSocketPath =>
        Path.Combine(Path.GetTempPath(), "fermata.sock");

    public event Action<InterceptEvent>? InterceptReceived;

    private readonly CancellationTokenSource _cts = new();
    private TaskCompletionSource<string>? _pendingResponse;

    public void Start() => Task.Run(() => ListenLoop(_cts.Token));

    public void SendResponse(string action) =>
        _pendingResponse?.TrySetResult(action);

    private async Task ListenLoop(CancellationToken ct)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: named pipe — recreate on each connection.
            while (!ct.IsCancellationRequested)
            {
                try { await HandleWindows(ct); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log($"pipe error (restarting): {ex.Message}"); }
            }
        }
        else
        {
            // macOS: Unix socket — create once, accept connections in a loop.
            try { await HandleUnix(ct); }
            catch (OperationCanceledException) { /* clean shutdown */ }
            catch (Exception ex) { Log($"socket fatal error: {ex.Message}"); }
        }
    }

    private static void Log(string msg) =>
        System.Diagnostics.Debug.WriteLine($"[IpcServer] {msg}");

    // ── Windows: Named Pipe ───────────────────────────────────────────────────

    private async Task HandleWindows(CancellationToken ct)
    {
        await using var pipe = new NamedPipeServerStream(
            PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        await pipe.WaitForConnectionAsync(ct);
        await Exchange(pipe, pipe, ct);
    }

    // ── macOS: Unix Domain Socket ─────────────────────────────────────────────

    private async Task HandleUnix(CancellationToken ct)
    {
        var path = UnixSocketPath;

        // Remove stale socket file from a previous run.
        if (File.Exists(path)) File.Delete(path);

        var endpoint = new UnixDomainSocketEndPoint(path);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(endpoint);
        listener.Listen(1);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptAsync(ct);
                await using var stream = new NetworkStream(client, ownsSocket: true);
                await Exchange(stream, stream, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log($"connection error (continuing): {ex.Message}"); }
        }
    }

    // ── Shared read/write ─────────────────────────────────────────────────────

    private async Task Exchange(Stream reader, Stream writer, CancellationToken ct)
    {
        // UTF8 without BOM — Rust's serde_json rejects the BOM prefix.
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var sr = new StreamReader(reader, utf8NoBom, leaveOpen: true);
        using var sw = new StreamWriter(writer, utf8NoBom, leaveOpen: true) { AutoFlush = true };

        var json = await sr.ReadLineAsync(ct);
        if (json is null) return;

        var dto = JsonSerializer.Deserialize<InterceptEventDto>(json);
        if (dto is null) return;

        var evt = new InterceptEvent(
            dto.app_name, dto.exe_path, dto.args ?? "",
            dto.working_dir ?? "", dto.timestamp ?? DateTime.UtcNow.ToString("O"));

        _pendingResponse = new TaskCompletionSource<string>();
        InterceptReceived?.Invoke(evt);

        var action = await _pendingResponse.Task
            .WaitAsync(TimeSpan.FromSeconds(90), ct)
            .ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : "cancel",
                          TaskContinuationOptions.None);

        await sw.WriteLineAsync(
            JsonSerializer.Serialize(new { type = "InterceptResponse", action }));

        _pendingResponse = null;
    }

    public void Dispose() => _cts.Cancel();

    private record InterceptEventDto(
        string app_name, string exe_path,
        string? args, string? working_dir, string? timestamp);
}
