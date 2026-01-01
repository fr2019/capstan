using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace Capstan;

public partial class MainWindow : Window
{
    private bool _isLoading = true;  // Start true to block events during init

    public MainWindow()
    {
        Settings.Load();
        InitializeComponent();
        LoadAvailableLayouts();

        // Monitor for language changes
        InputLanguageManager.Current.InputLanguageChanged += OnInputLanguageChanged;
    }

    private void OnInputLanguageChanged(object sender, InputLanguageEventArgs e)
    {
        RefreshLayoutList();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RefreshLayoutList();
    }

    private void RefreshLayoutList()
    {
        long? prevLayout1 = (Lang1Combo.SelectedItem as KeyboardLayoutItem)?.Hkl;
        long? prevLayout2 = (Lang2Combo.SelectedItem as KeyboardLayoutItem)?.Hkl;

        Lang1Combo.Items.Clear();
        Lang2Combo.Items.Clear();
        LoadAvailableLayouts(prevLayout1, prevLayout2);
    }

    private List<KeyboardLayoutItem> _allLayouts = new();

    private void LoadAvailableLayouts(long? selectLayout1 = null, long? selectLayout2 = null)
    {
        _isLoading = true;

        _allLayouts = KeyboardLayoutItem.GetInstalledLayouts();

        // Store for overlay use
        Settings.InstalledLayouts = _allLayouts.ToDictionary(l => l.Hkl, l => l);

        if (_allLayouts.Count < 2)
        {
            NormalUI.Visibility = Visibility.Collapsed;
            SingleLangUI.Visibility = Visibility.Visible;
            Settings.HookEnabled = false;
            return;
        }

        NormalUI.Visibility = Visibility.Visible;
        SingleLangUI.Visibility = Visibility.Collapsed;
        Settings.HookEnabled = true;

        OverlayCheckbox.IsChecked = Settings.ShowOverlay;
        AccentCheckbox.IsChecked = Settings.AccentHoldEnabled;

        // Initialize startup checkbox - default to enabled on first run
        bool startupEnabled = IsStartupEnabled();
        bool firstRun = IsFirstRun();

        if (firstRun)
        {
            // On first run, enable startup by default
            StartupCheckbox.IsChecked = true;
            if (!startupEnabled)
            {
                SetStartupEnabled(true);
            }
            MarkFirstRunComplete();
        }
        else
        {
            StartupCheckbox.IsChecked = startupEnabled;
        }

        // Determine initial selections
        long layout1Hkl = selectLayout1 ?? Settings.Layout1;
        long layout2Hkl = selectLayout2 ?? Settings.Layout2;

        // If no saved selection or saved selection not found, use defaults
        if (!_allLayouts.Any(l => l.Hkl == layout1Hkl))
            layout1Hkl = _allLayouts[0].Hkl;
        if (!_allLayouts.Any(l => l.Hkl == layout2Hkl) || layout2Hkl == layout1Hkl)
            layout2Hkl = _allLayouts.FirstOrDefault(l => l.Hkl != layout1Hkl)?.Hkl ?? _allLayouts[1].Hkl;

        // Populate dropdowns
        PopulateDropdowns();

        // Set selections
        SelectLayout(Lang1Combo, layout1Hkl);
        SelectLayout(Lang2Combo, layout2Hkl);

        // Update disabled states
        UpdateDisabledItems();

        // Populate cycle list
        Settings.AllLayoutsOrdered = _allLayouts;
        var cycleItems = _allLayouts.Select((layout, index) => new { Index = $"{index + 1}.", Layout = layout }).ToList();
        CycleLayoutsList.ItemsSource = cycleItems;

        // Set mode radio
        TwoFavoritesRadio.IsChecked = !Settings.CycleAllLayouts;
        CycleAllRadio.IsChecked = Settings.CycleAllLayouts;
        TwoFavoritesPanel.Visibility = Settings.CycleAllLayouts ? Visibility.Collapsed : Visibility.Visible;
        CycleAllPanel.Visibility = Settings.CycleAllLayouts ? Visibility.Visible : Visibility.Collapsed;

        Lang1Combo.SelectionChanged -= OnLayoutSelectionChanged;
        Lang2Combo.SelectionChanged -= OnLayoutSelectionChanged;
        Lang1Combo.SelectionChanged += OnLayoutSelectionChanged;
        Lang2Combo.SelectionChanged += OnLayoutSelectionChanged;

        _isLoading = false;
    }

