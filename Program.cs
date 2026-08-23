using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

return new App().Run(args);

internal sealed class App
{
    private const string DefaultBambuPath = @"C:\Program Files\Bambu Studio\bambu-studio.exe";
    private const string DefaultOutputFolderName = "print";
    private const short FormatStep = 3;
    private const short FormatStl = 6;
    private const int StepAp203 = 203;

    public int Run(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);

            if (options.ShowHelp)
            {
                Options.PrintHelp();
                return 0;
            }

            object kompas = Com.GetActiveObject("KOMPAS.Application.7");
            object document = GetActive3DDocument(kompas);
            DocumentInfo documentInfo = GetDocumentInfo(document);
            string exportPath = BuildExportPath(documentInfo, options);

            Export(kompas, document, documentInfo, exportPath, options.Format);
            Console.WriteLine($"Exported: {exportPath}");

            if (options.OpenBambu)
            {
                OpenInBambu(exportPath, options.BambuPath);
                Console.WriteLine("Bambu Studio launched.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("kompas-bambu failed:");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static object GetActive3DDocument(object kompas)
    {
        object? document = Com.TryInvoke(kompas, "ActiveDocument3D")
            ?? Com.TryGet(kompas, "ActiveDocument3D")
            ?? Com.TryGet(kompas, "ActiveDocument");

        if (document is null)
        {
            throw new InvalidOperationException("No active 3D document found. Open a part or assembly in KOMPAS-3D first.");
        }

        return document;
    }

    private static DocumentInfo GetDocumentInfo(object document)
    {
        string? fileName = Com.TryGetString(document, "fileName")
            ?? Com.TryGetString(document, "FileName")
            ?? Com.TryInvokeString(document, "GetFileName");

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            string? directory = Path.GetDirectoryName(fileName);
            string name = Path.GetFileNameWithoutExtension(fileName);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                return new DocumentInfo(name, directory, fileName);
            }
        }

        string? nameFromDoc = Com.TryGetString(document, "name")
            ?? Com.TryGetString(document, "Name");

        return new DocumentInfo(
            string.IsNullOrWhiteSpace(nameFromDoc) ? "kompas-part" : nameFromDoc,
            Path.Combine(Path.GetTempPath(), "kompas-bambu"),
            FullPath: null);
    }

    private static string BuildExportPath(DocumentInfo documentInfo, Options options)
    {
        string safeName = SanitizeFileName(documentInfo.Name);
        string extension = options.Format == ExportFormat.Step ? ".step" : ".stl";
        string outputFolderName = string.IsNullOrWhiteSpace(options.OutputFolderName)
            ? DefaultOutputFolderName
            : options.OutputFolderName;
        string exportDirectory = Path.Combine(documentInfo.Directory, SanitizeFolderName(outputFolderName));

        Directory.CreateDirectory(exportDirectory);

        return Path.Combine(exportDirectory, $"{safeName}{extension}");
    }

