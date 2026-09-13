using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

const string DefaultBambuPath = @"C:\Program Files\Bambu Studio\bambu-studio.exe";

try
{
    BridgeTask task = BridgeTask.Parse(args);
    if (!File.Exists(task.FilePath))
    {
        throw new FileNotFoundException("Export file was not found.", task.FilePath);
    }

    Log($"Received: {task.FilePath}; newWindow={task.NewWindow}");
    if (!task.NewWindow && BambuWindow.TryDropFile(task.FilePath, out nint window))
    {
        Log($"Delivered to Bambu window 0x{window:X}.");
        return;
    }

    string bambuPath = ResolveBambuPath(task.BambuPath);
    var startInfo = new ProcessStartInfo
    {
        FileName = bambuPath,
        UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(bambuPath) ?? Environment.CurrentDirectory
    };
    if (task.NewWindow)
    {
        startInfo.ArgumentList.Add("--no-single-instance");
    }
    startInfo.ArgumentList.Add(task.FilePath);
    _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Bambu Studio.");
    Log($"Started Bambu: {string.Join(" ", startInfo.ArgumentList)}");
}
catch (Exception error)
{
    Log($"Failed: {error}");
    Environment.ExitCode = 1;
}

static string ResolveBambuPath(string? configuredPath)
{
    if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
    {
        return configuredPath;
    }

    string? envPath = Environment.GetEnvironmentVariable("BAMBU_STUDIO_EXE");
    if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
    {
        return envPath;
    }

    if (File.Exists(DefaultBambuPath))
    {
        return DefaultBambuPath;
    }

    throw new FileNotFoundException("Bambu Studio executable was not found.", DefaultBambuPath);
}

static void Log(string message)
{
    File.AppendAllText(
        Path.Combine(Path.GetTempPath(), "kompas-bambu-bridge.log"),
        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
}

internal sealed record BridgeTask(string FilePath, bool NewWindow, string? BambuPath)
{
    public static BridgeTask Parse(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "open", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Usage: kompas-bambu-bridge open --file <path> [--new-window] [--bambu <path>]");
        }

        string? filePath = null;
        string? bambuPath = null;
        bool newWindow = false;
        for (int index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--file":
                    filePath = args[++index];
                    break;
                case "--new-window":
                    newWindow = true;
                    break;
                case "--bambu":
                    bambuPath = args[++index];
                    break;
                default:
                    throw new ArgumentException($"Unknown bridge argument: {args[index]}");
            }
        }

        return new BridgeTask(filePath ?? throw new ArgumentException("--file is required."), newWindow, bambuPath);
    }
}

internal static class BambuWindow
{
    private const uint WmDropFiles = 0x0233;
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;
    private const uint SmtoAbortIfHung = 0x0002;

    public static bool TryDropFile(string filePath, out nint windowHandle)
    {
        windowHandle = nint.Zero;
        nint candidate = nint.Zero;
        EnumWindows((handle, _) =>
        {
            var className = new StringBuilder(64);
            var title = new StringBuilder(512);
            _ = GetClassName(handle, className, className.Capacity);
            _ = GetWindowText(handle, title, title.Capacity);
            if (string.Equals(className.ToString(), "wxWindowNR", StringComparison.Ordinal)
                && title.ToString().Contains("BambuStudio", StringComparison.OrdinalIgnoreCase))
            {
                candidate = handle;
                return false;
            }
            return true;
        }, nint.Zero);

        if (candidate == nint.Zero)
        {
            return false;
        }

        BringIntoView(candidate);
        byte[] paths = Encoding.Unicode.GetBytes(filePath + "\0\0");
        int headerSize = Marshal.SizeOf<DropFiles>();
        nint hDrop = GlobalAlloc(GmemMoveable | GmemZeroInit, (nuint)(headerSize + paths.Length));
        if (hDrop == nint.Zero)
        {
            return false;
        }

        try
        {
            nint locked = GlobalLock(hDrop);
            if (locked == nint.Zero)
            {
                return false;
            }
            try
            {
                Marshal.StructureToPtr(new DropFiles { FileOffset = (uint)headerSize, Wide = 1 }, locked, false);
                Marshal.Copy(paths, 0, locked + headerSize, paths.Length);
            }
            finally { _ = GlobalUnlock(hDrop); }

            nint result;
            bool delivered = SendMessageTimeout(candidate, WmDropFiles, hDrop, nint.Zero, SmtoAbortIfHung, 2000, out result) != nint.Zero;
            if (delivered)
            {
                hDrop = nint.Zero;
                windowHandle = candidate;
                return true;
            }
            return false;
        }
        finally
        {
            if (hDrop != nint.Zero) _ = GlobalFree(hDrop);
        }
    }

    private static void BringIntoView(nint windowHandle)
    {
        const uint MonitorDefaultToNull = 0;
        const uint SwpNoZOrder = 0x0004;
        const uint SwpShowWindow = 0x0040;
        _ = ShowWindow(windowHandle, 9);
        if (MonitorFromWindow(windowHandle, MonitorDefaultToNull) == nint.Zero)
        {
            _ = SetWindowPos(windowHandle, nint.Zero, 100, 100, 1500, 950, SwpNoZOrder | SwpShowWindow);
        }
        _ = SetForegroundWindow(windowHandle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DropFiles { public uint FileOffset; public int X; public int Y; public int NonClientArea; public int Wide; }
    private delegate bool EnumWindowsCallback(nint handle, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint handle, StringBuilder className, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder title, int capacity);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SendMessageTimeout(nint handle, uint message, nint wParam, nint lParam, uint flags, uint timeoutMilliseconds, out nint result);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint handle, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
}
