using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

var bridge = new BridgeServer("KompasBambuBridge.v1");
await bridge.RunAsync();

internal sealed class BridgeServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _pipeName;
    private readonly string _root;

    public BridgeServer(string pipeName)
    {
        _pipeName = pipeName;
        // Keep diagnostics beside the actual deployed bridge binary. This makes
        // it unambiguous which build received a task.
        _root = AppContext.BaseDirectory;
        Directory.CreateDirectory(_root);
    }

    public async Task RunAsync()
    {
        Log("started", null, null);
        while (true)
        {
            using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync();
            await HandleAsync(pipe);
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        string? message = await reader.ReadLineAsync();
        BridgeRequest? request = message is null ? null : JsonSerializer.Deserialize<BridgeRequest>(message, JsonOptions);
        if (request is null || string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.FilePath))
        {
            await writer.WriteLineAsync("rejected:invalid-request");
            Log("rejected", null, "invalid request");
            return;
        }

        await writer.WriteLineAsync($"accepted:{request.Id}");
        Log("accepted", request, null);
        _ = Task.Run(() => Execute(request));
    }

    private void Execute(BridgeRequest request)
    {
        try
        {
            if (!File.Exists(request.FilePath)) throw new FileNotFoundException("Export file is missing.", request.FilePath);
            string bambu = ResolveBambuPath(request.BambuPath);
            if (request.Operation == "open" && BambuDelivery.TrySendToExistingWindow(bambu, request.FilePath, out nint window))
            {
                Log("delivered-existing-window", request, $"window=0x{window:X}");
                WriteStatus(request, "delivered-existing-window", null);
                return;
            }

            var start = new ProcessStartInfo { FileName = bambu, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(bambu) ?? Environment.CurrentDirectory };
            // Bambu Studio 2.8.2.61 creates an independent window when launched
            // with a model path. Its --*-single-instance switches exit with -2.
            start.ArgumentList.Add(request.FilePath);
            Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Bambu Studio.");
            string state = request.Operation == "new-window" ? "started-new-window" : "started-bambu";
            Log(state, request, $"pid={process.Id}; args={string.Join(" ", start.ArgumentList)}");
            WriteStatus(request, state, null);
        }
        catch (Exception error)
        {
            Log("failed", request, error.ToString());
            WriteStatus(request, "failed", error.Message);
        }
    }

    private static string ResolveBambuPath(string? configuredPath)
    {
        string? envPath = Environment.GetEnvironmentVariable("BAMBU_STUDIO_EXE");
        string[] candidates = [configuredPath ?? string.Empty, envPath ?? string.Empty, @"C:\Program Files\Bambu Studio\bambu-studio.exe"];
        return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Bambu Studio executable was not found.");
    }

    private void Log(string eventName, BridgeRequest? request, string? detail)
    {
        var entry = new { timestamp = DateTimeOffset.Now, processId = Environment.ProcessId, eventName, request, detail };
        string path = Path.Combine(_root, $"bridge-{DateTime.Today:yyyy-MM-dd}.jsonl");
        File.AppendAllText(path, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
    }

    private void WriteStatus(BridgeRequest request, string state, string? error)
    {
        File.WriteAllText(Path.Combine(_root, "bridge-status.json"), JsonSerializer.Serialize(new { updatedAt = DateTimeOffset.Now, request, state, error }, JsonOptions));
    }
}

internal sealed record BridgeRequest(string Id, string FilePath, string Operation, string? BambuPath);

internal static class BambuDelivery
{
    private const uint WmCopyData = 0x004A, SmtoAbortIfHung = 0x0002;

    public static bool TrySendToExistingWindow(string bambuPath, string filePath, out nint window)
    {
        window = nint.Zero;
        nint target = nint.Zero;
        string expectedPath = Path.GetFullPath(bambuPath);
        EnumWindows((handle, _) =>
        {
            var className = new StringBuilder(64);
            var title = new StringBuilder(512);
            _ = GetClassName(handle, className, className.Capacity);
            _ = GetWindowText(handle, title, title.Capacity);
            if (className.ToString() != "wxWindowNR" || !title.ToString().Contains("BambuStudio", StringComparison.OrdinalIgnoreCase)) return true;

            GetWindowThreadProcessId(handle, out uint processId);
            try
            {
                using Process process = Process.GetProcessById((int)processId);
                if (!string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? string.Empty), expectedPath, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch
            {
                return true;
            }

            target = handle;
            return false;
        }, nint.Zero);
        if (target == nint.Zero) return false;

        // InstanceCheck.cpp sends an escaped argv list by WM_COPYDATA.  The first
        // entry is the executable and every following entry that exists is loaded.
        string message = $"{EscapeCStyleArgument(expectedPath)} {EscapeCStyleArgument(Path.GetFullPath(filePath))}";
        byte[] payload = Encoding.Unicode.GetBytes(message + '\0');
        GCHandle pinnedPayload = GCHandle.Alloc(payload, GCHandleType.Pinned);
        try
        {
            var data = new CopyDataStruct { Data = 1, Bytes = payload.Length, Pointer = pinnedPayload.AddrOfPinnedObject() };
            if (SendMessageTimeout(target, WmCopyData, nint.Zero, ref data, SmtoAbortIfHung, 5000, out _) == nint.Zero) return false;
            window = target;
            return true;
        }
        finally
        {
            pinnedPayload.Free();
        }
    }

    private static string EscapeCStyleArgument(string value) =>
        '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct { public nuint Data; public int Bytes; public nint Pointer; }

    private delegate bool WindowCallback(nint handle, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint handle, StringBuilder className, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder title, int capacity);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint handle, uint message, nint wParam, ref CopyDataStruct lParam, uint flags, uint timeout, out nint result);
}
