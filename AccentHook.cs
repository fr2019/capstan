using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Capstan;

public class AccentHook
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    private const int VK_TAB = 0x09;
    private const int VK_RETURN = 0x0D;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;
    private const int VK_SHIFT = 0x10;
    private const int VK_LEFT = 0x25;
    private const int VK_RIGHT = 0x27;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static LowLevelKeyboardProc? _keyboardProc;
    private static LowLevelMouseProc? _mouseProc;
    private static IntPtr _keyboardHookId = IntPtr.Zero;
    private static IntPtr _mouseHookId = IntPtr.Zero;
    private static AccentOverlay? _overlay;

    private static DispatcherTimer? _longPressTimer;
    private static int _currentVkCode = 0;
    private static char _currentChar = '\0';
    private static bool _isLongPressing = false;
    private static bool _overlayVisible = false;
    private static bool _shiftPressed = false;
    private static bool _originalKeyReleased = false;  // Track if original key was released before allowing number select

    public AccentHook()
    {
        _overlay = new AccentOverlay();
    }

    /// <summary>
    /// Gets the keyboard repeat delay from Windows settings.
    /// Returns delay in milliseconds, slightly reduced to fire before first repeat.
    /// </summary>
    private static int GetKeyboardRepeatDelay()
    {
        // SystemParameters.KeyboardDelay returns 0-3:
        // 0 = ~250ms, 3 = ~1000ms
        // So each unit is roughly (1000-250)/3 = 250ms, base is 250ms
        int delayValue = System.Windows.SystemParameters.KeyboardDelay;

        // Calculate actual delay: 250ms base + (value * 250ms)
        // Then multiply by 0.8 to fire before Windows starts repeating
        int baseDelay = 250 + (delayValue * 250);
        int delayMs = (int)(baseDelay * 0.8);

        System.Diagnostics.Debug.WriteLine($"Capstan: SystemParameters.KeyboardDelay={delayValue}, base={baseDelay}ms, using {delayMs}ms");

        return delayMs;
    }

    public void Install()
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        var moduleHandle = GetModuleHandle(curModule.ModuleName);

        _keyboardProc = KeyboardHookCallback;
        _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);

        _mouseProc = MouseHookCallback;
        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);

        // Use the Windows keyboard repeat delay
        int delayMs = GetKeyboardRepeatDelay();
        _longPressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(delayMs)
        };
        _longPressTimer.Tick += OnLongPressTimer;
    }

    public void Uninstall()
    {
        if (_keyboardHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }
        if (_mouseHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
        _longPressTimer?.Stop();
    }

    private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _overlayVisible)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
            {
                // Check if click is outside overlay
                GetCursorPos(out POINT pt);

                bool clickedInOverlay = false;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_overlay != null && _overlay.IsVisible)
                    {
                        double left = _overlay.Left;
                        double top = _overlay.Top;
                        double right = left + _overlay.ActualWidth;
                        double bottom = top + _overlay.ActualHeight;

                        clickedInOverlay = pt.X >= left && pt.X <= right && pt.Y >= top && pt.Y <= bottom;
                    }
                });

                if (!clickedInOverlay)
                {
                    Application.Current.Dispatcher.Invoke(() => _overlay?.Cancel());
                }
            }
        }
        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private static void OnLongPressTimer(object? sender, EventArgs e)
    {
        _longPressTimer?.Stop();

        System.Diagnostics.Debug.WriteLine($"Capstan: Timer fired, _isLongPressing={_isLongPressing}, char='{_currentChar}'");

        if (_isLongPressing && _currentChar != '\0' && AccentData.HasAccents(_currentChar))
        {
            // Check if we're in a text input context (not a game)
            if (IsLikelyTextInput())
            {
                System.Diagnostics.Debug.WriteLine("Capstan: Showing overlay");
                ShowOverlay();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Capstan: Not a text input context, skipping overlay");
            }
        }
    }

    private static bool IsLikelyTextInput()
    {
        try
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            uint threadId = GetWindowThreadProcessId(foregroundWindow, out uint processId);

            // Check process name - exclude known games
            try
            {
                var process = System.Diagnostics.Process.GetProcessById((int)processId);
                string processName = process.ProcessName.ToLower();

                // Known game processes - definitely not text input
                if (processName.Contains("pubg") || processName.Contains("tslgame") ||
                    processName.Contains("csgo") || processName.Contains("cs2") ||
                    processName.Contains("valorant") || processName.Contains("fortnite") ||
                    processName.Contains("overwatch") || processName.Contains("dota") ||
                    processName.Contains("league") || processName.Contains("minecraft") ||
                    processName.Contains("gta") || processName.Contains("rdr2") ||
                    processName.Contains("destiny") || processName.Contains("apex") ||
                    processName.Contains("battlefield") || processName.Contains("warzone") ||
                    processName.Contains("rust") || processName.Contains("tarkov") ||
                    processName.Contains("dayz") || processName.Contains("arma"))
                {
                    return false;
                }

                // Known text-friendly apps
                if (processName.Contains("searchhost") ||    // Windows Search
                    processName.Contains("searchui") ||      // Windows Search
                    processName.Contains("explorer") ||      // File Explorer
                    processName.Contains("notepad") ||
                    processName.Contains("code") ||          // VS Code
                    processName.Contains("devenv") ||        // Visual Studio
                    processName.Contains("chrome") ||
                    processName.Contains("firefox") ||
                    processName.Contains("msedge") ||
                    processName.Contains("opera") ||
                    processName.Contains("brave") ||
                    processName.Contains("slack") ||
                    processName.Contains("discord") ||
                    processName.Contains("teams") ||
                    processName.Contains("outlook") ||
                    processName.Contains("word") ||
                    processName.Contains("excel") ||
                    processName.Contains("powerpnt") ||
                    processName.Contains("onenote") ||
                    processName.Contains("windowsterminal") ||
                    processName.Contains("powershell") ||
                    processName.Contains("cmd"))
                {
                    return true;
                }
            }
            catch { }

            // Check window class
            var gui = new GUITHREADINFO();
            gui.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));

            if (GetGUIThreadInfo(threadId, ref gui) && gui.hwndFocus != IntPtr.Zero)
            {
                StringBuilder className = new StringBuilder(256);
                GetClassName(gui.hwndFocus, className, className.Capacity);
                string cls = className.ToString().ToLower();

                // Common text input window classes
                if (cls.Contains("edit") ||
                    cls.Contains("richedit") ||
                    cls.Contains("scintilla") ||
                    cls.Contains("textbox") ||
                    cls.Contains("searchbox") ||
                    cls.Contains("windows.ui"))  // UWP controls
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private static void ShowOverlay()
    {
        _overlayVisible = true;
        _originalKeyReleased = false;  // Require key release before number selection

        Application.Current.Dispatcher.Invoke(() =>
        {
            _overlay?.Show(_currentChar, _shiftPressed,
                onSelect: (selectedChar) =>
                {
                    _overlayVisible = false;
                    // Small delay to ensure focus returns to original app
                    System.Threading.Tasks.Task.Delay(10).ContinueWith(_ =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            // Select the original character with Shift+Left
                            // Then typing the accented char will replace it
                            SendKeyCombo(0x10, 0x25); // Shift+Left
                            SendCharacter(selectedChar); // Replaces selection
                        });
                    });
                },
                onCancel: () =>
                {
                    _overlayVisible = false;
                });
        });
    }

    private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (!Settings.AccentHoldEnabled || nCode < 0)
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);

        int vkCode = Marshal.ReadInt32(lParam);
        int msg = wParam.ToInt32();

        // Track shift state (VK_SHIFT=0x10, VK_LSHIFT=0xA0, VK_RSHIFT=0xA1)
        if (vkCode == VK_SHIFT || vkCode == 0xA0 || vkCode == 0xA1)
        {
            bool wasShiftPressed = _shiftPressed;
            _shiftPressed = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);

            // Update case in overlay when shift state changes
            if (_overlayVisible && _shiftPressed != wasShiftPressed)
            {
                Application.Current.Dispatcher.Invoke(() => _overlay?.SetUppercase(_shiftPressed));
            }

            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        // Handle overlay navigation when visible
        if (_overlayVisible)
        {
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                if (vkCode == VK_TAB || vkCode == VK_RIGHT)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_shiftPressed)
                            _overlay?.SelectPrevious();
                        else
                            _overlay?.SelectNext();
                    });
                    return (IntPtr)1; // Block
                }
                else if (vkCode == VK_LEFT)
                {
                    Application.Current.Dispatcher.Invoke(() => _overlay?.SelectPrevious());
                    return (IntPtr)1;
                }
                else if (vkCode == VK_RETURN || vkCode == VK_SPACE)
                {
                    Application.Current.Dispatcher.Invoke(() => _overlay?.Confirm());
                    return (IntPtr)1;
                }
                else if (vkCode == VK_ESCAPE)
                {
                    Application.Current.Dispatcher.Invoke(() => _overlay?.Cancel());
                    return (IntPtr)1;
                }
                else if (vkCode >= 0x31 && vkCode <= 0x39) // 1-9
                {
                    // Only allow number selection if original key was released
                    // This prevents Shift+4 ($) from immediately selecting option 4
                    if (_originalKeyReleased)
                    {
                        int number = vkCode - 0x30;
                        Application.Current.Dispatcher.Invoke(() => _overlay?.SelectByNumber(number));
                    }
                    return (IntPtr)1;
                }
                else if (vkCode == _currentVkCode)
                {
                    // Same key held down - just block repeats, don't cycle
                    return (IntPtr)1;
                }
                else
                {
                    // Any other key cancels overlay
                    Application.Current.Dispatcher.Invoke(() => _overlay?.Cancel());

                    // Check if this new key is accentable
                    char c = VkCodeToChar(vkCode, _shiftPressed);
                    if (c != '\0' && AccentData.HasAccents(c))
                    {
                        // Start tracking the new key immediately
                        _currentVkCode = vkCode;
                        _currentChar = c;
                        _isLongPressing = true;
                        _longPressTimer?.Stop();
                        _longPressTimer?.Start();
                        // Let first keypress through
                        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
                    }
                    else
                    {
                        // Not accentable, just reset and pass through
                        _isLongPressing = false;
                        _currentVkCode = 0;
                        _currentChar = '\0';
                        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
                    }
                }
            }
            else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
            {
                if (vkCode == _currentVkCode)
                {
                    // Key released - keep overlay up, mark that original key was released
                    _isLongPressing = false;
                    _originalKeyReleased = true;
                    _currentVkCode = 0;
                    _currentChar = '\0';
                    // Don't confirm or cancel - let user click or press number
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        // Normal key handling (overlay not visible)
        if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
        {
            char c = VkCodeToChar(vkCode, _shiftPressed);

            System.Diagnostics.Debug.WriteLine($"Capstan: Key down vk=0x{vkCode:X}, shift={_shiftPressed}, char='{c}' (0x{(int)c:X}), hasAccents={AccentData.HasAccents(c)}");

            if (c != '\0' && AccentData.HasAccents(c))
            {
                // Only do accent handling if we're in a text input context (not a game)
                if (IsLikelyTextInput())
                {
                    if (!_isLongPressing)
                    {
                        // New key press - start timer
                        _currentVkCode = vkCode;
                        _currentChar = c;
                        _isLongPressing = true;
                        _longPressTimer?.Stop();
                        _longPressTimer?.Start();
                        System.Diagnostics.Debug.WriteLine($"Capstan: Started timer for '{c}'");
                        // Let first keypress through
                        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
                    }
                    else if (vkCode == _currentVkCode)
                    {
                        // Same key repeating while timer running - block it
                        return (IntPtr)1;
                    }
                }
            }
        }
        else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
        {
            if (vkCode == _currentVkCode)
            {
                // Key released before timer - just let it be a normal keypress
                _longPressTimer?.Stop();
                _isLongPressing = false;
                _currentVkCode = 0;
                _currentChar = '\0';
            }
        }

        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private static char VkCodeToChar(int vkCode, bool shift)
    {
        try
        {
            // Use ToUnicodeEx to get the actual character based on current keyboard layout
            byte[] keyboardState = new byte[256];
            GetKeyboardState(keyboardState);

            // GetKeyboardState already has the correct shift state, but ensure it's set
            // This handles cases where shift is physically pressed
            if (shift)
            {
                keyboardState[VK_SHIFT] = 0x80;
                keyboardState[0xA0] = 0x80; // VK_LSHIFT
            }

            IntPtr hkl = GetKeyboardLayout(0);

            StringBuilder sb = new StringBuilder(4);
            uint scanCode = MapVirtualKey((uint)vkCode, 0); // MAPVK_VK_TO_VSC
            int result = ToUnicodeEx((uint)vkCode, scanCode, keyboardState, sb, sb.Capacity, 0, hkl);

            if (result >= 1 && sb.Length > 0)
                return sb[0];
        }
        catch { }

        return '\0';
    }

    private static void SendBackspace()
    {
        var inputs = new INPUT[2];

        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = 0x08; // VK_BACK
        inputs[0].u.ki.dwFlags = 0;

        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = 0x08;
        inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendKey(ushort vkCode)
    {
        var inputs = new INPUT[2];

        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = vkCode;
        inputs[0].u.ki.dwFlags = 0;

        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = vkCode;
        inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendKeyCombo(ushort modifierVk, ushort keyVk)
    {
        var inputs = new INPUT[4];

        // Press modifier
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = modifierVk;
        inputs[0].u.ki.dwFlags = 0;

        // Press key
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = keyVk;
        inputs[1].u.ki.dwFlags = 0;

        // Release key
        inputs[2].type = INPUT_KEYBOARD;
        inputs[2].u.ki.wVk = keyVk;
        inputs[2].u.ki.dwFlags = KEYEVENTF_KEYUP;

        // Release modifier
        inputs[3].type = INPUT_KEYBOARD;
        inputs[3].u.ki.wVk = modifierVk;
        inputs[3].u.ki.dwFlags = KEYEVENTF_KEYUP;

        SendInput(4, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendCharacter(char c)
    {
        var inputs = new INPUT[2];

        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = 0;
        inputs[0].u.ki.wScan = c;
        inputs[0].u.ki.dwFlags = KEYEVENTF_UNICODE;

        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = 0;
        inputs[1].u.ki.wScan = c;
        inputs[1].u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    #region Native Methods

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetCaretPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        IntPtr hwnd,
        uint dwId,
        ref Guid riid,
        out IntPtr ppvObject);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    #endregion
}