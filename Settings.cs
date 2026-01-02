using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace Capstan;

/// <summary>
/// Settings with registry persistence.
/// </summary>
public static class Settings
{
    private const string RegistryKey = @"Software\Capstan";
    
    public static long Layout1 { get; set; } = 0;
    public static long Layout2 { get; set; } = 0;
    public static bool ShowOverlay { get; set; } = true;
    public static bool HookEnabled { get; set; } = true;
    
    /// <summary>
    /// If true, cycle through all layouts. If false, toggle between two favorites.
    /// </summary>
    public static bool CycleAllLayouts { get; set; } = false;
    
    /// <summary>
    /// If true, enable long-press accent selection.
    /// </summary>
    public static bool AccentHoldEnabled { get; set; } = true;
    
    /// <summary>
    /// Dictionary of installed keyboard layouts by HKL
    /// </summary>
    public static Dictionary<long, KeyboardLayoutItem> InstalledLayouts { get; set; } = new();
    
    /// <summary>
    /// Ordered list of all layouts for cycling
    /// </summary>
    public static List<KeyboardLayoutItem> AllLayoutsOrdered { get; set; } = new();

    public static void Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            if (key == null)
            {
                App.Log("No registry key found, using defaults");
                return;
            }
            
            var layout1Val = key.GetValue("Layout1");
            if (layout1Val != null)
                Layout1 = Convert.ToInt64(layout1Val);
            
            var layout2Val = key.GetValue("Layout2");
            if (layout2Val != null)
                Layout2 = Convert.ToInt64(layout2Val);
            
            var showOverlayVal = key.GetValue("ShowOverlay");
            if (showOverlayVal != null)
                ShowOverlay = Convert.ToInt32(showOverlayVal) != 0;
            
            var cycleAllVal = key.GetValue("CycleAllLayouts");
            if (cycleAllVal != null)
                CycleAllLayouts = Convert.ToInt32(cycleAllVal) != 0;
            
            var accentVal = key.GetValue("AccentHoldEnabled");
            if (accentVal != null)
                AccentHoldEnabled = Convert.ToInt32(accentVal) != 0;
            
            App.Log($"Loaded settings - Layout1={Layout1:X}, Layout2={Layout2:X}, ShowOverlay={ShowOverlay}, CycleAll={CycleAllLayouts}, Accent={AccentHoldEnabled}");
        }
        catch (Exception ex)
        {
            App.Log($"Error loading settings: {ex.Message}");
        }
    }

    public static void Save()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
            if (key == null)
            {
                App.Log("Failed to create registry key");
                return;
            }
            
            key.SetValue("Layout1", Layout1, RegistryValueKind.QWord);
            key.SetValue("Layout2", Layout2, RegistryValueKind.QWord);
            key.SetValue("ShowOverlay", ShowOverlay ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("CycleAllLayouts", CycleAllLayouts ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("AccentHoldEnabled", AccentHoldEnabled ? 1 : 0, RegistryValueKind.DWord);
            
            App.Log($"Saved settings - Layout1={Layout1:X}, Layout2={Layout2:X}, ShowOverlay={ShowOverlay}, CycleAll={CycleAllLayouts}, Accent={AccentHoldEnabled}");
        }
        catch (Exception ex)
        {
            App.Log($"Error saving settings: {ex.Message}");
        }
    }
}
