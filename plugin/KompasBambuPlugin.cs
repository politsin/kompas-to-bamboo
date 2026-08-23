using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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

        [return: MarshalAs(UnmanagedType.BStr)]
        public string GetLibraryName()
        {
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
                    return "Bambu STEP\tCtrl+Shift+S";
                case 2:
                    command = CommandBambuStl;
                    return "Bambu STL";
                case 3:
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
