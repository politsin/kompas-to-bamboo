using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;

namespace KompasBambu
{
    [ComVisible(true)]
    [Guid("DE7E8C03-25F4-497E-92EE-1C52B93A06E4")]
    [ProgId("KompasBambu.Plugin")]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public sealed class KompasBambuPlugin
    {
        private const short CommandBambuStep = 1;
        private const short CommandBambuStl = 2;
        private const short CommandEnableShortcut = 3;

        private static KeyboardHook keyboardHook;

        [return: MarshalAs(UnmanagedType.BStr)]
        public string GetLibraryName()
        {
            TryInstallKeyboardHook(null, false);
            return "Bambu Studio";
        }

        [return: MarshalAs(UnmanagedType.BStr)]
        public string ExternalMenuItem(short number, ref short itemType, ref short command)
        {
            itemType = 1;

            switch (number)
            {
                case 1:
                    command = CommandBambuStep;
                    return "Bambu STEP";
                case 2:
                    command = CommandBambuStl;
                    return "Bambu STL";
                case 3:
                    command = CommandEnableShortcut;
                    return "Enable Ctrl+Shift+S";
                case 4:
                    itemType = 3;
                    command = -1;
                    return string.Empty;
                default:
                    itemType = 3;
                    command = -1;
                    return string.Empty;
            }
        }

        public void ExternalRunCommand(
            [In] short command,
            [In] short mode,
            [In, MarshalAs(UnmanagedType.IDispatch)] object kompas)
        {
            try
            {
                if (command == CommandEnableShortcut)
                {
                    TryInstallKeyboardHook(kompas, true);
                    return;
                }

                TryInstallKeyboardHook(kompas, false);
                string exe = FindExporter();
                string format = command == CommandBambuStl ? "stl" : "step";

                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = format,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("KOMPAS Bambu command failed:\n" + ex.Message, "Bambu Studio");
            }
        }

        public short GetProtectNumber()
        {
            return 111;
        }

        private static string FindExporter()
        {
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string distDir = Path.GetFullPath(Path.Combine(pluginDir, ".."));
            string exe = Path.Combine(distDir, "kompas-bambu.exe");

            if (File.Exists(exe))
            {
                return exe;
            }

            throw new FileNotFoundException("kompas-bambu.exe was not found next to the plugin.", exe);
        }

        private static void RunExporter(string format)
        {
            string exe = FindExporter();

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = format,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)
            });
        }

        private static void TryInstallKeyboardHook(object kompas, bool showResult)
        {
            if (keyboardHook != null)
            {
                if (showResult)
                {
                    MessageBox.Show("Ctrl+Shift+S is already enabled for this KOMPAS session.", "Bambu Studio");
                }

                return;
            }

            try
            {
                object application = kompas ?? Marshal.GetActiveObject("KOMPAS.Application.7");
                keyboardHook = new KeyboardHook(application);

                if (showResult)
                {
                    MessageBox.Show("Ctrl+Shift+S is enabled for this KOMPAS session.", "Bambu Studio");
                }
            }
            catch (Exception ex)
            {
                keyboardHook = null;

                if (showResult)
                {
                    MessageBox.Show("Could not enable KOMPAS shortcut:\n" + ex.Message, "Bambu Studio");
                }
            }
        }

        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        public sealed class KeyboardHook : IKompasObjectNotify, IDisposable
        {
            private readonly IConnectionPoint connectionPoint;
            private readonly int cookie;
            private bool disposed;
            private DateTime lastRunUtc = DateTime.MinValue;

            public KeyboardHook(object application)
            {
                IConnectionPointContainer container = (IConnectionPointContainer)application;
                Guid eventGuid = typeof(IKompasObjectNotify).GUID;
                container.FindConnectionPoint(ref eventGuid, out connectionPoint);
                connectionPoint.Advise(this, out cookie);
            }

            public bool KeyDown(ref int key, int flags, bool system)
            {
                if (key == (int)Keys.S && Control.ModifierKeys == (Keys.Control | Keys.Shift))
                {
                    key = 0;

                    DateTime now = DateTime.UtcNow;
                    if ((now - lastRunUtc).TotalMilliseconds > 750)
                    {
                        lastRunUtc = now;

                        try
                        {
                            RunExporter("step");
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("KOMPAS Bambu shortcut failed:\n" + ex.Message, "Bambu Studio");
                        }
                    }

                    return false;
                }

                return true;
            }

            public bool ApplicationDestroy()
            {
                Dispose();
                keyboardHook = null;
                return true;
            }

            public void Dispose()
            {
                if (!disposed && connectionPoint != null && cookie != 0)
                {
                    disposed = true;
                    connectionPoint.Unadvise(cookie);
                }
            }
        }

        [ComVisible(true)]
        [Guid("C7CB743A-C59D-4C27-8CB6-971C2A393F2F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        public interface IKompasObjectNotify
        {
            [DispId(5)]
            bool ApplicationDestroy();

            [DispId(9)]
            bool KeyDown(ref int key, int flags, bool system);
        }

        [ComRegisterFunction]
        public static void RegisterKompasLib(Type t)
        {
            try
            {
                using (RegistryKey classes = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", true))
                using (RegistryKey clsid = classes.OpenSubKey(@"CLSID\{" + t.GUID + "}", true))
                {
                    clsid.CreateSubKey("Kompas_Library").Close();

                    using (RegistryKey inproc = clsid.OpenSubKey("InprocServer32", true))
                    {
                        inproc.SetValue(null, Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\mscoree.dll");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("KOMPAS Bambu plugin registration failed:\n" + ex);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterKompasLib(Type t)
        {
            using (RegistryKey classes = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", true))
            using (RegistryKey clsid = classes.OpenSubKey(@"CLSID\{" + t.GUID + "}", true))
            {
                if (clsid != null)
                {
                    clsid.DeleteSubKey("Kompas_Library", false);
                }
            }
        }
    }
}
