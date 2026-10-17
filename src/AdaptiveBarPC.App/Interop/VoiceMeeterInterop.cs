using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AdaptiveBarPC.Interop;

internal sealed class VoiceMeeterInterop : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int VBVMR_LoginDelegate();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int VBVMR_LogoutDelegate();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int VBVMR_IsParametersDirtyDelegate();

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int VBVMR_GetParameterFloatDelegate(string szParamName, out float pValue);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int VBVMR_SetParameterFloatDelegate(string szParamName, float fValue);

    private IntPtr _dllHandle = IntPtr.Zero;
    private VBVMR_LoginDelegate? _login;
    private VBVMR_LogoutDelegate? _logout;
    private VBVMR_IsParametersDirtyDelegate? _isParametersDirty;
    private VBVMR_GetParameterFloatDelegate? _getParameterFloat;
    private VBVMR_SetParameterFloatDelegate? _setParameterFloat;
    private bool _isLoggedIn;

    public bool IsLoaded => _dllHandle != IntPtr.Zero;
    public bool IsLoggedIn => _isLoggedIn;

    public VoiceMeeterInterop()
    {
        Initialize();
    }

    private void Initialize()
    {
        var dllPath = FindDllPath();
        if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
        {
            var fallback = GetDllName();
            if (!NativeLibrary.TryLoad(fallback, out _dllHandle))
                return;
        }
        else
        {
            if (!NativeLibrary.TryLoad(dllPath, out _dllHandle))
                return;
        }

        _login = GetExport<VBVMR_LoginDelegate>("VBVMR_Login");
        _logout = GetExport<VBVMR_LogoutDelegate>("VBVMR_Logout");
        _isParametersDirty = GetExport<VBVMR_IsParametersDirtyDelegate>("VBVMR_IsParametersDirty");
        _getParameterFloat = GetExport<VBVMR_GetParameterFloatDelegate>("VBVMR_GetParameterFloat");
        _setParameterFloat = GetExport<VBVMR_SetParameterFloatDelegate>("VBVMR_SetParameterFloat");
    }

    private T? GetExport<T>(string name) where T : Delegate
    {
        if (_dllHandle == IntPtr.Zero)
            return null;

        if (NativeLibrary.TryGetExport(_dllHandle, name, out var procAddress))
            return Marshal.GetDelegateForFunctionPointer<T>(procAddress);

        return null;
    }

    public int Login()
    {
        if (_login is null)
            return -1;

        var result = _login();
        if (result == 0 || result == 1)
            _isLoggedIn = true;

        return result;
    }

    public int Logout()
    {
        if (_logout is null || !_isLoggedIn)
            return 0;

        var result = _logout();
        _isLoggedIn = false;
        return result;
    }

    public int IsParametersDirty()
    {
        return _isParametersDirty?.Invoke() ?? -1;
    }

    public int GetParameterFloat(string paramName, out float value)
    {
        value = 0.0f;
        if (_getParameterFloat is null)
            return -1;

        return _getParameterFloat(paramName, out value);
    }

    public int SetParameterFloat(string paramName, float value)
    {
        if (_setParameterFloat is null)
            return -1;

        return _setParameterFloat(paramName, value);
    }

    private static string GetDllName() =>
        Environment.Is64BitProcess ? "VoicemeeterRemote64.dll" : "VoicemeeterRemote.dll";

    private static string? FindDllPath()
    {
        var dllName = GetDllName();

        // 1. Check registry uninstall key (32-bit and 64-bit hives)
        var registryKeys = new[]
        {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}"
        };

        foreach (var key in registryKeys)
        {
            var uninstallString = Registry.GetValue(key, "UninstallString", null) as string;
            if (!string.IsNullOrWhiteSpace(uninstallString))
            {
                var dir = Path.GetDirectoryName(uninstallString);
                if (!string.IsNullOrEmpty(dir))
                {
                    var candidate = Path.Combine(dir, dllName);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }

        // 2. Check standard Program Files directories
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pathX86 = Path.Combine(programFilesX86, "VB", "Voicemeeter", dllName);
        if (File.Exists(pathX86))
            return pathX86;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var path = Path.Combine(programFiles, "VB", "Voicemeeter", dllName);
        if (File.Exists(path))
            return path;

        return null;
    }

    public void Dispose()
    {
        Logout();

        if (_dllHandle != IntPtr.Zero)
        {
            NativeLibrary.Free(_dllHandle);
            _dllHandle = IntPtr.Zero;
        }

        _login = null;
        _logout = null;
        _isParametersDirty = null;
        _getParameterFloat = null;
        _setParameterFloat = null;
    }
}
