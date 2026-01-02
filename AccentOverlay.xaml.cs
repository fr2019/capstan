using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Capstan;

public partial class AccentOverlay : Window
{
    private List<AccentItem> _items = new();
    private char[] _baseAccents = Array.Empty<char>();  // Store original accents
    private char _baseChar = '\0';  // Store the original character
    private int _selectedIndex = 0;
    private Action<char>? _onSelect;
    private Action? _onCancel;
    private IntPtr _hwnd;
    private System.Windows.Threading.DispatcherTimer? _topmostTimer;
    public AccentOverlay()
    {
        try
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;

            // Start off-screen to prevent white flash on first show
            Left = -10000;
            Top = -10000;
            App.Log("AccentOverlay initialized");
        }
        catch (Exception ex)
        {
            App.Log($"AccentOverlay init failed: {ex}");
            throw;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // Set extended window styles for a topmost tool window
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
        exStyle &= ~WS_EX_APPWINDOW; // Remove from taskbar
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);

        // Hook window messages to maintain topmost
        HwndSource source = HwndSource.FromHwnd(_hwnd);
        source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_WINDOWPOSCHANGING = 0x0046;

        if (msg == WM_WINDOWPOSCHANGING)
        {
            // Force HWND_TOPMOST on every position change
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Additional topmost enforcement after load
        if (_hwnd != IntPtr.Zero)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    public void Show(char baseChar, bool shiftHeld, Action<char> onSelect, Action onCancel)
    {
        var accents = AccentData.GetAccents(baseChar);
        if (accents == null || accents.Length == 0)
        {
            onCancel();
            return;
        }

        _onSelect = onSelect;
        _onCancel = onCancel;
        _selectedIndex = 0;
        _baseChar = baseChar;
        _baseAccents = accents;

        // Build items - use uppercase if shift is held, otherwise lowercase
        _items.Clear();
        for (int i = 0; i < accents.Length; i++)
        {
            char c = shiftHeld ? char.ToUpper(accents[i]) : char.ToLower(accents[i]);
            _items.Add(new AccentItem
            {
                Character = c.ToString(),
                Index = i
            });
        }

        UpdateSelection();
        AccentItems.ItemsSource = null;
        AccentItems.ItemsSource = _items;

        // Show which character we're replacing (match shift state)
        char displayChar = shiftHeld ? char.ToUpper(baseChar) : char.ToLower(baseChar);
        BaseCharLabel.Text = displayChar.ToString();

        // Set the app icon
        try
        {
            var iconUri = new Uri("pack://application:,,,/capstan.ico");
            var iconStream = Application.GetResourceStream(iconUri)?.Stream;
            if (iconStream != null)
            {
                var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(
                    iconStream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.Default);
                AppIcon.Source = decoder.Frames[0];
            }
        }
        catch { }

        // Center on screen like Alt+Tab
        Opacity = 0;  // Hide until positioned
        Show();

        // Position after showing so we have actual dimensions
        Dispatcher.InvokeAsync(() =>
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            Left = (screenWidth - ActualWidth) / 2;
            Top = (screenHeight - ActualHeight) / 2;

            Opacity = 1;  // Show after positioned
            ForceTopmost();
        }, System.Windows.Threading.DispatcherPriority.Loaded);

        // Force topmost repeatedly while visible
        _topmostTimer?.Stop();
        _topmostTimer = new System.Windows.Threading.DispatcherTimer();
        _topmostTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60fps
        _topmostTimer.Tick += (s, e) =>
        {
            if (IsVisible)
                ForceTopmost();
            else
                _topmostTimer?.Stop();
        };
        _topmostTimer.Start();
    }

    private void ForceTopmost()
    {
        if (_hwnd == IntPtr.Zero)
            _hwnd = new WindowInteropHelper(this).Handle;

        if (_hwnd != IntPtr.Zero)
        {
            // Use multiple approaches to force topmost

            // 1. Set window to topmost band
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

            // 2. Try SetWindowBand (Windows 8+) to put in system topmost band
            // ZBID_UIACCESS = 4 is above normal topmost
            try
            {
                SetWindowBand(_hwnd, IntPtr.Zero, ZBID_UIACCESS);
            }
            catch { }

            // 3. Force repaint
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
        }
    }

    public void SelectNext()
    {
        if (_items.Count == 0) return;
        _selectedIndex = (_selectedIndex + 1) % _items.Count;
        UpdateSelection();
    }

