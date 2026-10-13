#include <windows.h>
#include "../native/agent/logon_startup.h"
#include <cstdio>
#include <stdexcept>
#define CHECK(x) do { if (!(x)) throw std::runtime_error(#x); } while (0)
using namespace tray;
int wmain() {
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    auto name = L"TrayIconAssistant.Test." + StartupUserSid() + L"." + std::to_wstring(GetCurrentProcessId());
    auto folder = StartupTaskFolder();
    bool created{};
    try {
        wchar_t executable[32768]{}; GetModuleFileNameW(nullptr, executable, std::size(executable));
        CHECK(SetLogonStartup(executable, false, name));
        winrt::com_ptr<IRegisteredTask> task;
        CHECK(FAILED(folder->GetTask(TaskString(name), task.put())));
        CHECK(SetLogonStartup(executable, true, name)); created = true;
        CHECK(SUCCEEDED(folder->GetTask(TaskString(name), task.put())));
        BSTR xml{}; CHECK(SUCCEEDED(task->get_Xml(&xml)));
        std::wstring text(xml); SysFreeString(xml); xml = nullptr;
        CHECK(text.find(L"<LogonTrigger>") != std::wstring::npos);
        CHECK(text.find(L"<Delay>") == std::wstring::npos);
        CHECK(text.find(L"--background") != std::wstring::npos);
        winrt::com_ptr<ITaskDefinition> checkedDefinition;
        CHECK(SUCCEEDED(task->get_Definition(checkedDefinition.put())));
        winrt::com_ptr<IPrincipal> checkedPrincipal;
        CHECK(SUCCEEDED(checkedDefinition->get_Principal(checkedPrincipal.put())));
        TASK_RUNLEVEL_TYPE level{}; CHECK(SUCCEEDED(checkedPrincipal->get_RunLevel(&level)));
        CHECK(level == TASK_RUNLEVEL_LUA);
        CHECK(text.find(L"<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>") != std::wstring::npos);
        CHECK(SetLogonStartup(executable, false, name));
        VARIANT_BOOL enabled{}; CHECK(SUCCEEDED(task->get_Enabled(&enabled))); CHECK(!enabled);
        CHECK(SetLogonStartup(executable, true, name));
        task = nullptr; CHECK(SUCCEEDED(folder->GetTask(TaskString(name), task.put())));
        CHECK(SUCCEEDED(task->get_Enabled(&enabled))); CHECK(enabled);
        winrt::com_ptr<ITaskDefinition> definition;
        CHECK(SUCCEEDED(task->get_Definition(definition.put())));
        winrt::com_ptr<IRegistrationInfo> info;
        CHECK(SUCCEEDED(definition->get_RegistrationInfo(info.put())));
        CHECK(SUCCEEDED(info->put_Source(TaskString(L"synthetic-other-owner"))));
        TaskValue user(StartupUserSid()), empty;
        winrt::com_ptr<IRegisteredTask> collision;
        CHECK(SUCCEEDED(folder->RegisterTaskDefinition(TaskString(name), definition.get(), TASK_UPDATE,
            user.value, empty.value, TASK_LOGON_INTERACTIVE_TOKEN, empty.value, collision.put())));
        CHECK(SUCCEEDED(collision->get_Xml(&xml))); std::wstring before(xml); SysFreeString(xml); xml = nullptr;
        for (bool value : {true, false}) {
            CHECK(!SetLogonStartup(executable, value, name)); CHECK(GetLastError() == ERROR_ALREADY_EXISTS);
        }
        task = nullptr; CHECK(SUCCEEDED(folder->GetTask(TaskString(name), task.put())));
        CHECK(SUCCEEDED(task->get_Xml(&xml))); std::wstring after(xml); SysFreeString(xml);
        CHECK(before == after);
        CHECK(SUCCEEDED(folder->DeleteTask(TaskString(name), 0))); created = false;
        std::puts("PASS immediate user logon, least privilege, enable/disable/update, same-name foreign task preserved");
    } catch (const std::exception& error) {
        auto lastError = GetLastError();
        std::fprintf(stderr, "FAIL startup: %s (error=%lu)\n", error.what(), lastError);
        if (created) folder->DeleteTask(TaskString(name), 0);
        folder = nullptr; CoUninitialize(); return 1;
    }
    folder = nullptr; CoUninitialize(); return 0;
}