    private static string SanitizeFileName(string value)
    {
        string sanitized = Regex.Replace(value, @"[^\w\-. ]+", "_");
        sanitized = sanitized.Trim(' ', '.', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "kompas-part" : sanitized;
    }

    private static string SanitizeFolderName(string value)
    {
        string sanitized = SanitizeFileName(value);
        return sanitized.Equals("kompas-part", StringComparison.OrdinalIgnoreCase) ? DefaultOutputFolderName : sanitized;
    }

    private static void Export(object kompas, object document, DocumentInfo documentInfo, string exportPath, ExportFormat format)
    {
        Exception? api5Error = null;

        try
        {
            ExportWithApi5(document, exportPath, format);
        }
        catch (Exception ex)
        {
            api5Error = ex;
        }

        if (File.Exists(exportPath) && new FileInfo(exportPath).Length > 0)
        {
            return;
        }

        try
        {
            ExportWithApi7(kompas, document, exportPath, format);
        }
        catch (Exception api7Error)
        {
            try
            {
                ExportWithConverterFile(kompas, documentInfo, exportPath, format);
            }
            catch (Exception converterError)
            {
                throw new InvalidOperationException(
                    "Export failed through KOMPAS APIs. " +
                    $"API5: {api5Error?.Message ?? "not attempted"}. " +
                    $"API7 document: {api7Error.Message}. " +
                    $"API7 converter: {converterError.Message}");
            }
        }
    }

    private static void ExportWithConverterFile(object kompas, DocumentInfo documentInfo, string exportPath, ExportFormat format)
    {
        if (string.IsNullOrWhiteSpace(documentInfo.FullPath) || !File.Exists(documentInfo.FullPath))
        {
            throw new InvalidOperationException(
                "The active KOMPAS document is not saved. Save it once before using file-based converter export.");
        }

        object converter = GetConverter(kompas);
        int command = format == ExportFormat.Step ? StepAp203 : FormatStl;
        object result = Com.Invoke(converter, "Convert", documentInfo.FullPath, exportPath, command, false);

        if (result is int code && code != 0)
        {
            throw new InvalidOperationException($"KOMPAS converter returned code {code}.");
        }

        if (!File.Exists(exportPath) || new FileInfo(exportPath).Length == 0)
        {
            throw new InvalidOperationException($"Export file was not created or is empty: {exportPath}");
        }
    }

    private static void ExportWithApi5(object document, string exportPath, ExportFormat format)
    {
        object param = Com.Invoke(document, "AdditionFormatParam");
        Com.TryInvoke(param, "Init");

        short kompasFormat = format == ExportFormat.Step ? FormatStep : FormatStl;
        SetParam(param, "format", kompasFormat);
        SetParam(param, "topolgyIncluded", true);
        ConfigureExportObjects(param);

        if (format == ExportFormat.Step)
        {
            SetParam(param, "stepType", StepAp203);
        }
        else
        {
            SetParam(param, "formatBinary", true);
            SetParam(param, "lengthUnits", 1);
            SetParam(param, "angle", 0.08726646259971647);
            SetParam(param, "length", 0.02);
            SetParam(param, "maxTeselationCellCount", 0);
        }

        object result = Com.Invoke(document, "SaveAsToAdditionFormat", exportPath, param);

        if (result is bool ok && !ok)
        {
            throw new InvalidOperationException($"KOMPAS returned false while exporting {exportPath}.");
        }

        if (!File.Exists(exportPath) || new FileInfo(exportPath).Length == 0)
        {
            throw new InvalidOperationException($"Export file was not created or is empty: {exportPath}");
        }
    }

    private static void ExportWithApi7(object kompas, object document, string exportPath, ExportFormat format)
    {
        short kompasFormat = format == ExportFormat.Step ? FormatStep : FormatStl;
        object param = CreateAdditionConvertParameters(kompas, kompasFormat);
        Com.TryInvoke(param, "Clear");

        SetParam(param, "Format", kompasFormat);
        SetParam(param, "TopolgyIncluded", true);
        ConfigureExportObjects(param);

        if (format == ExportFormat.Step)
        {
            SetParam(param, "StepType", StepAp203);
        }
        else
        {
            SetParam(param, "FormatBinary", true);
            SetParam(param, "LengthUnits", 1);
            SetParam(param, "Angle", 0.08726646259971647);
            SetParam(param, "Length", 0.02);
            SetParam(param, "MaxTeselationCellCount", 0);
        }

        object result = Com.Invoke(document, "ConvertToAdditionFormat", exportPath, param);

        if (result is bool ok && !ok)
        {
            throw new InvalidOperationException($"KOMPAS returned false while exporting {exportPath}.");
        }

        if (!File.Exists(exportPath) || new FileInfo(exportPath).Length == 0)
        {
            throw new InvalidOperationException($"Export file was not created or is empty: {exportPath}");
        }
    }

    private static object CreateAdditionConvertParameters(object kompas, short command)
    {
        object converter = GetConverter(kompas);
        return Com.Invoke(converter, "ConverterParameters", (int)command);
    }

    private static object GetConverter(object kompas)
    {
        return Com.TryGet(kompas, "Converter", string.Empty)
            ?? Com.TryGet(kompas, "Converter", Type.Missing)
            ?? Com.TryGet(kompas, "Converter")
            ?? throw new InvalidOperationException("Failed to get KOMPAS converter object from API7 application.");
    }

    private static void ConfigureExportObjects(object param)
    {
        SetObjectOption(param, 0, true);

        foreach (int option in new[] { 2, 4, 6, 8, 10, 12, 14, 16, 18 })
        {
            SetObjectOption(param, option, false);
        }
    }

    private static void SetObjectOption(object param, int option, bool enabled)
    {
        if (Com.TryInvokeMethod(param, "SetObjectsOptions", option, enabled))
        {
            return;
        }

        Com.TrySet(param, "ObjectsOptions", option, enabled);
    }

    private static void SetParam(object target, string name, object value)
    {
        if (Com.TrySet(target, name, value))
        {
            return;
        }

        string setter = "Set" + char.ToUpperInvariant(name[0]) + name[1..];
        if (Com.TryInvokeMethod(target, setter, value))
        {
            return;
        }

        throw new InvalidOperationException($"Failed to set KOMPAS export parameter '{name}'.");
    }

    private static void OpenInBambu(string filePath, string? configuredPath)
    {
        string bambuPath = ResolveBambuPath(configuredPath);

        Process.Start(new ProcessStartInfo
        {
            FileName = bambuPath,
            ArgumentList = { filePath },
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(bambuPath) ?? Environment.CurrentDirectory
        });
    }

    private static string ResolveBambuPath(string? configuredPath)
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

        throw new FileNotFoundException(
            "Bambu Studio executable was not found. Pass --bambu \"path\\to\\bambu-studio.exe\" or set BAMBU_STUDIO_EXE.");
    }
}

