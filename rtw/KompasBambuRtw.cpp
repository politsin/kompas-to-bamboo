#include <windows.h>
#include <shellapi.h>

namespace
{
const wchar_t* ExporterFileName = L"kompas-bambu.exe";

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

void runExporter(const wchar_t* command)
{
    wchar_t exporterPath[MAX_PATH] = {};
    if (!buildExporterPath(exporterPath, MAX_PATH)) {
        MessageBoxW(nullptr, L"Cannot locate kompas-bambu.exe next to RTW.", L"Bambu Studio", MB_ICONERROR);
        return;
    }

    HINSTANCE result = ShellExecuteW(nullptr, L"open", exporterPath, command, nullptr, SW_SHOWNORMAL);
    if (reinterpret_cast<INT_PTR>(result) <= 32) {
        MessageBoxW(nullptr, L"Cannot start kompas-bambu.exe.", L"Bambu Studio", MB_ICONERROR);
    }
}
}

extern "C" __declspec(dllexport) char* __stdcall LIBRARYNAME()
{
    return const_cast<char*>("Bambu Studio");
}

extern "C" __declspec(dllexport) unsigned int __stdcall LIBRARYID()
{
    return 0xBABA203;
}

extern "C" __declspec(dllexport) void __stdcall LIBRARYENTRY(unsigned int command)
{
    switch (command) {
    case 1: runExporter(L"step"); break;
    case 3: runExporter(L"step --new-window"); break;
    case 2: runExporter(L"stl"); break;
    case 4: runExporter(L"stl --new-window"); break;
    default: break;
    }
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID)
{
    return TRUE;
}