    public void SelectPrevious()
    {
        if (_items.Count == 0) return;
        _selectedIndex = (_selectedIndex - 1 + _items.Count) % _items.Count;
        UpdateSelection();
    }

    public void SelectByNumber(int number)
    {
        if (number >= 1 && number <= _items.Count)
        {
            _selectedIndex = number - 1;
            Confirm();
        }
    }

    public void SetUppercase(bool uppercase)
    {
        // Set all characters to upper or lower case based on shift state
        for (int i = 0; i < _items.Count; i++)
        {
            char c = _baseAccents[i];
            _items[i].Character = (uppercase ? char.ToUpper(c) : char.ToLower(c)).ToString();
        }

        // Update the base char label too (just the character, italicized in XAML)
        char displayChar = uppercase ? char.ToUpper(_baseChar) : char.ToLower(_baseChar);
        BaseCharLabel.Text = displayChar.ToString();

        // Refresh the display
        AccentItems.ItemsSource = null;
        AccentItems.ItemsSource = _items;
        UpdateSelection();
    }

    public void Confirm()
    {
        if (_items.Count > 0 && _selectedIndex >= 0 && _selectedIndex < _items.Count)
        {
            char selected = _items[_selectedIndex].Character[0];
            Hide();
            _onSelect?.Invoke(selected);
        }
    }

    public void Cancel()
    {
        Hide();
        _onCancel?.Invoke();
    }

    private void UpdateSelection()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            _items[i].IsSelected = (i == _selectedIndex);
        }

        // Force refresh
        AccentItems.ItemsSource = null;
        AccentItems.ItemsSource = _items;
    }

    private void AccentItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AccentItem item)
        {
            _selectedIndex = item.Index;
            Confirm();
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
    }

    #region Win32 API

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_APPWINDOW = 0x00040000;

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;

    // Window band IDs for SetWindowBand (undocumented)
    private const int ZBID_UIACCESS = 4;  // Above normal topmost

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    // Undocumented API - may not exist on all Windows versions
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowBand(IntPtr hWnd, IntPtr hwndInsertAfter, int dwBand);

    #endregion
}