internal enum ExportFormat
{
    Step,
    Stl
}

internal sealed record DocumentInfo(string Name, string Directory, string? FullPath);

internal sealed record Options(
    ExportFormat Format,
    bool OpenBambu,
    string? BambuPath,
    string OutputFolderName,
    bool ShowHelp)
{
    public static Options Parse(string[] args)
    {
        ExportFormat format = ExportFormat.Step;
        bool openBambu = true;
        string? bambuPath = null;
        string outputFolderName = "print";

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].Trim();

            switch (arg.ToLowerInvariant())
            {
                case "step":
                case "--step":
                    format = ExportFormat.Step;
                    break;
                case "stl":
                case "--stl":
                    format = ExportFormat.Stl;
                    break;
                case "export":
                case "--export-only":
                    openBambu = false;
                    break;
                case "open":
                case "--open":
                    openBambu = true;
                    break;
                case "--bambu":
                    bambuPath = ReadNext(args, ref i, "--bambu");
                    break;
                case "--out-dir":
                    outputFolderName = ReadNext(args, ref i, "--out-dir");
                    break;
                case "-h":
                case "--help":
                case "/?":
                    return new Options(format, openBambu, bambuPath, outputFolderName, ShowHelp: true);
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return new Options(format, openBambu, bambuPath, outputFolderName, ShowHelp: false);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
        kompas-bambu

        Usage:
          kompas-bambu [step|stl] [open|export] [--out-dir <name>] [--bambu <path>]

        Defaults:
          format: step, STEP AP203
          output: <KOMPAS file folder>\print\<same-name>.step
          action: open in Bambu Studio after export

        Examples:
          kompas-bambu
          kompas-bambu stl
          kompas-bambu step export
          kompas-bambu step --out-dir print
          kompas-bambu step --bambu "C:\Program Files\Bambu Studio\bambu-studio.exe"
        """);
    }

    private static string ReadNext(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[++index];
    }
}

internal static class Com
{
    private const int SOk = 0;

    public static object GetActiveObject(string progId)
    {
        int hr = CLSIDFromProgID(progId, out Guid clsid);
        if (hr != SOk)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        hr = GetActiveObject(ref clsid, IntPtr.Zero, out object? obj);
        if (hr != SOk || obj is null)
        {
            throw new InvalidOperationException(
                $"Could not connect to {progId}. Start KOMPAS-3D and open a 3D document first.");
        }

        return obj;
    }

    public static object Invoke(object target, string name, params object?[] args)
    {
        try
        {
            return target.GetType().InvokeMember(
                name,
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
                binder: null,
                target,
                args,
                CultureInfo.InvariantCulture)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    public static object? TryInvoke(object target, string name, params object?[] args)
    {
        try
        {
            return Invoke(target, name, args);
        }
        catch
        {
            return null;
        }
    }

    public static bool TryInvokeMethod(object target, string name, params object?[] args)
    {
        try
        {
            Invoke(target, name, args);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static object? TryGet(object target, string name)
    {
        return TryGet(target, name, Array.Empty<object>());
    }

    public static object? TryGet(object target, string name, params object?[] args)
    {
        try
        {
            return target.GetType().InvokeMember(
                name,
                BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
                binder: null,
                target,
                args,
                CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public static string? TryGetString(object target, string name)
    {
        return TryGet(target, name)?.ToString();
    }

    public static string? TryInvokeString(object target, string name, params object?[] args)
    {
        return TryInvoke(target, name, args)?.ToString();
    }

    public static bool TrySet(object target, string name, object value)
    {
        return TrySet(target, name, new[] { value });
    }

    public static bool TrySet(object target, string name, object index, object value)
    {
        return TrySet(target, name, new[] { index, value });
    }

    private static bool TrySet(object target, string name, object[] args)
    {
        try
        {
            target.GetType().InvokeMember(
                name,
                BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase,
                binder: null,
                target,
                args,
                CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string lpszProgID, out Guid lpclsid);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(ref Guid rclsid, IntPtr pvReserved, [MarshalAs(UnmanagedType.IUnknown)] out object? ppunk);
}
