using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

ApplicationConfiguration.Initialize();
Application.Run(new HotkeyContext());

internal sealed class HotkeyContext : ApplicationContext
{
    private const int HotkeyId = 0xBABA;
    private const int ModShift = 0x0004;
    private const int ModControl = 0x0002;
    private const int VkS = 0x53;

    private readonly HotkeyWindow _window = new();

    public HotkeyContext()
    {
        if (!RegisterHotKey(_window.Handle, HotkeyId, ModControl | ModShift, VkS))
        {
            MessageBox.Show("Ctrl+Shift+S is already registered by another app.", "kompas-bambu hotkey");
            ExitThread();
            return;
        }

        _window.HotkeyPressed += RunExporterIfKompasIsActive;
    }

    protected override void Dispose(bool disposing)
    {
        UnregisterHotKey(_window.Handle, HotkeyId);
        _window.Dispose();
        base.Dispose(disposing);
    }

    private static void RunExporterIfKompasIsActive()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return;
        }

        GetWindowThreadProcessId(foreground, out uint processId);
        string processName = Process.GetProcessById((int)processId).ProcessName;

        if (!processName.Contains("kompas", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string exe = Path.Combine(AppContext.BaseDirectory, "kompas-bambu.exe");
        Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "step",
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        });
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());
    }

    public event Action? HotkeyPressed;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey)
        {
            HotkeyPressed?.Invoke();
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        DestroyHandle();
    }
}
