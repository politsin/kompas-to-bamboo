using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

return new App().Run(args);

internal sealed class App
{
    private const string DefaultBambuPath = @"C:\Program Files\Bambu Studio\bambu-studio.exe";
    private const string DefaultOutputFolderName = "fdm";
    private const string DefaultCamOutputFolderName = "cnc";
    private const string DefaultLaserOutputFolderName = "laser";
    private const string DefaultAllSketchesOutputFolderName = "dfx";
    private const short FormatStep = 3;
    private const short FormatStl = 6;
    private const int StepAp203 = 203;
    private const int TopPart = -1;
    private const int Obj3dSketch = 5;
    private const int DocFragment = 3;
    private const int All2DObjects = 0;
    private const int LineSegObj = 1;
    private const int CircleObj = 2;
    private const int ArcObj = 3;
    private const int BasicLineStyle = 1;
    private const int AllParam = -1;
    private const int KoLineSegParam = 11;
    private const int KoArcByAngleParam = 12;
    private const int KoCircleParam = 20;
    private const int KoDocumentParam = 35;

    public int Run(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            Log($"Start: {string.Join(" ", args)}");
            if (options.CallerProcessId is not null)
            {
                Log($"Caller KOMPAS PID: {options.CallerProcessId}");
            }
            if (!string.IsNullOrWhiteSpace(options.ExpectedDocumentPath))
            {
                Log($"Expected document from RTW: {options.ExpectedDocumentPath}");
            }

            if (options.ShowHelp)
            {
                Options.PrintHelp();
                return 0;
            }

            object kompas7 = Com.GetActiveObjectWithRetry(
                "KOMPAS.Application.7",
                options.CallerProcessId is null ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(10));
            object? kompas5 = Com.TryGetActiveObject("KOMPAS.Application.5");

            if (!string.IsNullOrWhiteSpace(options.ExpectedDocumentPath) && !options.SketchDxf && !options.AllSketchesDxf)
            {
                DocumentInfo expectedDocument = DocumentInfo.FromPath(Path.GetFullPath(options.ExpectedDocumentPath));
                string exportPathFromExpectedDocument = BuildExportPath(expectedDocument, options);
                ExportWithConverterFile(kompas7, expectedDocument, exportPathFromExpectedDocument, options.Format);
                Console.WriteLine($"Exported: {exportPathFromExpectedDocument}");
                Log($"Exported from RTW document path: {exportPathFromExpectedDocument}");

                if (options.SendToBridge)
                {
                    SubmitToBridge(exportPathFromExpectedDocument, options);
                }

                return 0;
            }

            object document = GetActive3DDocument(kompas5 ?? kompas7);
            DocumentInfo documentInfo = GetDocumentInfo(document);
            Log($"COM active document: {documentInfo.FullPath ?? documentInfo.Name}");

            if (!string.IsNullOrWhiteSpace(options.ExpectedDocumentPath))
            {
                string expectedPath = Path.GetFullPath(options.ExpectedDocumentPath);
                if (!PathsEqual(documentInfo.FullPath, expectedPath))
                {
                    throw new InvalidOperationException(
                        "KOMPAS COM returned a different active document than the RTW command source. " +
                        $"RTW document: {expectedPath}. COM document: {documentInfo.FullPath ?? "<unsaved>"}. " +
                        "Close duplicate KOMPAS windows and retry.");
                }
            }

            if (options.SketchDxf)
            {
                object kompasApi5 = kompas5 ?? throw new InvalidOperationException("KOMPAS API5 is required for sketch DXF export.");
                object sketch = FindSketch(document);
                string dxfExportPath = BuildSketchDxfPath(documentInfo, options, sketch);
                SketchExportStats stats = ExportSketchDxf(kompasApi5, sketch, documentInfo, dxfExportPath);
                Console.WriteLine($"Exported: {dxfExportPath}");
                Console.WriteLine($"Copied curves: {stats.Copied}, skipped: {stats.Skipped}");
                Log($"Exported sketch DXF: {dxfExportPath}. Copied={stats.Copied}, skipped={stats.Skipped}");
                return 0;
            }

            if (options.AllSketchesDxf)
            {
                object kompasApi5 = kompas5 ?? throw new InvalidOperationException("KOMPAS API5 is required for sketch DXF export.");
                List<object> sketches = FindAllSketches(document);
                string exportDirectory = BuildSketchDxfDirectory(documentInfo, options);
                int exported = 0;
                int copied = 0;
                int skipped = 0;

                for (int i = 0; i < sketches.Count; i++)
                {
                    object sketch = sketches[i];
                    string dxfExportPath = BuildSketchDxfPath(documentInfo, exportDirectory, sketch, i + 1);
                    SketchExportStats stats = ExportSketchDxf(kompasApi5, sketch, documentInfo, dxfExportPath);
                    exported++;
                    copied += stats.Copied;
                    skipped += stats.Skipped;
                    Console.WriteLine($"Exported: {dxfExportPath}");
                    Console.WriteLine($"  Copied curves: {stats.Copied}, skipped: {stats.Skipped}");
                }

                Console.WriteLine($"Exported sketches: {exported}. Copied curves: {copied}, skipped: {skipped}");
                Log($"Exported all sketch DXF files to {exportDirectory}. Sketches={exported}, copied={copied}, skipped={skipped}");
                return 0;
            }

            string exportPath = BuildExportPath(documentInfo, options);

            Export(kompas7, document, documentInfo, exportPath, options.Format);
            Console.WriteLine($"Exported: {exportPath}");
            Log($"Exported: {exportPath}");

            if (options.SendToBridge)
            {
                SubmitToBridge(exportPath, options);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("kompas-bambu failed:");
            Console.Error.WriteLine(ex.Message);
            Log("Failed: " + ex);
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
            ? (options.IsCam ? DefaultCamOutputFolderName : DefaultOutputFolderName)
            : options.OutputFolderName;
        string exportDirectory = Path.Combine(documentInfo.Directory, SanitizeFolderName(outputFolderName));

        Directory.CreateDirectory(exportDirectory);

        return Path.Combine(exportDirectory, $"{safeName}{extension}");
    }

    private static string BuildSketchDxfDirectory(DocumentInfo documentInfo, Options options)
    {
        string outputFolderName = string.IsNullOrWhiteSpace(options.OutputFolderName)
            ? (options.AllSketchesDxf ? DefaultAllSketchesOutputFolderName : DefaultLaserOutputFolderName)
            : options.OutputFolderName;
        string exportDirectory = Path.Combine(documentInfo.Directory, SanitizeFolderName(outputFolderName));

        Directory.CreateDirectory(exportDirectory);
        return exportDirectory;
    }

    private static string BuildSketchDxfPath(DocumentInfo documentInfo, Options options, object sketch)
    {
        return BuildSketchDxfPath(documentInfo, BuildSketchDxfDirectory(documentInfo, options), sketch, null);
    }

    private static string BuildSketchDxfPath(DocumentInfo documentInfo, string exportDirectory, object sketch, int? index)
    {
        string sketchName = SanitizeFileName(GetSketchName(sketch, fallback: index is null ? "sketch" : $"sketch-{index.Value}"));
        string prefix = index is null ? string.Empty : $"{index.Value:00}-";
        return Path.Combine(exportDirectory, $"{prefix}{SanitizeFileName(documentInfo.Name)}-{sketchName}.dxf");
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
        DeletePreviousExport(exportPath);
        Exception? api5Error = null;

        try
        {
            ExportWithApi5(document, exportPath, format);
            return;
        }
        catch (Exception ex)
        {
            api5Error = ex;
        }

        try
        {
            ExportWithApi7(kompas, document, exportPath, format);
            return;
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

        DeletePreviousExport(exportPath);
        object converter = GetConverter(kompas);
        int command = format == ExportFormat.Step ? FormatStep : FormatStl;
        object result = Com.Invoke(converter, "Convert", documentInfo.FullPath, exportPath, command, false);

        if (File.Exists(exportPath))
        {
            if (result is int nonZeroCode && nonZeroCode != 0)
            {
                Log($"KOMPAS converter returned code {nonZeroCode}; validating output before use: {exportPath}");
            }
            ValidateExportFile(exportPath, format);
            return;
        }

        if (result is int code && code != 0)
        {
            throw new InvalidOperationException($"KOMPAS converter returned code {code}.");
        }

        throw new InvalidOperationException($"Export file was not created or is empty: {exportPath}");
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

        ValidateExportFile(exportPath, format);
    }

    private static void ExportWithApi7(object kompas, object document, string exportPath, ExportFormat format)
    {
        short kompasFormat = format == ExportFormat.Step ? FormatStep : FormatStl;
        object param = CreateAdditionConvertParameters(kompas, kompasFormat);
        Com.TryInvoke(param, "Clear");

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

        ValidateExportFile(exportPath, format);
    }

    private static void DeletePreviousExport(string exportPath)
    {
        if (File.Exists(exportPath)) File.Delete(exportPath);
    }

    private static void ValidateExportFile(string exportPath, ExportFormat format)
    {
        if (!File.Exists(exportPath))
        {
            throw new InvalidOperationException($"Export file was not created: {exportPath}");
        }

        using var stream = File.OpenRead(exportPath);
        if (format == ExportFormat.Step)
        {
            byte[] header = new byte[Math.Min(32, checked((int)stream.Length))];
            _ = stream.Read(header, 0, header.Length);
            string text = Encoding.ASCII.GetString(header);
            if (!text.StartsWith("ISO-10303-21;", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"KOMPAS created an invalid STEP file (expected ISO-10303-21 header): {exportPath}");
            }
            return;
        }

        if (stream.Length < 84)
        {
            throw new InvalidOperationException($"KOMPAS created an invalid binary STL file (smaller than 84 bytes): {exportPath}");
        }
        stream.Position = 80;
        Span<byte> countBuffer = stackalloc byte[4];
        _ = stream.Read(countBuffer);
        uint triangleCount = BitConverter.ToUInt32(countBuffer);
        long expectedLength = 84L + 50L * triangleCount;
        if (stream.Length != expectedLength)
        {
            throw new InvalidOperationException($"KOMPAS created an invalid binary STL file (triangle count does not match file length): {exportPath}");
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

    private static SketchExportStats ExportSketchDxf(object kompas5, object sketch, DocumentInfo documentInfo, string exportPath)
    {
        string sketchName = GetSketchName(sketch, fallback: "sketch");
        object sketchDefinition = Com.Invoke(sketch, "GetDefinition");
        object? source2D = Com.TryInvoke(sketchDefinition, "BeginEditEx", true)
            ?? Com.TryInvoke(sketchDefinition, "BeginEdit");

        if (source2D is null)
        {
            throw new InvalidOperationException($"Failed to open sketch '{sketchName}' for reading.");
        }

        List<SketchCurve> curves;
        int skipped;
        try
        {
            curves = ReadBasicSketchCurves(kompas5, source2D, out skipped);
        }
        finally
        {
            Com.TryInvokeMethod(sketchDefinition, "EndEdit");
        }

        if (curves.Count == 0)
        {
            throw new InvalidOperationException(
                $"Sketch '{sketchName}' has no supported basic-style curves for laser DXF export. " +
                "Use main-line segments/circles/arcs, or keep unsupported curves out of this first exporter version.");
        }

        object target2D = CreateTemporaryFragmentDocument(kompas5, documentInfo, exportPath);
        try
        {
            foreach (SketchCurve curve in curves)
            {
                curve.Draw(target2D);
            }

            object result = Com.Invoke(target2D, "ksSaveToDXF", exportPath);
            if (result is bool ok && !ok)
            {
                throw new InvalidOperationException($"KOMPAS returned false while saving DXF: {exportPath}");
            }

            if (!File.Exists(exportPath) || new FileInfo(exportPath).Length == 0)
            {
                throw new InvalidOperationException($"DXF file was not created or is empty: {exportPath}");
            }

            RemoveDxfVariable(exportPath, "$PDMODE");
        }
        finally
        {
            Com.TryInvokeMethod(target2D, "ksCloseDocument");
        }

        return new SketchExportStats(curves.Count, skipped);
    }

    private static object FindSketch(object document)
    {
        object? selected = FindSelectedSketch(document);
        if (selected is not null)
        {
            return selected;
        }

        object topPart = Com.Invoke(document, "GetPart", TopPart);
        object sketches = Com.Invoke(topPart, "EntityCollection", Obj3dSketch);
        int count = Convert.ToInt32(Com.Invoke(sketches, "GetCount"), CultureInfo.InvariantCulture);
        if (count <= 0)
        {
            throw new InvalidOperationException("No sketch found. Select a sketch in the model tree or create one first.");
        }

        return Com.Invoke(sketches, "GetByIndex", 0);
    }

    private static List<object> FindAllSketches(object document)
    {
        object topPart = Com.Invoke(document, "GetPart", TopPart);
        object sketches = Com.Invoke(topPart, "EntityCollection", Obj3dSketch);
        int count = Convert.ToInt32(Com.Invoke(sketches, "GetCount"), CultureInfo.InvariantCulture);
        if (count <= 0)
        {
            throw new InvalidOperationException("No sketches found. Create at least one sketch first.");
        }

        var result = new List<object>(count);
        for (int i = 0; i < count; i++)
        {
            result.Add(Com.Invoke(sketches, "GetByIndex", i));
        }

        return result;
    }

    private static object? FindSelectedSketch(object document)
    {
        object? selection = Com.TryInvoke(document, "GetSelectionMng")
            ?? Com.TryGet(document, "SelectionManager")
            ?? Com.TryGet(document, "SelectionMng");

        if (selection is null)
        {
            return null;
        }

        int count = Convert.ToInt32(Com.Invoke(selection, "GetCount"), CultureInfo.InvariantCulture);
        for (int i = 0; i < count; i++)
        {
            int type = Convert.ToInt32(Com.Invoke(selection, "GetObjectType", i), CultureInfo.InvariantCulture);
            if (type != Obj3dSketch)
            {
                continue;
            }

            object? selected = Com.TryInvoke(selection, "GetObjectByIndex", i);
            if (selected is not null)
            {
                return selected;
            }
        }

        return null;
    }

    private static string GetSketchName(object sketch, string fallback)
    {
        string? name = Com.TryGetString(sketch, "name")
            ?? Com.TryGetString(sketch, "Name")
            ?? Com.TryInvokeString(sketch, "GetName");

        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static List<SketchCurve> ReadBasicSketchCurves(object kompas5, object source2D, out int skipped)
    {
        var curves = new List<SketchCurve>();
        skipped = 0;

        object iterator = Com.Invoke(kompas5, "GetIterator");
        try
        {
            object created = Com.Invoke(iterator, "ksCreateIterator", All2DObjects, 0);
            if (created is bool ok && !ok)
            {
                throw new InvalidOperationException("KOMPAS failed to create a 2D object iterator.");
            }

            int obj = Convert.ToInt32(Com.Invoke(iterator, "ksMoveIterator", "F"), CultureInfo.InvariantCulture);
            while (Convert.ToInt32(Com.Invoke(source2D, "ksExistObj", obj), CultureInfo.InvariantCulture) == 1)
            {
                if (!TryReadSupportedBasicCurve(kompas5, source2D, obj, out SketchCurve? curve))
                {
                    skipped++;
                }
                else
                {
                    curves.Add(curve!);
                }

                obj = Convert.ToInt32(Com.Invoke(iterator, "ksMoveIterator", "N"), CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            Com.TryInvokeMethod(iterator, "ksDeleteIterator");
        }

        return curves;
    }

    private static bool TryReadSupportedBasicCurve(object kompas5, object source2D, int obj, out SketchCurve? curve)
    {
        curve = null;

        int style = Convert.ToInt32(Com.Invoke(source2D, "ksGetObjectStyle", obj), CultureInfo.InvariantCulture);
        if (style != BasicLineStyle)
        {
            return false;
        }

        object lineParam = GetParamStruct(kompas5, KoLineSegParam);
        if (TryGetObjParam(source2D, obj, lineParam, AllParam, out int lineType) && lineType == LineSegObj)
        {
            curve = new LineSegCurve(GetDouble(lineParam, "x1"), GetDouble(lineParam, "y1"), GetDouble(lineParam, "x2"), GetDouble(lineParam, "y2"));
            return true;
        }

        object circleParam = GetParamStruct(kompas5, KoCircleParam);
        if (TryGetObjParam(source2D, obj, circleParam, AllParam, out int circleType) && circleType == CircleObj)
        {
            curve = new CircleCurve(GetDouble(circleParam, "xc"), GetDouble(circleParam, "yc"), GetDouble(circleParam, "rad"));
            return true;
        }

        object arcParam = GetParamStruct(kompas5, KoArcByAngleParam);
        if (TryGetObjParam(source2D, obj, arcParam, AllParam, out int arcType) && arcType == ArcObj)
        {
            curve = new ArcCurve(
                GetDouble(arcParam, "xc"),
                GetDouble(arcParam, "yc"),
                GetDouble(arcParam, "rad"),
                GetDouble(arcParam, "ang1"),
                GetDouble(arcParam, "ang2"),
                Convert.ToInt32(Com.TryGet(arcParam, "dir") ?? 1, CultureInfo.InvariantCulture));
            return true;
        }

        return false;
    }

    private static bool TryGetObjParam(object document2D, int obj, object param, int parType, out int objectType)
    {
        try
        {
            objectType = Convert.ToInt32(Com.Invoke(document2D, "ksGetObjParam", obj, param, parType), CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            objectType = 0;
            return false;
        }
    }

    private static object CreateTemporaryFragmentDocument(object kompas5, DocumentInfo documentInfo, string exportPath)
    {
        object target2D = Com.Invoke(kompas5, "Document2D");
        object docParam = GetParamStruct(kompas5, KoDocumentParam);
        Com.TryInvoke(docParam, "Init");
        SetParam(docParam, "type", DocFragment);
        Com.TrySet(docParam, "regime", 0);
        Com.TrySet(docParam, "fileName", Path.ChangeExtension(exportPath, ".frw"));
        Com.TrySet(docParam, "comment", $"Laser DXF from {documentInfo.Name}");

        object result = Com.Invoke(target2D, "ksCreateDocument", docParam);
        if (result is bool ok && !ok)
        {
            throw new InvalidOperationException("KOMPAS failed to create a temporary 2D fragment for DXF export.");
        }

        return target2D;
    }

    private static object GetParamStruct(object kompas5, int type)
    {
        object param = Com.Invoke(kompas5, "GetParamStruct", type);
        Com.TryInvoke(param, "Init");
        return param;
    }

    private static double GetDouble(object target, string name)
    {
        return Convert.ToDouble(Com.TryGet(target, name) ?? throw new InvalidOperationException($"Missing parameter '{name}'."), CultureInfo.InvariantCulture);
    }

    private static void SubmitToBridge(string filePath, Options options)
    {
        string taskId = BridgeClient.Enqueue(filePath, options.NewWindow, options.BambuPath, options.ManufacturingTarget);
        Log($"Bridge task accepted: {taskId}; file={filePath}; target={options.ManufacturingTarget}; newWindow={options.NewWindow}");
        Console.WriteLine(options.IsCam ? "CAM job sent to bridge." : "File sent to Bambu Studio.");
    }

    private static void Log(string message)
    {
        try
        {
            string logPath = Path.Combine(Path.GetTempPath(), "kompas-bambu.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static void RemoveDxfVariable(string path, string variableName)
    {
        try
        {
            string[] lines = File.ReadAllLines(path, Encoding.Default);
            var filtered = new List<string>(lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.Equals(lines[i].Trim(), variableName, StringComparison.OrdinalIgnoreCase)
                    && filtered.Count > 0
                    && string.Equals(filtered[^1].Trim(), "9", StringComparison.OrdinalIgnoreCase)) {
                    filtered.RemoveAt(filtered.Count - 1);
                    i += 2;
                    continue;
                }

                filtered.Add(lines[i]);
            }

            File.WriteAllLines(path, filtered, Encoding.Default);
        }
        catch (Exception ex)
        {
            Log($"DXF cleanup warning: {ex.Message}");
        }
    }

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) {
            return false;
        }

        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }
}

internal enum ExportFormat
{
    Step,
    Stl
}

internal sealed record DocumentInfo(string Name, string Directory, string? FullPath)
{
    public static DocumentInfo FromPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Document path has no directory: {path}");
        }

        return new DocumentInfo(Path.GetFileNameWithoutExtension(fullPath), directory, fullPath);
    }
}

internal sealed record SketchExportStats(int Copied, int Skipped);

internal abstract record SketchCurve
{
    public abstract void Draw(object document2D);
}

internal sealed record LineSegCurve(double X1, double Y1, double X2, double Y2) : SketchCurve
{
    public override void Draw(object document2D)
    {
        Com.Invoke(document2D, "ksLineSeg", X1, Y1, X2, Y2, 2);
    }
}

internal sealed record CircleCurve(double Xc, double Yc, double Radius) : SketchCurve
{
    public override void Draw(object document2D)
    {
        Com.Invoke(document2D, "ksCircle", Xc, Yc, Radius, 2);
    }
}

internal sealed record ArcCurve(double Xc, double Yc, double Radius, double Angle1, double Angle2, int Direction) : SketchCurve
{
    public override void Draw(object document2D)
    {
        Com.Invoke(document2D, "ksArcByAngle", Xc, Yc, Radius, Angle1, Angle2, Direction, 2);
    }
}

internal sealed record Options(
    ExportFormat Format,
    bool OpenBambu,
    string? BambuPath,
    string OutputFolderName,
    bool ShowHelp,
    bool SketchDxf,
    bool AllSketchesDxf,
    string? ExpectedDocumentPath,
    int? CallerProcessId,
    bool NewWindow = false,
    bool IsCam = false)
{
    public string ManufacturingTarget => IsCam ? "cnc" : "fdm";
    public bool SendToBridge => IsCam || OpenBambu;

    public static Options Parse(string[] args)
    {
        ExportFormat format = ExportFormat.Step;
        bool openBambu = true;
        bool newWindow = false;
        bool sketchDxf = false;
        bool allSketchesDxf = false;
        bool isCam = false;
        string? bambuPath = null;
        string? outputFolderName = null;
        string? expectedDocumentPath = null;
        int? callerProcessId = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].Trim();

            switch (arg.ToLowerInvariant())
            {
                case "step":
                case "--step":
                    format = ExportFormat.Step;
                    break;
                case "--new-window":
                    newWindow = true;
                    break;
                case "stl":
                case "--stl":
                    format = ExportFormat.Stl;
                    break;
                case "cam":
                case "--cam":
                    format = ExportFormat.Step;
                    isCam = true;
                    openBambu = false;
                    break;
                case "dxf":
                case "--dxf":
                case "dxf-sketch":
                case "sketch-dxf":
                case "--dxf-sketch":
                case "--sketch-dxf":
                    sketchDxf = true;
                    allSketchesDxf = false;
                    openBambu = false;
                    break;
                case "all-dxf":
                case "--all-dxf":
                case "dxf-all-sketches":
                case "all-sketches-dxf":
                case "--dxf-all-sketches":
                case "--all-sketches-dxf":
                    allSketchesDxf = true;
                    sketchDxf = false;
                    openBambu = false;
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
                case "--document":
                    expectedDocumentPath = ReadNext(args, ref i, "--document");
                    break;
                case "--caller-pid":
                    callerProcessId = int.Parse(ReadNext(args, ref i, "--caller-pid"), CultureInfo.InvariantCulture);
                    break;
                case "-h":
                case "--help":
                case "/?":
                    return new Options(
                        format,
                        openBambu,
                        bambuPath,
                        outputFolderName ?? (allSketchesDxf ? "dfx" : sketchDxf ? "laser" : isCam ? "cnc" : "fdm"),
                        ShowHelp: true,
                        SketchDxf: sketchDxf,
                        AllSketchesDxf: allSketchesDxf,
                        ExpectedDocumentPath: expectedDocumentPath,
                        CallerProcessId: callerProcessId,
                        NewWindow: newWindow,
                        IsCam: isCam);
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        // CAM is an export-only command. Keep this invariant even if a caller
        // places an `open` switch after `cam`.
        if (isCam)
        {
            format = ExportFormat.Step;
            openBambu = false;
        }

        return new Options(
            format,
            openBambu,
            bambuPath,
            outputFolderName ?? (allSketchesDxf ? "dfx" : sketchDxf ? "laser" : isCam ? "cnc" : "fdm"),
            ShowHelp: false,
            SketchDxf: sketchDxf,
            AllSketchesDxf: allSketchesDxf,
            ExpectedDocumentPath: expectedDocumentPath,
            CallerProcessId: callerProcessId,
            NewWindow: newWindow,
            IsCam: isCam);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
        kompas-bambu

        Usage:
          kompas-bambu [step|stl] [--new-window] [open|export] [--out-dir <name>] [--bambu <path>]
          kompas-bambu cam [--out-dir <name>]
          kompas-bambu [step|stl] [--document <path>] [--caller-pid <pid>]
          kompas-bambu dxf-sketch [--out-dir <name>]
          kompas-bambu all-sketches-dxf [--out-dir <name>]

        Defaults:
          format: step, STEP AP203
          FDM output: <KOMPAS file folder>\fdm\<same-name>.step
          CAM output: <KOMPAS file folder>\cnc\<same-name>.step
          action: normal commands reuse Bambu Studio; --new-window starts a separate window
          dxf-sketch output: <KOMPAS file folder>\laser\<model>-<sketch>.dxf
          all-sketches-dxf output: <KOMPAS file folder>\dfx\NN-<model>-<sketch>.dxf

        Examples:
          kompas-bambu
          kompas-bambu step --new-window
          kompas-bambu stl
          kompas-bambu dxf-sketch
          kompas-bambu all-sketches-dxf
          kompas-bambu step export
          kompas-bambu step --out-dir fdm
          kompas-bambu cam
          kompas-bambu cam --out-dir cnc
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

internal static class BambuWindow
{
    private const uint WmDropFiles = 0x0233;
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroInit = 0x0040;
    private const uint SmtoAbortIfHung = 0x0002;

    public static bool TryDropFile(string filePath, out nint windowHandle)
    {
        windowHandle = nint.Zero;
        if (!File.Exists(filePath))
        {
            return false;
        }

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
            finally
            {
                _ = GlobalUnlock(hDrop);
            }

            nint result;
            bool delivered = SendMessageTimeout(candidate, WmDropFiles, hDrop, nint.Zero, SmtoAbortIfHung, 2000, out result) != nint.Zero;
            if (delivered)
            {
                // The recipient owns hDrop after a successfully delivered WM_DROPFILES.
                hDrop = nint.Zero;
                windowHandle = candidate;
                return true;
            }

            return false;
        }
        finally
        {
            if (hDrop != nint.Zero)
            {
                _ = GlobalFree(hDrop);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DropFiles
    {
        public uint FileOffset;
        public int X;
        public int Y;
        public int NonClientArea;
        public int Wide;
    }

    private delegate bool EnumWindowsCallback(nint handle, nint parameter);

    private static void BringIntoView(nint windowHandle)
    {
        const uint MonitorDefaultToNull = 0;
        const uint SwpNoZOrder = 0x0004;
        const uint SwpShowWindow = 0x0040;
        const int SwRestore = 9;

        _ = ShowWindow(windowHandle, SwRestore);
        if (MonitorFromWindow(windowHandle, MonitorDefaultToNull) == nint.Zero)
        {
            // A display that owned Bambu's last position is no longer present.
            _ = SetWindowPos(windowHandle, nint.Zero, 100, 100, 1500, 950, SwpNoZOrder | SwpShowWindow);
        }

        _ = SetForegroundWindow(windowHandle);
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint handle, StringBuilder className, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder title, int capacity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint handle,
        uint message,
        nint wParam,
        nint lParam,
        uint flags,
        uint timeoutMilliseconds,
        out nint result);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint handle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint handle, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalFree(nint memory);
}

internal static class BridgeClient
{
    private const string PipeName = "KompasBambuBridge.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Enqueue(string filePath, bool newWindow, string? bambuPath, string manufacturingTarget)
    {
        string bridgePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KompasBambu",
            "Bridge",
            "kompas-bambu-bridge.exe");
        if (!File.Exists(bridgePath))
        {
            throw new FileNotFoundException("Kompas Bambu Bridge is not installed.", bridgePath);
        }

        var request = new BridgeRequest(
            Guid.NewGuid().ToString("N"),
            filePath,
            newWindow ? "new-window" : "open",
            bambuPath,
            manufacturingTarget);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
                pipe.Connect(250);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                writer.WriteLine(JsonSerializer.Serialize(request, JsonOptions));
                string? response = reader.ReadLine();
                if (response is not null && response.StartsWith("accepted:", StringComparison.Ordinal))
                {
                    return request.Id;
                }
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
            }

            if (attempt == 0)
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = bridgePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(bridgePath) ?? Environment.CurrentDirectory
                });
            }
            Thread.Sleep(150);
        }

        throw new InvalidOperationException("Kompas Bambu Bridge did not accept the task. See its log in %LOCALAPPDATA%\\KompasBambu\\logs.");
    }

    // The bridge currently ignores ManufacturingTarget, but it is deliberately
    // included in the wire request so it can route FDM/CNC jobs later without
    // changing the KOMPAS plugin protocol again.
    private sealed record BridgeRequest(string Id, string FilePath, string Operation, string? BambuPath, string ManufacturingTarget);
}

internal static class Com
{
    private const int SOk = 0;

    public static object GetActiveObjectWithRetry(string progId, TimeSpan timeout)
    {
        Exception? lastError = null;
        Stopwatch stopwatch = Stopwatch.StartNew();
        do
        {
            try
            {
                return GetActiveObject(progId);
            }
            catch (Exception error)
            {
                lastError = error;
                Thread.Sleep(250);
            }
        }
        while (stopwatch.Elapsed < timeout);

        throw new InvalidOperationException(
            $"Could not connect to {progId} within {timeout.TotalSeconds:0} seconds. " +
            "Keep KOMPAS-3D open with the source 3D document loaded, then retry.",
            lastError);
    }

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

    public static object? TryGetActiveObject(string progId)
    {
        try
        {
            return GetActiveObject(progId);
        }
        catch
        {
            return null;
        }
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
