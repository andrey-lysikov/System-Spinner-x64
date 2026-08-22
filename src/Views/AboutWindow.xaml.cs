using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Platform;

namespace SystemSpinnerX64.Views;

// About: what this is, who wrote it and which version.
public partial class AboutWindow : Window
{
    public AboutWindow(string version)
    {
        InitializeComponent();

        // Arabic reads right to left: the whole window is mirrored rather than each label,
        // or the numbers would end up on the wrong side of their captions.
        FlowDirection = Text.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        // The name of the project with its version on one line, the story underneath.
        Headline.Text = $"System Spinner x64 v{version}";
        Description.Text = Text.AboutText;
        Project.Content = Text.MenuProject;
        Logo.Source = LoadIcon();

        ApplyTheme();

        // A click elsewhere closes the window, like every other window here. Closing itself
        // deactivates it, so without the guard the handler would call Close() on an already
        // closed window — and that throws.
        Deactivated += (_, _) => CloseOnce();
    }

    private bool _closing;

    // The flag is raised here rather than in CloseOnce: Close() raises Closing synchronously,
    // so every way of closing the window — the button, Esc, the system — passes through this
    // and is covered.
    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }

    private void CloseOnce()
    {
        if (_closing) return;
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Dwm.ApplyAcrylic(new WindowInteropHelper(this).Handle, Theme.AreWindowsDark());
    }

    private void ApplyTheme()
    {
        bool dark = Theme.AreWindowsDark();

        Color foreground = dark ? Colors.White : Color.FromRgb(0x11, 0x11, 0x11);
        Color background = dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF7, 0xF7, 0xF7);

        Shell.Background = new SolidColorBrush(background) { Opacity = 0.92 };
        Shell.BorderBrush = new SolidColorBrush(foreground) { Opacity = 0.12 };
        System.Windows.Documents.TextElement.SetForeground(Body, new SolidColorBrush(foreground));

        foreach (System.Windows.Controls.Button button in new[] { Ok, Project })
        {
            button.Foreground = new SolidColorBrush(foreground);
            button.Background = new SolidColorBrush(foreground) { Opacity = 0.10 };
            button.BorderBrush = new SolidColorBrush(foreground) { Opacity = 0.18 };
        }
    }

    // Centres the window on the screen under the pointer.
    public void ShowAbout()
    {
        // Shown off-screen first: the height depends on how the text wraps, and placing it
        // before that is known would let the window flash where Windows opened it.
        Left = AppParameters.Layout.OffScreen;
        Top = AppParameters.Layout.OffScreen;

        Show();
        UpdateLayout();

        ScreenPlacement.PutCentred(this);

        Activate();
    }

    // The same icon as in the tray: it sits in the assembly resources.
    private static ImageSource? LoadIcon()
    {
        try
        {
            using Stream? stream = typeof(AboutWindow).Assembly
                .GetManifestResourceStream(AppParameters.Identity.IconResource);
            if (stream is null) return null;

            var decoder = new IconBitmapDecoder(
                stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            // The largest entry: in the window the icon is four times the size it has in the tray.
            BitmapFrame frame = decoder.Frames[^1];
            frame.Freeze();
            return frame;
        }
        catch (Exception ex)
        {
            Log.Error("the About icon did not load", ex);
            return null;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => CloseOnce();

    // The repository: the source, the releases and the place to report what went wrong.
    private void OnProjectClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(AppParameters.Links.Project) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("the project page did not open", ex);
        }
    }
}
