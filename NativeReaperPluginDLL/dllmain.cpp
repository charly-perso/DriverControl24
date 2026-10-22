#include "pch.h"
#include "reaper_plugin.h"
#include <fstream>
#include <string>
#include <cstdlib>

#ifdef _WIN32
#include <windows.h>
#else
#include <sys/socket.h>
#include <sys/un.h>
#include <unistd.h>
#include <cstring>
#endif

static reaper_plugin_info_t* g_rec = nullptr;

static std::string GetLogPath()
{
#ifdef _WIN32
    return "C:\\Users\\chcolin\\AppData\\Roaming\\REAPER\\UserPlugins\\NativeReaperPlugIn.log";
#else
    const char* home = std::getenv("HOME");
    std::string path = (home != nullptr) ? home : "/tmp";
    path += "/Library/Application Support/REAPER/UserPlugins/NativeReaperPlugIn.log";
    return path;
#endif
}

static void LogFile(const char* text)
{
    std::ofstream f(GetLogPath(), std::ios::app);
    if (f.is_open())
    {
        f << text << "\n";
    }
}

#ifdef _WIN32

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

#else

static std::string GetSocketPath()
{
    const char* tmp = std::getenv("TMPDIR");
    std::string dir = (tmp != nullptr && tmp[0] != '\0') ? tmp : "/tmp/";
    if (dir.back() != '/')
    {
        dir += "/";
    }
    return dir + "CoreFxPipe_c24_reaper_events";
}

static bool SendEventToPipe(const std::string& payload)
{
    std::string path = GetSocketPath();

    int fd = socket(AF_UNIX, SOCK_STREAM, 0);
    if (fd < 0)
    {
        LogFile("SendEventToPipe: socket failed");
        return false;
    }

    int noSigPipe = 1;
    setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &noSigPipe, sizeof(noSigPipe));

    sockaddr_un addr;
    std::memset(&addr, 0, sizeof(addr));
    addr.sun_family = AF_UNIX;
    if (path.size() >= sizeof(addr.sun_path))
    {
        LogFile("SendEventToPipe: path too long");
        close(fd);
        return false;
    }
    std::strncpy(addr.sun_path, path.c_str(), sizeof(addr.sun_path) - 1);

    if (connect(fd, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) < 0)
    {
        LogFile("SendEventToPipe: connect failed");
        close(fd);
        return false;
    }

    std::string line = payload;
    line += "\n";

    size_t total = 0;
    while (total < line.size())
    {
        ssize_t n = write(fd, line.c_str() + total, line.size() - total);
        if (n <= 0)
        {
            LogFile("SendEventToPipe: write failed");
            close(fd);
            return false;
        }
        total += static_cast<size_t>(n);
    }

    close(fd);
    return true;
}

#endif

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

#ifdef _WIN32
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
#endif