#pragma once
#include <taskschd.h>
#include <sddl.h>
#include <winrt/base.h>
#include <filesystem>
#include <string>
#include <vector>

namespace tray {
inline constexpr wchar_t StartupSource[] = L"CRYS74L.TrayIconAssistant/logon-v1";
struct TaskString {
    BSTR value{};
    explicit TaskString(const std::wstring& text) : value(SysAllocString(text.c_str())) {
        if (!value) throw std::bad_alloc();
    }
    ~TaskString() { SysFreeString(value); }
    operator BSTR() const { return value; }
};
struct TaskValue {
    VARIANT value{};
    TaskValue() { VariantInit(&value); }
    explicit TaskValue(const std::wstring& text) : TaskValue() {
        value.vt = VT_BSTR; value.bstrVal = SysAllocString(text.c_str());
        if (!value.bstrVal) throw std::bad_alloc();
    }
    ~TaskValue() { VariantClear(&value); }
};
inline std::wstring StartupUserSid() {
    HANDLE token{};
    winrt::check_bool(OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token));
    DWORD length{};
    GetTokenInformation(token, TokenUser, nullptr, 0, &length);
    std::vector<uint8_t> data(length);
    auto ok = GetTokenInformation(token, TokenUser, data.data(), length, &length);
    auto error = GetLastError(); CloseHandle(token);
    if (!ok) throw winrt::hresult_error(HRESULT_FROM_WIN32(error));
    wchar_t* sid{};
    winrt::check_bool(ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(data.data())->User.Sid, &sid));
    std::wstring result(sid); LocalFree(sid); return result;
}
inline std::wstring StartupTaskName() { return L"TrayIconAssistant.Logon." + StartupUserSid(); }
inline bool StartupPrincipalMatches(BSTR user, const std::wstring& expected) {
    if (!user) return false;
    if (!_wcsicmp(user, expected.c_str())) return true;
    DWORD size{}, domainSize{}; SID_NAME_USE use{};
    LookupAccountNameW(nullptr, user, nullptr, &size, nullptr, &domainSize, &use);
    if (!size || size > 65536 || domainSize > 32768) return false;
    std::vector<uint8_t> sid(size); std::vector<wchar_t> domain(domainSize);
    if (!LookupAccountNameW(nullptr, user, sid.data(), &size, domain.data(), &domainSize, &use))
        return false;
    wchar_t* text{};
    if (!ConvertSidToStringSidW(sid.data(), &text)) return false;
    bool matches = !_wcsicmp(text, expected.c_str()); LocalFree(text); return matches;
}
inline winrt::com_ptr<ITaskFolder> StartupTaskFolder() {
    winrt::com_ptr<ITaskService> service;
    winrt::check_hresult(CoCreateInstance(__uuidof(TaskScheduler), nullptr, CLSCTX_INPROC_SERVER,
                                         __uuidof(ITaskService), service.put_void()));
    TaskValue empty;
    winrt::check_hresult(service->Connect(empty.value, empty.value, empty.value, empty.value));
    winrt::com_ptr<ITaskFolder> folder;
    winrt::check_hresult(service->GetFolder(TaskString(L"\\"), folder.put()));
    return folder;
}
// Every mutation addresses one exact task. Never enumerate, replace or delete a task folder.
// A same-name task without our source marker and user identity is a collision, not ours.
inline DWORD SetLogonStartupResult(const std::filesystem::path& executable, bool enabled,
                                   const std::wstring& name,
                                   const std::wstring& arguments = L"--background") {
    try {
        auto folder = StartupTaskFolder();
        auto sid = StartupUserSid();
        winrt::com_ptr<IRegisteredTask> existing;
        auto found = folder->GetTask(TaskString(name), existing.put());
        if (SUCCEEDED(found)) {
            winrt::com_ptr<ITaskDefinition> definition;
            winrt::check_hresult(existing->get_Definition(definition.put()));
            winrt::com_ptr<IRegistrationInfo> registration;
            winrt::com_ptr<IPrincipal> principal;
            winrt::check_hresult(definition->get_RegistrationInfo(registration.put()));
            winrt::check_hresult(definition->get_Principal(principal.put()));
            BSTR source{}, user{};
            auto sourceResult = registration->get_Source(&source);
            auto userResult = principal->get_UserId(&user);
            bool ours = SUCCEEDED(sourceResult) && SUCCEEDED(userResult) && source && user &&
                        !wcscmp(source, StartupSource) && StartupPrincipalMatches(user, sid);
            SysFreeString(source); SysFreeString(user);
            if (!ours) return ERROR_ALREADY_EXISTS;
            if (!enabled) {
                winrt::check_hresult(existing->put_Enabled(VARIANT_FALSE));
                return ERROR_SUCCESS;
            }
        } else if (found != HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND) &&
                   found != HRESULT_FROM_WIN32(ERROR_PATH_NOT_FOUND)) {
            winrt::check_hresult(found);
        } else if (!enabled) return ERROR_SUCCESS;

        winrt::com_ptr<ITaskService> service;
        winrt::check_hresult(CoCreateInstance(__uuidof(TaskScheduler), nullptr, CLSCTX_INPROC_SERVER,
                                             __uuidof(ITaskService), service.put_void()));
        TaskValue empty, user(sid);
        winrt::check_hresult(service->Connect(empty.value, empty.value, empty.value, empty.value));
        winrt::com_ptr<ITaskDefinition> task;
        winrt::check_hresult(service->NewTask(0, task.put()));
        winrt::com_ptr<IRegistrationInfo> registration;
        winrt::check_hresult(task->get_RegistrationInfo(registration.put()));
        winrt::check_hresult(registration->put_Source(TaskString(StartupSource)));
        winrt::check_hresult(registration->put_Author(TaskString(sid)));
        winrt::check_hresult(registration->put_Description(TaskString(L"托盘图标助手：用户登录后立即启动原生后台")));
        winrt::com_ptr<IPrincipal> principal;
        winrt::check_hresult(task->get_Principal(principal.put()));
        winrt::check_hresult(principal->put_UserId(TaskString(sid)));
        winrt::check_hresult(principal->put_LogonType(TASK_LOGON_INTERACTIVE_TOKEN));
        winrt::check_hresult(principal->put_RunLevel(TASK_RUNLEVEL_LUA));
        winrt::com_ptr<ITaskSettings> settings;
        winrt::check_hresult(task->get_Settings(settings.put()));
        winrt::check_hresult(settings->put_MultipleInstances(TASK_INSTANCES_IGNORE_NEW));
        winrt::check_hresult(settings->put_DisallowStartIfOnBatteries(VARIANT_FALSE));
        winrt::check_hresult(settings->put_StopIfGoingOnBatteries(VARIANT_FALSE));
        winrt::check_hresult(settings->put_AllowHardTerminate(VARIANT_FALSE));
        winrt::check_hresult(settings->put_ExecutionTimeLimit(TaskString(L"PT0S")));
        winrt::check_hresult(settings->put_StartWhenAvailable(VARIANT_TRUE));
        winrt::check_hresult(settings->put_Enabled(VARIANT_TRUE));
        winrt::com_ptr<ITriggerCollection> triggers;
        winrt::com_ptr<ITrigger> trigger;
        winrt::check_hresult(task->get_Triggers(triggers.put()));
        winrt::check_hresult(triggers->Create(TASK_TRIGGER_LOGON, trigger.put()));
        auto logon = trigger.as<ILogonTrigger>();
        winrt::check_hresult(logon->put_UserId(TaskString(sid)));
        // No Delay: the trigger is eligible as soon as this user logs on.
        winrt::com_ptr<IActionCollection> actions;
        winrt::com_ptr<IAction> action;
        winrt::check_hresult(task->get_Actions(actions.put()));
        winrt::check_hresult(actions->Create(TASK_ACTION_EXEC, action.put()));
        auto exec = action.as<IExecAction>();
        winrt::check_hresult(exec->put_Path(TaskString(executable.wstring())));
        winrt::check_hresult(exec->put_Arguments(TaskString(arguments)));
        winrt::check_hresult(exec->put_WorkingDirectory(TaskString(executable.parent_path().wstring())));
        winrt::com_ptr<IRegisteredTask> registered;
        winrt::check_hresult(folder->RegisterTaskDefinition(TaskString(name), task.get(),
            existing ? TASK_UPDATE : TASK_CREATE, user.value, empty.value,
            TASK_LOGON_INTERACTIVE_TOKEN, empty.value, registered.put()));
        return ERROR_SUCCESS;
    } catch (const winrt::hresult_error& error) {
        auto code = static_cast<DWORD>(error.code().value);
        return (code & 0xffff0000) == 0x80070000 ? code & 0xffff : code;
    } catch (...) { return ERROR_NOT_ENOUGH_MEMORY; }
}
inline bool SetLogonStartup(const std::filesystem::path& executable, bool enabled,
                            const std::wstring& name = {}) {
    DWORD error{};
    try { error = SetLogonStartupResult(executable, enabled, name.empty() ? StartupTaskName() : name); }
    catch (...) { error = ERROR_NOT_ENOUGH_MEMORY; }
    SetLastError(error);
    return error == ERROR_SUCCESS;
}
}
