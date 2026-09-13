#include <windows.h>
#include <shellapi.h>
#include <oleauto.h>
#include <string>

namespace
{
const wchar_t* ExporterFileName = L"kompas-bambu.exe";
using CreateKompasObjectProc = IDispatch* (__stdcall*)();

bool getModuleDirectory(wchar_t* buffer, DWORD bufferCount)
{
    HMODULE module = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&getModuleDirectory),
            &module)) {
        return false;
    }

    DWORD length = GetModuleFileNameW(module, buffer, bufferCount);
    if (length == 0 || length >= bufferCount) {
        return false;
    }

    for (DWORD i = length; i > 0; --i) {
        if (buffer[i - 1] == L'\\' || buffer[i - 1] == L'/') {
            buffer[i] = L'\0';
            return true;
        }
    }

    return false;
}

bool buildExporterPath(wchar_t* buffer, DWORD bufferCount)
{
    if (!getModuleDirectory(buffer, bufferCount)) {
        return false;
    }

    DWORD currentLength = lstrlenW(buffer);
    DWORD fileNameLength = lstrlenW(ExporterFileName);
    if (currentLength + fileNameLength + 1 > bufferCount) {
        return false;
    }

    lstrcatW(buffer, ExporterFileName);
    return true;
}

bool dispatchInvokeNoArgs(IDispatch* dispatch, const wchar_t* name, WORD flags, VARIANT* result)
{
    if (!dispatch) {
        return false;
    }

    DISPID dispId = 0;
    LPOLESTR names[] = { const_cast<LPOLESTR>(name) };
    HRESULT hr = dispatch->GetIDsOfNames(IID_NULL, names, 1, LOCALE_USER_DEFAULT, &dispId);
    if (FAILED(hr)) {
        return false;
    }

    DISPPARAMS params = {};
    VariantInit(result);
    hr = dispatch->Invoke(dispId, IID_NULL, LOCALE_USER_DEFAULT, flags, &params, result, nullptr, nullptr);
    return SUCCEEDED(hr);
}

std::wstring getCurrentKompasDocumentPath()
{
    HMODULE api5 = LoadLibraryW(L"kAPI5.dll");
    if (!api5) {
        return {};
    }

    auto createKompasObject = reinterpret_cast<CreateKompasObjectProc>(
        GetProcAddress(api5, "CreateKompasObject"));
    if (!createKompasObject) {
        return {};
    }

    HRESULT coInit = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    bool shouldUninitialize = coInit == S_OK || coInit == S_FALSE;

    IDispatch* kompas = createKompasObject();
    if (!kompas) {
        if (shouldUninitialize) {
            CoUninitialize();
        }
        return {};
    }

    VARIANT documentVariant;
    VariantInit(&documentVariant);
    std::wstring path;

    if (dispatchInvokeNoArgs(kompas, L"ActiveDocument3D", DISPATCH_METHOD, &documentVariant)
        && documentVariant.vt == VT_DISPATCH
        && documentVariant.pdispVal) {
        VARIANT fileNameVariant;
        VariantInit(&fileNameVariant);

        if (dispatchInvokeNoArgs(documentVariant.pdispVal, L"fileName", DISPATCH_PROPERTYGET, &fileNameVariant)
            && fileNameVariant.vt == VT_BSTR
            && fileNameVariant.bstrVal
            && SysStringLen(fileNameVariant.bstrVal) > 0) {
            path.assign(fileNameVariant.bstrVal, SysStringLen(fileNameVariant.bstrVal));
        }

        VariantClear(&fileNameVariant);
    }

    VariantClear(&documentVariant);
    kompas->Release();

    if (shouldUninitialize) {
        CoUninitialize();
    }

    return path;
}

std::wstring quoteArgument(const std::wstring& value)
{
    std::wstring quoted = L"\"";
    for (wchar_t ch : value) {
        if (ch == L'"' || ch == L'\\') {
            quoted.push_back(L'\\');
        }
        quoted.push_back(ch);
    }
    quoted.push_back(L'"');
    return quoted;
}

void runExporter(const wchar_t* command)
{
    wchar_t exporterPath[MAX_PATH] = {};
    if (!buildExporterPath(exporterPath, MAX_PATH)) {
        MessageBoxW(nullptr, L"Cannot locate kompas-bambu.exe next to RTW.", L"Bambu Studio", MB_ICONERROR);
        return;
    }

    std::wstring arguments = command;
    arguments += L" --caller-pid ";
    arguments += std::to_wstring(GetCurrentProcessId());

    std::wstring documentPath = getCurrentKompasDocumentPath();
    if (!documentPath.empty()) {
        arguments += L" --document ";
        arguments += quoteArgument(documentPath);
    }

    HINSTANCE result = ShellExecuteW(nullptr, L"open", exporterPath, arguments.c_str(), nullptr, SW_SHOWNORMAL);
    if (reinterpret_cast<INT_PTR>(result) <= 32) {
        MessageBoxW(nullptr, L"Cannot start kompas-bambu.exe.", L"Bambu Studio", MB_ICONERROR);
    }
}
}

extern "C" __declspec(dllexport) char* __stdcall LIBRARYNAME()
{
    return const_cast<char*>("Bambu Studio");
}

extern "C" __declspec(dllexport) wchar_t* __stdcall LIBRARYNAMEW()
{
    return const_cast<wchar_t*>(L"Bambu Studio");
}

extern "C" __declspec(dllexport) wchar_t* __stdcall DisplayLibraryNameW()
{
    return const_cast<wchar_t*>(L"Bambu Studio");
}

extern "C" __declspec(dllexport) unsigned int __stdcall LIBRARYID()
{
    return 0xBABA203;
}

extern "C" __declspec(dllexport) unsigned int __stdcall LIBRARYPROTECTNUMBER()
{
    return 0;
}

extern "C" __declspec(dllexport) unsigned int __stdcall LibraryBmpBeginID()
{
    return 0;
}

extern "C" __declspec(dllexport) unsigned int __stdcall LibToolBarId(int, int)
{
    return 0;
}

extern "C" __declspec(dllexport) void __stdcall LIBRARYENTRY(unsigned int command)
{
    switch (command) {
    case 1: runExporter(L"step"); break;
    case 3: runExporter(L"step --new-window"); break;
    case 2: runExporter(L"stl"); break;
    case 4: runExporter(L"stl --new-window"); break;
    case 5: runExporter(L"dxf-sketch"); break;
    case 6: runExporter(L"all-sketches-dxf"); break;
    default: break;
    }
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID)
{
    return TRUE;
}
