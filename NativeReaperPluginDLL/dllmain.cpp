#include "pch.h"
#include "reaper_plugin.h"
#include <windows.h>
#include <fstream>
#include <string>

static reaper_plugin_info_t* g_rec = nullptr;

static void LogFile(const char* text)
{
    std::ofstream f("C:\\Users\\chcolin\\AppData\\Roaming\\REAPER\\UserPlugins\\NativeReaperPlugIn.log", std::ios::app);
    if (f.is_open())
    {
        f << text << "\n";
    }
}

static bool SendEventToPipe(const std::string& payload)
{
    const char* pipeName = "\\\\.\\pipe\\c24_reaper_events";

    if (WaitNamedPipeA(pipeName, 500) == FALSE)
    {
        LogFile("SendEventToPipe: WaitNamedPipe failed");
        return false;
    }

    HANDLE hPipe = CreateFileA(
        pipeName,
        GENERIC_WRITE,
        0,
        nullptr,
        OPEN_EXISTING,
        0,
        nullptr);

    if (hPipe == INVALID_HANDLE_VALUE)
    {
        LogFile("SendEventToPipe: CreateFile failed");
        return false;
    }

    std::string line = payload;
    line += "\n";

    DWORD written = 0;
    BOOL ok = WriteFile(
        hPipe,
        line.c_str(),
        static_cast<DWORD>(line.size()),
        &written,
        nullptr);

    CloseHandle(hPipe);

    if (ok == FALSE)
    {
        LogFile("SendEventToPipe: WriteFile failed");
        return false;
    }

    return true;
}

static void HookPostCommand2Callback(
    KbdSectionInfo* section,
    int command,
    int val,
    int valhw,
    int relmode,
    HWND hwnd,
    ReaProject* proj)
{
    (void)section;
    (void)hwnd;
    (void)proj;

    std::string payload =
        std::to_string(command) + "|" +
        std::to_string(val) + "|" +
        std::to_string(valhw) + "|" +
        std::to_string(relmode);

    SendEventToPipe(payload);
}

extern "C" REAPER_PLUGIN_DLL_EXPORT
int REAPER_PLUGIN_ENTRYPOINT(REAPER_PLUGIN_HINSTANCE hInstance, reaper_plugin_info_t* rec)
{
    (void)hInstance;

    LogFile("ReaperPluginEntry called");

    if (rec == nullptr)
    {
        LogFile("ReaperPluginEntry unload");

        if (g_rec != nullptr)
        {
            g_rec->Register("-hookpostcommand2", reinterpret_cast<void*>(HookPostCommand2Callback));
        }

        g_rec = nullptr;
        return 0;
    }

    if (rec->caller_version != REAPER_PLUGIN_VERSION)
    {
        LogFile("Version mismatch");
        return 0;
    }

    g_rec = rec;

    int rc = rec->Register("hookpostcommand2", reinterpret_cast<void*>(HookPostCommand2Callback));
    if (rc == 0)
    {
        LogFile("Register hookpostcommand2 failed");
        return 0;
    }

    LogFile("Register hookpostcommand2 OK");
    return 1;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    (void)hModule;
    (void)lpReserved;

    if (ul_reason_for_call == DLL_PROCESS_ATTACH)
    {
        LogFile("DllMain attach");
    }
    else if (ul_reason_for_call == DLL_PROCESS_DETACH)
    {
        LogFile("DllMain detach");
    }

    return TRUE;
}