public class AccentItem : INotifyPropertyChanged
{
    private string _character = "";
    public string Character
    {
        get => _character;
        set
        {
            _character = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Character)));
        }
    }
    public int Index { get; set; }

    public string NumberLabel => Index < 9 ? (Index + 1).ToString() : "";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Background)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Foreground)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NumberForeground)));
        }
    }

    public Brush Background => IsSelected ? Brushes.DodgerBlue : Brushes.Transparent;
    public Brush Foreground => IsSelected ? Brushes.White : Brushes.Black;
    public Brush NumberForeground => IsSelected ? Brushes.LightGray : Brushes.Gray;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class AccentData
{
    private static readonly Dictionary<char, char[]> Accents = new()
    {
        // Lowercase vowels
        ['a'] = new[] { 'à', 'á', 'â', 'ä', 'æ', 'ã', 'å', 'ā' },
        ['e'] = new[] { 'è', 'é', 'ê', 'ë', 'ē', 'ė', 'ę' },
        ['i'] = new[] { 'ì', 'í', 'î', 'ï', 'ī', 'į' },
        ['o'] = new[] { 'ò', 'ó', 'ô', 'ö', 'õ', 'ø', 'ō', 'œ' },
        ['u'] = new[] { 'ù', 'ú', 'û', 'ü', 'ū' },
        ['y'] = new[] { 'ÿ', 'ý' },

        // Uppercase vowels
        ['A'] = new[] { 'À', 'Á', 'Â', 'Ä', 'Æ', 'Ã', 'Å', 'Ā' },
        ['E'] = new[] { 'È', 'É', 'Ê', 'Ë', 'Ē', 'Ė', 'Ę' },
        ['I'] = new[] { 'Ì', 'Í', 'Î', 'Ï', 'Ī', 'Į' },
        ['O'] = new[] { 'Ò', 'Ó', 'Ô', 'Ö', 'Õ', 'Ø', 'Ō', 'Œ' },
        ['U'] = new[] { 'Ù', 'Ú', 'Û', 'Ü', 'Ū' },
        ['Y'] = new[] { 'Ÿ', 'Ý' },

        // Consonants
        ['c'] = new[] { 'ç', 'ć', 'č' },
        ['C'] = new[] { 'Ç', 'Ć', 'Č' },
        ['n'] = new[] { 'ñ', 'ń' },
        ['N'] = new[] { 'Ñ', 'Ń' },
        ['s'] = new[] { 'ß', 'ś', 'š' },
        ['S'] = new[] { 'Ś', 'Š' },
        ['z'] = new[] { 'ž', 'ź', 'ż' },
        ['Z'] = new[] { 'Ž', 'Ź', 'Ż' },
        ['l'] = new[] { 'ł' },
        ['L'] = new[] { 'Ł' },
        ['d'] = new[] { 'ð' },
        ['D'] = new[] { 'Ð' },
        ['t'] = new[] { 'þ' },
        ['T'] = new[] { 'Þ' },

        // Greek lowercase with tonos/dialytika variants
        ['α'] = new[] { 'ά' },
        ['ε'] = new[] { 'έ' },
        ['η'] = new[] { 'ή' },
        ['ι'] = new[] { 'ί', 'ϊ', 'ΐ' },
        ['ο'] = new[] { 'ό' },
        ['υ'] = new[] { 'ύ', 'ϋ', 'ΰ' },
        ['ω'] = new[] { 'ώ' },

        // Greek uppercase with tonos/dialytika variants
        ['Α'] = new[] { 'Ά' },
        ['Ε'] = new[] { 'Έ' },
        ['Η'] = new[] { 'Ή' },
        ['Ι'] = new[] { 'Ί', 'Ϊ' },
        ['Ο'] = new[] { 'Ό' },
        ['Υ'] = new[] { 'Ύ', 'Ϋ' },
        ['Ω'] = new[] { 'Ώ' },

        // Cyrillic Russian - е to ё, и to й
        ['е'] = new[] { 'ё' },
        ['Е'] = new[] { 'Ё' },
        ['и'] = new[] { 'й' },
        ['И'] = new[] { 'Й' },

        // Cyrillic Ukrainian - і to ї, г to ґ
        ['і'] = new[] { 'ї' },
        ['І'] = new[] { 'Ї' },
        ['г'] = new[] { 'ґ' },
        ['Г'] = new[] { 'Ґ' },

        // Cyrillic Belarusian - у to ў
        ['у'] = new[] { 'ў' },
        ['У'] = new[] { 'Ў' },

        // Punctuation
        ['?'] = new[] { '¿', ';' }, // semicolon is Greek question mark
        ['!'] = new[] { '¡' },
        ['"'] = new[] { '\u201C', '\u201D', '\u201E', '\u00BB', '\u00AB' }, // " " „ » «
        ['\''] = new[] { '\u2018', '\u2019', '\u201A', '\u203A', '\u2039' }, // ' ' ‚ › ‹
        ['-'] = new[] { '\u2013', '\u2014', '\u00B7' }, // – — ·

        // Currency symbols - each shows other currencies (excluding itself)
        ['$'] = new[] { '€', '£', '¥', '¢', '₹', '₽', '₩', '₪', '₿' },  // Dollar
        ['€'] = new[] { '$', '£', '¥', '¢', '₹', '₽', '₩', '₪', '₿' },  // Euro
        ['£'] = new[] { '$', '€', '¥', '¢', '₹', '₽', '₩', '₪', '₿' },  // Pound
        ['¥'] = new[] { '$', '€', '£', '¢', '₹', '₽', '₩', '₪', '₿' },  // Yen/Yuan
        ['¢'] = new[] { '$', '€', '£', '¥', '₹', '₽', '₩', '₪', '₿' },  // Cent
        ['₹'] = new[] { '$', '€', '£', '¥', '¢', '₽', '₩', '₪', '₿' },  // Indian Rupee
        ['₽'] = new[] { '$', '€', '£', '¥', '¢', '₹', '₩', '₪', '₿' },  // Russian Ruble
        ['₩'] = new[] { '$', '€', '£', '¥', '¢', '₹', '₽', '₪', '₿' },  // Korean Won
        ['₪'] = new[] { '$', '€', '£', '¥', '¢', '₹', '₽', '₩', '₿' },  // Israeli Shekel
        ['₿'] = new[] { '$', '€', '£', '¥', '¢', '₹', '₽', '₩', '₪' },  // Bitcoin

        // Common symbols
        ['.'] = new[] { '…', '•' },
    };

    public static char[]? GetAccents(char c)
    {
        return Accents.TryGetValue(c, out var accents) ? accents : null;
    }

    public static bool HasAccents(char c)
    {
        return Accents.ContainsKey(c);
    }
}