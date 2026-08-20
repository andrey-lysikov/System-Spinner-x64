using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Diagnostics;
using SystemSpinnerX64.Localization;
using SystemSpinnerX64.Platform;
using SystemSpinnerX64.Spinner;

namespace SystemSpinnerX64.Tray;

/// <summary>
/// The tray icon and its menu.
///
/// The menu repeats the macOS version, item for item. It controls what it controlled there: the
/// animation, the volume and brightness panel, the language and the poll. Everything else — fans,
/// sensor names, the look of the in-game panel, the frame counter — lives in config.conf, as in
/// GameOverlay: there are dozens of those parameters, each with its own note, and a menu of them
/// would be longer than the program.
///
/// Every choice is written to config.conf at once: the settings live in one place rather than
/// two, and after a restart the menu looks the way it was left.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly MenuOwner _owner = new();

    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _displaysItem;
    private readonly ToolStripMenuItem _alwaysOsdItem;
    private readonly ToolStripMenuItem _languageItem;
    private readonly ToolStripMenuItem _externalAddressItem;
    private readonly ToolStripMenuItem _invertItem;
    private readonly ToolStripMenuItem _overlayItem;

    private readonly Icon _fallbackIcon;

    public event Action? StatsRequested;
    public event Action<bool>? AutoStartToggled;
    public event Action? SpinnerChanged;
    public event Action? IntervalChanged;
    public event Action? OsdChanged;
    public event Action? LanguageChanged;
    public event Action? OverlayChanged;
    public event Action? DisplayRefreshRequested;
    public event Action? ExitRequested;

    public TrayIcon(AppConfig cfg)
    {
        _cfg = cfg;
        _fallbackIcon = LoadIcon();

        _autoStartItem = Check(Text.MenuAutoStart, false);
        _alwaysOsdItem = Check(Text.MenuAlwaysCustomOsd, _cfg.Osd.AlwaysUseCustomOsd);
        _languageItem = Check(Text.MenuSystemLanguage, _cfg.Language == Language.Auto);
        _externalAddressItem = Check(Text.MenuExternalAddress, _cfg.Stats.ShowExternalAddress);
        _invertItem = Check(Text.MenuInvertRotation, _cfg.Spinner.InvertRotation);
        _overlayItem = Check(Text.MenuOverlay, _cfg.ShowOverlayInGames);

        _displaysItem = new ToolStripMenuItem(Text.MenuDisplays);

        WireItems();
        BuildMenu();
        ApplyTheme();

        // The menu is not handed to NotifyIcon: it is opened by hand in ShowMenu, which knows
        // where the taskbar is.
        _icon = new NotifyIcon
        {
            Icon = _fallbackIcon,
            Visible = true,
            Text = "System Spinner x64"
        };

        // Left button: the status window. Right button: the menu. As in the macOS version.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) StatsRequested?.Invoke();
            else if (e.Button == MouseButtons.Right) ShowMenu();
        };
    }

    /// <summary>Shows the next animation frame.</summary>
    public void ShowFrame(Icon frame) => _icon.Icon = frame;

    /// <summary>
    /// The tooltip under the pointer. It is also the answer to "how many per cent exactly": the
    /// speed of the animation shows the load at a glance, and the number is here. No second icon
    /// with digits is needed for that.
    /// </summary>
    public void ShowTip(string tip)
    {
        // NotifyIcon.Text longer than 63 characters is silently refused along with the whole icon.
        _icon.Text = tip.Length <= 63 ? tip : tip[..63];
    }

    // Sets the tick without raising the event: the state lives in Task Scheduler, this only mirrors it.
    public void ShowAutoStart(bool enabled)
    {
        if (_autoStartItem.Checked == enabled) return;

        _autoStartItem.CheckedChanged -= OnAutoStartChanged;
        _autoStartItem.Checked = enabled;
        _autoStartItem.CheckedChanged += OnAutoStartChanged;
    }

    /// <summary>Fills the submenu with the list of attached screens.</summary>
    public void ShowDisplays(IReadOnlyList<string> displays)
    {
        _displaysItem.DropDownItems.Clear();

        foreach (string display in displays)
            _displaysItem.DropDownItems.Add(new ToolStripMenuItem(display) { Enabled = false });

        var refresh = new ToolStripMenuItem(Text.MenuRefreshDisplays);
        refresh.Click += (_, _) => DisplayRefreshRequested?.Invoke();

        if (displays.Count > 0) _displaysItem.DropDownItems.Add(new ToolStripSeparator());
        _displaysItem.DropDownItems.Add(refresh);
    }

    public void Notify(string message)
    {
        _icon.BalloonTipTitle = "System Spinner x64";
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(5000);
    }

    // --- Building the menu. The item order follows the macOS version. ---

    /// <summary>
    /// Subscriptions for the permanent items. Separate from building the menu and done exactly
    /// once: the menu is rebuilt on a language change, and the handlers must stay single — after
    /// switching the language every click would otherwise fire twice.
    /// </summary>
    private void WireItems()
    {
        _autoStartItem.CheckedChanged += OnAutoStartChanged;

        _alwaysOsdItem.CheckedChanged += (_, _) =>
        {
            _cfg.Osd.AlwaysUseCustomOsd = _alwaysOsdItem.Checked;
            Save();
            OsdChanged?.Invoke();
        };

        _languageItem.CheckedChanged += (_, _) =>
        {
            // Unticked means English, as in the macOS version: that switch chooses between the
            // system language and English rather than between every language at once.
            _cfg.Language = _languageItem.Checked ? Language.Auto : Language.En;
            Text.Use(_cfg.Language);
            Save();
            LanguageChanged?.Invoke();
        };

        _externalAddressItem.CheckedChanged += (_, _) =>
        {
            _cfg.Stats.ShowExternalAddress = _externalAddressItem.Checked;
            Save();
        };

        _invertItem.CheckedChanged += (_, _) =>
        {
            _cfg.Spinner.InvertRotation = _invertItem.Checked;
            Save();
            SpinnerChanged?.Invoke();
        };

        _overlayItem.CheckedChanged += (_, _) =>
        {
            _cfg.ShowOverlayInGames = _overlayItem.Checked;
            Save();
            OverlayChanged?.Invoke();
        };
    }

    /// <summary>
    /// Builds the menu again. The permanent items get new titles, the submenus are built from
    /// scratch: what they hold depends on what the config says.
    /// </summary>
    private void BuildMenu()
    {
        _autoStartItem.Text = Text.MenuAutoStart;
        _displaysItem.Text = Text.MenuDisplays;
        _alwaysOsdItem.Text = Text.MenuAlwaysCustomOsd;
        _languageItem.Text = Text.MenuSystemLanguage;
        _externalAddressItem.Text = Text.MenuExternalAddress;
        _invertItem.Text = Text.MenuInvertRotation;
        _overlayItem.Text = Text.MenuOverlay;

        // The config and the log open in a text editor rather than in whatever the shell picks:
        // .conf and .log may have no association at all, and a click would then open the
        // "choose a program" dialog instead of the file.
        var config = new ToolStripMenuItem(Text.MenuOpenConfig);
        config.Click += (_, _) => Edit(_cfg.Path, AppConfig.PortablePath, AppConfig.UserPath);

        var log = new ToolStripMenuItem(Text.MenuOpenLog);
        log.Click += (_, _) => Edit(Log.Path);

        var about = new ToolStripMenuItem(Text.MenuAbout);
        about.Click += (_, _) => ShowAbout();

        var exit = new ToolStripMenuItem(Text.MenuExit);
        exit.Click += (_, _) => ExitRequested?.Invoke();

        // The previews are freed here: Clear() takes the items out of the menu, but the bitmaps
        // attached to them stay around until the garbage collector notices.
        ReleasePreviews();

        _menu.Items.Clear();
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _overlayItem,
            _autoStartItem,
            _languageItem,
            _externalAddressItem,
            new ToolStripSeparator(),
            _displaysItem,
            StepsMenu(),
            _alwaysOsdItem,
            new ToolStripSeparator(),
            SpinnersMenu(),
            IntervalsMenu(),
            EffectsMenu(),
            _invertItem,
            new ToolStripSeparator(),
            config,
            log,
            about,
            exit
        });

        ShowDisplays(Array.Empty<string>());
    }

    /// <summary>
    /// Opens the menu at the pointer. <c>NotifyIcon</c> can do this itself, but it places the menu
    /// before the menu has been laid out: on the very first right click one of no size lands in a
    /// corner of the screen. Shown by hand it also opens in the right direction — upwards from a
    /// taskbar at the bottom, downwards from one at the top — instead of running off the screen.
    /// </summary>
    private void ShowMenu()
    {
        System.Drawing.Point pointer = Control.MousePosition;
        System.Drawing.Rectangle work = Screen.FromPoint(pointer).WorkingArea;

        bool above = pointer.Y > work.Top + work.Height / 2;
        bool toTheLeft = pointer.X > work.Left + work.Width / 2;

        ToolStripDropDownDirection direction = (above, toTheLeft) switch
        {
            (true, true) => ToolStripDropDownDirection.AboveLeft,
            (true, false) => ToolStripDropDownDirection.AboveRight,
            (false, true) => ToolStripDropDownDirection.BelowLeft,
            _ => ToolStripDropDownDirection.BelowRight
        };

        // Activation goes to a window of our own first. Something has to be the foreground window
        // or the menu stays open after a click elsewhere — and if that something is the menu
        // itself, Windows puts a button for it on the taskbar. The owner window is a tool window:
        // those get no button.
        Win32.SetForegroundWindow(_owner.Handle);

        _menu.Show(pointer, direction);
    }

    /// <summary>
    /// An invisible window the menu can be activated through. A tray icon has no window of its
    /// own, and a menu needs one behind it: that is what keeps it off the taskbar and lets it
    /// close on a click elsewhere.
    /// </summary>
    private sealed class MenuOwner : NativeWindow, IDisposable
    {
        private const int WsPopup = unchecked((int)0x80000000);
        private const int WsExToolWindow = 0x00000080;

        public MenuOwner()
        {
            CreateHandle(new CreateParams
            {
                Caption = string.Empty,
                X = AppParameters.Layout.OffScreen,
                Y = AppParameters.Layout.OffScreen,
                Width = 0,
                Height = 0,
                Style = WsPopup,
                ExStyle = WsExToolWindow
            });
        }

        public void Dispose() => DestroyHandle();
    }


    private ToolStripMenuItem SpinnersMenu()
    {
        var root = new ToolStripMenuItem(Text.MenuSpinners);

        foreach (SpinnerStyle style in SpinnerCatalog.All)
        {
            var item = new ToolStripMenuItem(style.Name)
            {
                Checked = style.Name.Equals(_cfg.Spinner.Style, StringComparison.OrdinalIgnoreCase),
                Image = Preview(style)
            };

            item.Click += (_, _) =>
            {
                SelectOne(root, item);
                _cfg.Spinner.Style = style.Name;
                Save();
                SpinnerChanged?.Invoke();
                RefreshEffectsAvailability();
            };

            root.DropDownItems.Add(item);
        }

        return root;
    }

    // The first frame of a set next to its name: "Delay" does not tell you what it looks like.
    private static Image? Preview(SpinnerStyle style)
    {
        try
        {
            using Stream? stream = typeof(TrayIcon).Assembly
                .GetManifestResourceStream(SpinnerCatalog.ResourceName(style, 1));
            if (stream is null) return null;

            using var frame = new Bitmap(stream);
            return new Bitmap(frame, new Size(16, 16));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"the preview of \"{style.Name}\" did not load: {ex.Message}");
            return null;
        }
    }

    private ToolStripMenuItem? _effectsItem;

    private ToolStripMenuItem EffectsMenu()
    {
        var root = new ToolStripMenuItem(Text.MenuEffects);
        _effectsItem = root;

        (SpinnerEffect Effect, string Title)[] effects =
        {
            (SpinnerEffect.Original, Text.EffectOriginal),
            (SpinnerEffect.White, Text.EffectWhite),
            (SpinnerEffect.Black, Text.EffectBlack),
            (SpinnerEffect.Auto, Text.EffectAuto)
        };

        foreach ((SpinnerEffect effect, string title) in effects)
        {
            var item = new ToolStripMenuItem(title) { Checked = _cfg.Spinner.Effect == effect };

            item.Click += (_, _) =>
            {
                SelectOne(root, item);
                _cfg.Spinner.Effect = effect;
                Save();
                SpinnerChanged?.Invoke();
            };

            root.DropDownItems.Add(item);
        }

        RefreshEffectsAvailability();
        return root;
    }

    // For sets that live by their own colours a silhouette would turn the drawing into a blob —
    // there the item is simply disabled, as in the macOS version.
    private void RefreshEffectsAvailability()
    {
        if (_effectsItem is null) return;
        _effectsItem.Enabled = SpinnerCatalog.Validate(_cfg.Spinner.Style).SupportsEffect;
    }

    private ToolStripMenuItem IntervalsMenu()
    {
        var root = new ToolStripMenuItem(Text.MenuUpdateInterval);

        foreach (int milliseconds in AppParameters.Menu.Intervals)
        {
            var item = new ToolStripMenuItem(Text.Seconds(milliseconds / 1000.0))
            {
                Checked = _cfg.UpdateIntervalMs == milliseconds
            };

            item.Click += (_, _) =>
            {
                SelectOne(root, item);
                _cfg.UpdateIntervalMs = milliseconds;
                Save();
                IntervalChanged?.Invoke();
            };

            root.DropDownItems.Add(item);
        }

        return root;
    }

    private ToolStripMenuItem StepsMenu()
    {
        var root = new ToolStripMenuItem(Text.MenuAdjustmentSteps);

        foreach (int steps in AppParameters.Menu.Steps)
        {
            var item = new ToolStripMenuItem(steps.ToString(CultureInfo.InvariantCulture))
            {
                Checked = _cfg.Osd.AdjustmentSteps == steps
            };

            item.Click += (_, _) =>
            {
                SelectOne(root, item);
                _cfg.Osd.AdjustmentSteps = steps;
                Save();
                OsdChanged?.Invoke();
            };

            root.DropDownItems.Add(item);
        }

        return root;
    }

    /// <summary>Rebuilds the menu in another language. Simpler and safer than rewriting titles.</summary>
    public void Rebuild()
    {
        BuildMenu();
        ApplyTheme();
        ShowAutoStart(Startup.AutoStart.IsEnabled());
    }

    /// <summary>
    /// Paints the menu to match the Windows theme. Called when the menu is built and when the
    /// theme was switched on the fly: WinForms does not follow the theme, its menu is white at any.
    /// </summary>
    public void ApplyTheme()
    {
        var renderer = new MenuRenderer(Theme.AreWindowsDark());

        // Arabic reads right to left: the menu mirrors along with the windows.
        _menu.RightToLeft = Text.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;

        _menu.RenderMode = ToolStripRenderMode.Professional;
        _menu.Renderer = renderer;

        // Submenus are separate windows with their own renderer: skipping them would leave the
        // list of animations white in the middle of a dark menu.
        Paint(_menu.Items, renderer);
    }

    private static void Paint(ToolStripItemCollection items, MenuRenderer renderer)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = item.Enabled ? renderer.Foreground : renderer.Disabled;

            if (item is not ToolStripMenuItem { HasDropDownItems: true } parent) continue;

            parent.DropDown.RenderMode = ToolStripRenderMode.Professional;
            parent.DropDown.Renderer = renderer;
            Paint(parent.DropDownItems, renderer);
        }
    }

    private static ToolStripMenuItem Check(string title, bool state) =>
        new(title) { CheckOnClick = true, Checked = state };

    private static void SelectOne(ToolStripMenuItem root, ToolStripMenuItem chosen)
    {
        foreach (ToolStripItem item in root.DropDownItems)
            if (item is ToolStripMenuItem entry) entry.Checked = ReferenceEquals(entry, chosen);
    }

    private void OnAutoStartChanged(object? sender, EventArgs e) =>
        AutoStartToggled?.Invoke(_autoStartItem.Checked);

    // A setting chosen in the menu has to survive a restart, or the menu is lying.
    private void Save()
    {
        if (_cfg.SaveSomewhere() is null)
            Log.Warn("the setting was not written to config.conf — it applies until restart");
    }

    /// <summary>The only window with a button in the whole program: what this is and which version.</summary>
    private void ShowAbout() => new Views.AboutWindow(Version()).ShowAbout();

    /// <summary>
    /// The version as three numbers. The assembly always has four — .NET adds the revision itself —
    /// but showing "0.1.0.0" means showing a field nobody here uses.
    /// </summary>
    private static string Version()
    {
        System.Version? version = typeof(TrayIcon).Assembly.GetName().Version;
        return version is null
            ? ""
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>
    /// Opens the first existing file in Notepad. In Notepad specifically rather than through the
    /// shell: .conf and .log may have no association, and a click would open the "choose a program"
    /// dialog instead of the file.
    /// </summary>
    private void Edit(params string?[] candidates)
    {
        foreach (string? path in candidates)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
                return;
            }
            catch (Exception ex)
            {
                Log.Error($"{path} did not open in the editor", ex);
            }
        }

        // Not a single file exists: the log did not start or the config is not written yet. Silence
        // here would look like a broken menu item.
        Notify(Text.FileMissing(candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? "—"));
    }

    // If the resource is missing, the system icon is used: without a tray icon there is no way to quit.
    private static Icon LoadIcon()
    {
        try
        {
            using Stream? stream = typeof(TrayIcon).Assembly
                .GetManifestResourceStream("SystemSpinnerX64.icon.ico");
            if (stream is null) return SystemIcons.Information;

            using var full = new Icon(stream);
            return new Icon(full, SystemInformation.SmallIconSize);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"the icon did not load: {ex.Message}");
            return SystemIcons.Information;
        }
    }

    // The first-frame previews in the animation submenu are the only bitmaps the menu creates itself.
    private void ReleasePreviews()
    {
        foreach (ToolStripMenuItem item in _menu.Items.OfType<ToolStripMenuItem>()
                                                      .SelectMany(i => i.DropDownItems.OfType<ToolStripMenuItem>()))
        {
            Image? preview = item.Image;
            item.Image = null;
            preview?.Dispose();
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();

        ReleasePreviews();
        _menu.Dispose();
        _owner.Dispose();

        // NotifyIcon does not free the icon itself — it is ours.
        if (!ReferenceEquals(_fallbackIcon, SystemIcons.Information)) _fallbackIcon.Dispose();
    }
}