    private void PopulateDropdowns()
    {
        Lang1Combo.Items.Clear();
        Lang2Combo.Items.Clear();

        foreach (var layout in _allLayouts)
        {
            Lang1Combo.Items.Add(new KeyboardLayoutItem(layout.Hkl, layout.DisplayName, layout.CountryCode));
            Lang2Combo.Items.Add(new KeyboardLayoutItem(layout.Hkl, layout.DisplayName, layout.CountryCode));
        }
    }

    private void SelectLayout(System.Windows.Controls.ComboBox combo, long hkl)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is KeyboardLayoutItem item && item.Hkl == hkl)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    private void UpdateDisabledItems()
    {
        long? selected1 = (Lang1Combo.SelectedItem as KeyboardLayoutItem)?.Hkl;
        long? selected2 = (Lang2Combo.SelectedItem as KeyboardLayoutItem)?.Hkl;

        // Update Lang1Combo - disable item selected in Lang2
        for (int i = 0; i < Lang1Combo.Items.Count; i++)
        {
            if (Lang1Combo.Items[i] is KeyboardLayoutItem item)
            {
                item.IsDisabled = (item.Hkl == selected2);
            }
        }

        // Update Lang2Combo - disable item selected in Lang1
        for (int i = 0; i < Lang2Combo.Items.Count; i++)
        {
            if (Lang2Combo.Items[i] is KeyboardLayoutItem item)
            {
                item.IsDisabled = (item.Hkl == selected1);
            }
        }
    }

    private void OnLayoutSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Lang1Combo.SelectedItem is not KeyboardLayoutItem layout1 ||
            Lang2Combo.SelectedItem is not KeyboardLayoutItem layout2)
            return;

        // If user selected a disabled item, revert
        if (sender == Lang1Combo && layout1.Hkl == layout2.Hkl)
        {
            // Find previous selection from removed items
            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is KeyboardLayoutItem prev)
            {
                SelectLayout(Lang1Combo, prev.Hkl);
                return;
            }
        }
        if (sender == Lang2Combo && layout2.Hkl == layout1.Hkl)
        {
            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is KeyboardLayoutItem prev)
            {
                SelectLayout(Lang2Combo, prev.Hkl);
                return;
            }
        }

        UpdateDisabledItems();
        SaveSettings();
    }

    private void SaveSettings()
    {
        if (Lang1Combo.SelectedItem is KeyboardLayoutItem layout1)
            Settings.Layout1 = layout1.Hkl;
        if (Lang2Combo.SelectedItem is KeyboardLayoutItem layout2)
            Settings.Layout2 = layout2.Hkl;
        Settings.Save();
    }

    private void OverlayCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        Settings.ShowOverlay = OverlayCheckbox.IsChecked ?? true;
        Settings.Save();
    }

    private void AccentCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        Settings.AccentHoldEnabled = AccentCheckbox.IsChecked ?? true;
        Settings.Save();
    }

    private void ModeRadio_Changed(object sender, RoutedEventArgs e)
    {
        // Guard against being called during XAML initialization
        if (_isLoading || TwoFavoritesPanel == null || CycleAllPanel == null) return;

        bool cycleAll = CycleAllRadio.IsChecked ?? false;
        Settings.CycleAllLayouts = cycleAll;

        TwoFavoritesPanel.Visibility = cycleAll ? Visibility.Collapsed : Visibility.Visible;
        CycleAllPanel.Visibility = cycleAll ? Visibility.Visible : Visibility.Collapsed;

        Settings.Save();
    }

    private void StartupCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SetStartupEnabled(StartupCheckbox.IsChecked ?? true);
    }

    private static void SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;

            if (enabled)
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath))
                {
                    // Add --minimized flag so it starts in background
                    key.SetValue("Capstan", $"\"{exePath}\" --minimized");
                }
            }
            else
            {
                key.DeleteValue("Capstan", throwOnMissingValue: false);
            }
        }
        catch { }
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return key?.GetValue("Capstan") != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFirstRun()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Capstan");
            return key == null;
        }
        catch
        {
            return true;
        }
    }

    private static void MarkFirstRunComplete()
    {
        try
        {
            Registry.CurrentUser.CreateSubKey(@"Software\Capstan");
        }
        catch { }
    }

    private void OpenLanguageSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:regionlanguage",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}

