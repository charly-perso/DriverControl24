#include <windows.h>
#include <string>
#include "reaper_plugin.h"

static reaper_plugin_info_t* g_rec = nullptr;

static void SendEventToDotNet(int command, int flag)
{
    HANDLE hPipe = CreateFileA(
        "\\\\.\\pipe\\c24_reaper_events",
        GENERIC_WRITE,
        0,
        nullptr,
        OPEN_EXISTING,
        0,
        nullptr
    );

    if (hPipe == INVALID_HANDLE_VALUE)
    {
        return;
    }

    std::string line = std::to_string(command) + "|" + std::to_string(flag) + "\n";
    DWORD written = 0;
    WriteFile(hPipe, line.c_str(), static_cast<DWORD>(line.size()), &written, nullptr);
    CloseHandle(hPipe);
}

static void hookpostcommand(int command, int flag)
{
    SendEventToDotNet(command, flag);
}

extern "C"
{
    int REAPER_PLUGIN_ENTRYPOINT(REAPER_PLUGIN_HINSTANCE hInstance, reaper_plugin_info_t* rec)
    {
        if (rec == nullptr)
        {
            if (g_rec != nullptr)
            {
                g_rec->Register("-hookpostcommand", (void*)hookpostcommand);
            }

            g_rec = nullptr;
            return 0;
        }

        g_rec = rec;
        g_rec->Register("hookpostcommand", (void*)hookpostcommand);
        return 1;
    }
}