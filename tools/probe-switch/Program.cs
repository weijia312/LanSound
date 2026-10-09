// 切换系统默认播放设备（IPolicyConfig，undocumented COM）。
// 用途：自动化复现"切换默认音频设备后手机端没声"这类问题，不必手点系统设置。
//
// 用法:
//   probe-switch.exe list                 列出所有可用播放设备及其序号
//   probe-switch.exe set <序号>           把默认设备切到该序号
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

if (args.Length < 1) { Console.WriteLine("用法: probe-switch.exe list | set <序号>"); return 1; }

using var en = new MMDeviceEnumerator();
var devices = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

if (args[0] == "list")
{
    for (var i = 0; i < devices.Count; i++)
    {
        var isDefault = false;
        try
        {
            using var d = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            isDefault = d.ID == devices[i].ID;
        }
        catch { }
        Console.WriteLine($"  [{i}] {devices[i].FriendlyName}{(isDefault ? "   <== 当前默认" : "")}");
    }
    foreach (var d in devices) d.Dispose();
    return 0;
}

if (args[0] == "set")
{
    if (!int.TryParse(args[1], out var idx) || idx < 0 || idx >= devices.Count)
    {
        Console.WriteLine($"序号无效（0..{devices.Count - 1}）");
        return 1;
    }
    var target = devices[idx];
    Console.WriteLine($"  切换到: {target.FriendlyName}");
    var pc = (IPolicyConfig)new CPolicyConfigClient();
    // 三个 Role 都设，和系统设置面板的行为一致
    foreach (var role in new[] { 0, 1, 2 })   // eConsole, eMultimedia, eCommunications
    {
        var hr = pc.SetDefaultEndpoint(target.ID, role);
        if (hr != 0) Console.WriteLine($"    SetDefaultEndpoint(role={role}) 返回 0x{hr:X8}");
    }
    foreach (var d in devices) d.Dispose();
    Console.WriteLine("  已切换");
    return 0;
}

Console.WriteLine("未知命令");
return 1;

[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig
{
    int GetMixFormat(string deviceName, IntPtr format);
    int GetDeviceFormat(string deviceName, bool isDefault, IntPtr format);
    int ResetDeviceFormat(string deviceName);
    int SetDeviceFormat(string deviceName, IntPtr endpointFormat, IntPtr mixFormat);
    int GetProcessingPeriod(string deviceName, bool isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
    int SetProcessingPeriod(string deviceName, IntPtr period);
    int GetShareMode(string deviceName, IntPtr mode);
    int SetShareMode(string deviceName, IntPtr mode);
    int GetPropertyValue(string deviceName, IntPtr key, IntPtr value);
    int SetPropertyValue(string deviceName, IntPtr key, IntPtr value);
    int SetDefaultEndpoint(string deviceName, int role);
    int SetEndpointVisibility(string deviceName, bool visible);
}

[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
class CPolicyConfigClient { }