/// <summary>
/// Represents an installed keyboard layout
/// </summary>
public class KeyboardLayoutItem : System.ComponentModel.INotifyPropertyChanged
{
    public long Hkl { get; }
    public string DisplayName { get; }
    public string CountryCode { get; }

    private bool _isDisabled;
    public bool IsDisabled
    {
        get => _isDisabled;
        set
        {
            if (_isDisabled != value)
            {
                _isDisabled = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsDisabled)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ItemOpacity)));
            }
        }
    }

    public double ItemOpacity => IsDisabled ? 0.4 : 1.0;

    /// <summary>
    /// Country code for display (e.g., "US", "GB")
    /// </summary>
    public string CountryDisplay => string.IsNullOrEmpty(CountryCode) ? "" : CountryCode.ToUpperInvariant();

    /// <summary>
    /// Whether to show the country indicator
    /// </summary>
    public bool HasCountry => !string.IsNullOrEmpty(CountryCode);

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public KeyboardLayoutItem(long hkl, string displayName, string countryCode = "")
    {
        Hkl = hkl;
        DisplayName = displayName;
        CountryCode = countryCode;
    }

    public override string ToString() => DisplayName;

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, IntPtr[] lpList);

    public static List<KeyboardLayoutItem> GetInstalledLayouts()
    {
        var result = new List<KeyboardLayoutItem>();

        // Get preload entries (defines order)
        var preload = GetPreload();

        // Get installed keyboard layouts
        int count = GetKeyboardLayoutList(0, null!);
        if (count == 0) return result;

        var hklArray = new IntPtr[count];
        GetKeyboardLayoutList(count, hklArray);

        // Build layout info with proper names
        var layoutInfos = new List<(long hkl, int preloadIndex, int langId, string layoutName)>();

        foreach (var hkl in hklArray)
        {
            long hklValue = hkl.ToInt64();
            int langId = (int)(hklValue & 0xFFFF);

            // Get layout name using Windows algorithm
            string layoutName = GetLayoutName(hkl);

            // Find preload index for ordering
            int preloadIndex = FindPreloadIndex(hklValue, langId, preload);

            layoutInfos.Add((hklValue, preloadIndex, langId, layoutName));
        }

        // Sort by language (using first preload index per language), then by preload index within language
        var langFirstIndex = layoutInfos
            .GroupBy(l => l.langId)
            .ToDictionary(g => g.Key, g => g.Min(l => l.preloadIndex));

        layoutInfos = layoutInfos
            .OrderBy(l => langFirstIndex[l.langId])  // Group languages together
            .ThenBy(l => l.preloadIndex)              // Then order within language
            .ToList();

        // Group by language to see which need layout disambiguation
        var langGroups = layoutInfos.GroupBy(l => l.langId).ToDictionary(g => g.Key, g => g.Count());

        // Get Windows language autonyms
        var autonyms = GetLanguageAutonyms();

        foreach (var info in layoutInfos)
        {
            string displayName;
            bool multipleLayoutsForLang = langGroups[info.langId] > 1;

            // Get the appropriate language display name from Windows
            string langDisplay = GetLanguageDisplayName(info.langId, autonyms);

            if (multipleLayoutsForLang)
            {
                displayName = $"{langDisplay} - {info.layoutName}";
            }
            else
            {
                displayName = langDisplay;
            }

            result.Add(new KeyboardLayoutItem(info.hkl, displayName, GetRegionCode(info.langId)));
        }

        return result;
    }

    /// <summary>
    /// Gets the region/country code for a language ID
    /// </summary>
    private static string GetRegionCode(int langId)
    {
        try
        {
            var culture = new CultureInfo(langId);

            // Try to get region from the culture's region info
            if (!culture.IsNeutralCulture)
            {
                var region = new RegionInfo(culture.Name);
                return region.TwoLetterISORegionName.ToLowerInvariant();
            }
        }
        catch { }

        // For neutral cultures or if region lookup fails, use language-to-country mapping
        try
        {
            var culture = new CultureInfo(langId);
            string langCode = culture.TwoLetterISOLanguageName.ToLowerInvariant();

            return langCode switch
            {
                "en" => "us",  // English → United States
                "es" => "es",  // Spanish → Spain
                "fr" => "fr",  // French → France
                "de" => "de",  // German → Germany
                "it" => "it",  // Italian → Italy
                "pt" => "pt",  // Portuguese → Portugal
                "ru" => "ru",  // Russian → Russia
                "zh" => "cn",  // Chinese → China
                "ja" => "jp",  // Japanese → Japan
                "ko" => "kr",  // Korean → South Korea
                "ar" => "sa",  // Arabic → Saudi Arabia
                "he" => "il",  // Hebrew → Israel
                "hi" => "in",  // Hindi → India
                "th" => "th",  // Thai → Thailand
                "vi" => "vn",  // Vietnamese → Vietnam
                "el" => "gr",  // Greek → Greece
                "tr" => "tr",  // Turkish → Turkey
                "pl" => "pl",  // Polish → Poland
                "nl" => "nl",  // Dutch → Netherlands
                "sv" => "se",  // Swedish → Sweden
                "da" => "dk",  // Danish → Denmark
                "no" => "no",  // Norwegian → Norway
                "fi" => "fi",  // Finnish → Finland
                "cs" => "cz",  // Czech → Czechia
                "sk" => "sk",  // Slovak → Slovakia
                "hu" => "hu",  // Hungarian → Hungary
                "ro" => "ro",  // Romanian → Romania
                "bg" => "bg",  // Bulgarian → Bulgaria
                "uk" => "ua",  // Ukrainian → Ukraine
                "hr" => "hr",  // Croatian → Croatia
                "sr" => "rs",  // Serbian → Serbia
                "sl" => "si",  // Slovenian → Slovenia
                "et" => "ee",  // Estonian → Estonia
                "lv" => "lv",  // Latvian → Latvia
                "lt" => "lt",  // Lithuanian → Lithuania
                "tl" => "ph",  // Tagalog → Philippines
                "fil" => "ph", // Filipino → Philippines
                "ms" => "my",  // Malay → Malaysia
                "id" => "id",  // Indonesian → Indonesia
                "bn" => "bd",  // Bengali → Bangladesh
                "ta" => "in",  // Tamil → India
                "te" => "in",  // Telugu → India
                "mr" => "in",  // Marathi → India
                "gu" => "in",  // Gujarati → India
                "kn" => "in",  // Kannada → India
                "ml" => "in",  // Malayalam → India
                "pa" => "in",  // Punjabi → India
                "ur" => "pk",  // Urdu → Pakistan
                "fa" => "ir",  // Persian/Farsi → Iran
                "sw" => "ke",  // Swahili → Kenya
                "am" => "et",  // Amharic → Ethiopia
                "ne" => "np",  // Nepali → Nepal
                "si" => "lk",  // Sinhala → Sri Lanka
                "km" => "kh",  // Khmer → Cambodia
                "lo" => "la",  // Lao → Laos
                "my" => "mm",  // Burmese → Myanmar
                "ka" => "ge",  // Georgian → Georgia
                "hy" => "am",  // Armenian → Armenia
                "az" => "az",  // Azerbaijani → Azerbaijan
                "kk" => "kz",  // Kazakh → Kazakhstan
                "uz" => "uz",  // Uzbek → Uzbekistan
                "mn" => "mn",  // Mongolian → Mongolia
                "is" => "is",  // Icelandic → Iceland
                "ga" => "ie",  // Irish → Ireland
                "cy" => "gb",  // Welsh → United Kingdom
                "mt" => "mt",  // Maltese → Malta
                "eu" => "es",  // Basque → Spain
                "ca" => "es",  // Catalan → Spain
                "gl" => "es",  // Galician → Spain
                "af" => "za",  // Afrikaans → South Africa
                "sq" => "al",  // Albanian → Albania
                "mk" => "mk",  // Macedonian → North Macedonia
                "bs" => "ba",  // Bosnian → Bosnia
                "lb" => "lu",  // Luxembourgish → Luxembourg
                "fo" => "fo",  // Faroese → Faroe Islands
                "be" => "by",  // Belarusian → Belarus
                _ => ""
            };
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Gets the layout name for an HKL using the Windows algorithm
    /// </summary>
    private static string GetLayoutName(IntPtr hkl)
    {
        long hklValue = hkl.ToInt64();
        int language = (int)(hklValue & 0xFFFF);
        int highWord = (int)((hklValue >> 16) & 0xFFFF);

        // Case 1: Default keyboard for language (highWord == language or highWord == 0)
        if (highWord == language || highWord == 0)
        {
            string keyName = language.ToString("X8");
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\{keyName}");
            return key?.GetValue("Layout Text") as string ?? "Unknown";
        }

        // Case 2: High word is another language ID (e.g., 0x0809 for UK keyboard on US language)
        // These don't have the F prefix in the high nibble
        if ((highWord & 0xF000) != 0xF000)
        {
            string keyName = highWord.ToString("X8");
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\{keyName}");
            if (key != null)
            {
                string? layoutName = key.GetValue("Layout Text") as string;
                if (layoutName != null) return layoutName;
            }
        }

        // Case 3: Layout Id lookup (high word has F prefix, e.g., F020 for Layout Id 0020)
        int layoutId = highWord & 0x0FFF;

        using var layouts = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts");
        if (layouts == null) return "Unknown";

        foreach (string encoding in layouts.GetSubKeyNames())
        {
            if (encoding.Length != 8) continue;

            using var key = layouts.OpenSubKey(encoding);
            if (key == null) continue;

            string? layoutIdStr = key.GetValue("Layout Id") as string;
            if (layoutIdStr == null) continue;

            // Compare Layout Id (may be stored as "0020" or "20")
            if (int.TryParse(layoutIdStr, NumberStyles.HexNumber, null, out int regLayoutId) &&
                regLayoutId == layoutId)
            {
                string? layoutName = key.GetValue("Layout Text") as string;
                if (layoutName != null) return layoutName;
            }
        }

        return "Unknown";
    }

    private static int FindPreloadIndex(long hklValue, int langId, Dictionary<int, string> preload)
    {
        int hiWord = (int)((hklValue >> 16) & 0xFFFF);

        foreach (var entry in preload)
        {
            string preloadValue = entry.Value.ToUpperInvariant();

            if (preloadValue.StartsWith("D", StringComparison.OrdinalIgnoreCase))
            {
                // Substitute layout
                if (preloadValue.EndsWith(langId.ToString("X4"), StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(preloadValue.Substring(1, 3), NumberStyles.HexNumber, null, out int preloadSubIndex))
                    {
                        int hklSubIndex = hiWord & 0x0FFF;
                        if (hklSubIndex == preloadSubIndex)
                        {
                            return entry.Key;
                        }
                    }
                }
            }
            else
            {
                // Standard layout
                if (int.TryParse(preloadValue.Substring(4, 4), NumberStyles.HexNumber, null, out int preloadLangId))
                {
                    if (preloadLangId == langId && hiWord == langId)
                    {
                        return entry.Key;
                    }
                }
            }
        }

        return 999; // Not found, sort last
    }

    private static Dictionary<int, string> GetPreload()
    {
        var result = new Dictionary<int, string>();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Preload");
            if (key == null) return result;

            foreach (var valueName in key.GetValueNames())
            {
                if (int.TryParse(valueName, out int index))
                {
                    var value = key.GetValue(valueName) as string;
                    if (!string.IsNullOrEmpty(value))
                    {
                        result[index] = value;
                    }
                }
            }
        }
        catch { }

        return result;
    }

    private static int GetBaseLanguageId(int langId)
    {
        // Get primary language ID (strips sublanguage)
        return langId & 0x3FF;
    }

    /// <summary>
    /// Gets the language autonyms from Windows user language list
    /// </summary>
    private static Dictionary<string, string> GetLanguageAutonyms()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Use PowerShell to get the language list with Autonyms
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $list = Get-WinUserLanguageList; foreach ($l in $list) { Write-Host \\\"$($l.LanguageTag)|$($l.Autonym)\\\" }\"",
                RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = line.Trim();
                    int pipeIndex = trimmed.IndexOf('|');
                    if (pipeIndex > 0)
                    {
                        string tag = trimmed.Substring(0, pipeIndex);
                        string autonym = trimmed.Substring(pipeIndex + 1);
                        result[tag] = autonym;
                    }
                }
            }
        }
        catch { }

        return result;
    }

    private static string GetLanguageDisplayName(int langId, Dictionary<string, string> autonyms)
    {
        try
        {
            var culture = new CultureInfo(langId);

            // Try to find autonym from Windows language list
            // First try full name (e.g., "en-US"), then try short name (e.g., "el")
            if (autonyms.TryGetValue(culture.Name, out string? autonym) && !string.IsNullOrEmpty(autonym))
            {
                return autonym;
            }

            // Try the two-letter language code
            if (autonyms.TryGetValue(culture.TwoLetterISOLanguageName, out autonym) && !string.IsNullOrEmpty(autonym))
            {
                return autonym;
            }

            // Fallback: use full native name (includes region)
            return culture.NativeName;
        }
        catch
        {
            return $"Unknown ({langId:X4})";
        }
    }
}