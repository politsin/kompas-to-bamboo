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
            if (request.Operation == "open" && BambuDelivery.TryDropFile(request.FilePath, out nint window))
            {
                Log("delivered-existing-window", request, $"window=0x{window:X}");
                WriteStatus(request, "delivered-existing-window", null);
                return;
            }

            string bambu = ResolveBambuPath(request.BambuPath);
            var start = new ProcessStartInfo { FileName = bambu, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(bambu) ?? Environment.CurrentDirectory };
            if (request.Operation == "new-window") start.ArgumentList.Add("--no-single-instance");
            start.ArgumentList.Add(request.FilePath);
            _ = Process.Start(start) ?? throw new InvalidOperationException("Could not start Bambu Studio.");
            Log("started-bambu", request, string.Join(" ", start.ArgumentList));
            WriteStatus(request, "started-bambu", null);
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
    private const uint WmDropFiles = 0x0233, GmemMoveable = 0x0002, GmemZeroInit = 0x0040, SmtoAbortIfHung = 0x0002;

    public static bool TryDropFile(string filePath, out nint window)
    {
        window = nint.Zero;
        nint target = nint.Zero;
        EnumWindows((handle, _) =>
        {
            var className = new StringBuilder(64); var title = new StringBuilder(512);
            _ = GetClassName(handle, className, className.Capacity); _ = GetWindowText(handle, title, title.Capacity);
            if (className.ToString() == "wxWindowNR" && title.ToString().Contains("BambuStudio", StringComparison.OrdinalIgnoreCase)) { target = handle; return false; }
            return true;
        }, nint.Zero);
        if (target == nint.Zero) return false;

        BringIntoView(target);
        byte[] paths = Encoding.Unicode.GetBytes(filePath + "\0\0");
        int header = Marshal.SizeOf<DropFiles>();
        nint hDrop = GlobalAlloc(GmemMoveable | GmemZeroInit, (nuint)(header + paths.Length));
        if (hDrop == nint.Zero) return false;
        try
        {
            nint memory = GlobalLock(hDrop); if (memory == nint.Zero) return false;
            try { Marshal.StructureToPtr(new DropFiles { FileOffset = (uint)header, Wide = 1 }, memory, false); Marshal.Copy(paths, 0, memory + header, paths.Length); }
            finally { _ = GlobalUnlock(hDrop); }
            if (SendMessageTimeout(target, WmDropFiles, hDrop, nint.Zero, SmtoAbortIfHung, 2000, out _) == nint.Zero) return false;
            hDrop = nint.Zero; window = target; return true;
        }
        finally { if (hDrop != nint.Zero) _ = GlobalFree(hDrop); }
    }

    private static void BringIntoView(nint target)
    {
        _ = ShowWindow(target, 9);
        if (MonitorFromWindow(target, 0) == nint.Zero) _ = SetWindowPos(target, nint.Zero, 100, 100, 1500, 950, 0x0004 | 0x0040);
        _ = SetForegroundWindow(target);
    }

    [StructLayout(LayoutKind.Sequential)] private struct DropFiles { public uint FileOffset; public int X; public int Y; public int NonClientArea; public int Wide; }
    private delegate bool WindowCallback(nint handle, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint handle, StringBuilder className, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder title, int capacity);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint handle, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint handle, uint flags);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
}
