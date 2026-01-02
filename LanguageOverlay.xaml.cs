using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Capstan;

public partial class LanguageOverlay : Window
{
    private readonly DispatcherTimer _hideTimer;

    public LanguageOverlay()
    {
        try
        {
            InitializeComponent();

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                FadeOut();
            };

            // Start shown but invisible (avoids slow Show() call later)
            Opacity = 0;
            Show();
            
            // Initial position
            UpdatePosition();
            App.Log("LanguageOverlay initialized");
        }
        catch (Exception ex)
        {
            App.Log($"LanguageOverlay init failed: {ex}");
            throw;
        }
    }

    private void UpdatePosition()
    {
        // Position in bottom-right corner
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 20;
        Top = workArea.Bottom - Height - 20;
    }

    public void Flash(string displayName)
    {
        try
        {
            // Stop any animation in progress
            BeginAnimation(OpacityProperty, null);
            
            LanguageText.Text = displayName;

            // Update position after content changes (for auto-width)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdatePosition);

            _hideTimer.Stop();
            Opacity = 1;
            _hideTimer.Start();
        }
        catch (Exception ex)
        {
            App.Log($"LanguageOverlay.Flash failed: {ex.Message}");
        }
    }

    private void FadeOut()
    {
        var animation = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300)
        };

        BeginAnimation(OpacityProperty, animation);
    }
}
