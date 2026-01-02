using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Capstan;

public class KeyboardHook
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int VK_CAPITAL = 0x14;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static LowLevelKeyboardProc? _proc;
    private static LanguageOverlay? _overlay;
    private static IntPtr _hookId = IntPtr.Zero;
    private static bool _isLayout2 = false;
    private static int _cycleIndex = 0;

    public KeyboardHook(LanguageOverlay overlay)
    {
        _overlay = overlay;
    }

    public void Install()
    {
        try
        {
            // Clear caps lock if it's on
            ClearCapsLock();

            _proc = HookCallback;
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);

            if (_hookId == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                App.Log($"Failed to install keyboard hook. Error: {error}");
                MessageBox.Show($"Failed to install keyboard hook. Error: {error}", "Capstan", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                App.Log("Keyboard hook installed successfully");
            }
        }
        catch (Exception ex)
        {
            App.Log($"KeyboardHook.Install failed: {ex}");
            throw;
        }
    }

    private static void ClearCapsLock()
    {
        // Check if caps lock is on
        if ((GetKeyState(VK_CAPITAL) & 0x0001) != 0)
        {
            // Simulate key press to turn it off
            keybd_event(VK_CAPITAL, 0x45, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(VK_CAPITAL, 0x45, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
            App.Log("Cleared caps lock");
        }
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            App.Log("Keyboard hook uninstalled");
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (vkCode == VK_CAPITAL && Settings.HookEnabled)
                {
                    if (Settings.CycleAllLayouts)
                    {
                        CycleToNextLayout();
                    }
                    else
                    {
                        _isLayout2 = !_isLayout2;
                        SwitchToLayout(_isLayout2 ? Settings.Layout2 : Settings.Layout1);
                    }

                    return (IntPtr)1;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"HookCallback error: {ex.Message}");
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static void CycleToNextLayout()
    {
        if (Settings.AllLayoutsOrdered.Count == 0) return;

        _cycleIndex = (_cycleIndex + 1) % Settings.AllLayoutsOrdered.Count;
        var layout = Settings.AllLayoutsOrdered[_cycleIndex];
        SwitchToLayout(layout.Hkl);
    }

    private static void SwitchToLayout(long hklValue)
    {
        try
        {
            if (hklValue == 0) return;

            IntPtr hkl = new IntPtr(hklValue);

            // Activate the keyboard layout system-wide
            ActivateKeyboardLayout(hkl, KLF_SETFORPROCESS);

            // Also post to foreground window for immediate effect
            IntPtr foregroundWindow = GetForegroundWindow();
            PostMessage(foregroundWindow, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);

            if (Settings.ShowOverlay)
            {
                Application.Current.Dispatcher.Invoke(DispatcherPriority.Send, () =>
                {
                    string displayName = hklValue.ToString("X8");
                    if (Settings.InstalledLayouts.TryGetValue(hklValue, out var layout))
                    {
                        displayName = layout.DisplayName;
                    }
                    _overlay?.Flash(displayName);
                });
            }
        }
        catch (Exception ex)
        {
            App.Log($"SwitchToLayout failed: {ex}");
        }
    }

    #region Native Methods

    private const uint KLF_SETFORPROCESS = 0x00000100;
    private const int WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    #endregion
}
