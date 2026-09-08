//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

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

// Colours of the tray menu, matched to the Windows theme.
internal sealed class MenuColors : ProfessionalColorTable
{
    private readonly bool _dark;

    public MenuColors(bool dark)
    {
        _dark = dark;

        // A flat menu, no gradients: that is how Windows 11 draws it, and an Office 2007 gradient
        // would give away a foreign program at a glance.
        UseSystemColors = false;
    }

    private Color Surface => _dark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF9, 0xF9, 0xF9);
    private Color Hover => _dark ? Color.FromArgb(0x3D, 0x3D, 0x3D) : Color.FromArgb(0xE9, 0xE9, 0xE9);
    private Color Edge => _dark ? Color.FromArgb(0x45, 0x45, 0x45) : Color.FromArgb(0xE0, 0xE0, 0xE0);

    public override Color ToolStripDropDownBackground => Surface;

    // The strip on the left where the check marks and the animation previews sit.
    public override Color ImageMarginGradientBegin => Surface;
    public override Color ImageMarginGradientMiddle => Surface;
    public override Color ImageMarginGradientEnd => Surface;

    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color MenuItemPressedGradientBegin => Surface;
    public override Color MenuItemPressedGradientMiddle => Surface;
    public override Color MenuItemPressedGradientEnd => Surface;

    public override Color MenuItemBorder => Hover;
    public override Color MenuBorder => Edge;

    public override Color SeparatorDark => Edge;
    public override Color SeparatorLight => Edge;

    // Backdrop of a check mark. No reason to differ from hover: the mark is visible anyway.
    public override Color CheckBackground => Hover;
    public override Color CheckSelectedBackground => Hover;
    public override Color CheckPressedBackground => Hover;
}

// The menu renderer, differing from the standard one in the text and check mark colours: the
// colour table sets neither, and WinForms puts the system black there whatever the theme.
internal sealed class MenuRenderer : ToolStripProfessionalRenderer
{
    private readonly bool _dark;

    public MenuRenderer(bool dark) : base(new MenuColors(dark))
    {
        _dark = dark;
    }

    public Color Foreground => _dark ? Color.FromArgb(0xF0, 0xF0, 0xF0) : Color.FromArgb(0x1A, 0x1A, 0x1A);

    // A disabled item: the same colour at half strength.
    public Color Disabled => _dark ? Color.FromArgb(0x88, 0x88, 0x88) : Color.FromArgb(0x8A, 0x8A, 0x8A);

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Foreground : Disabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // WinForms draws the check mark with a system bitmap — a black one. It is invisible on
        // a dark background, so ours is drawn instead: two lines at an angle, as Windows does.
        if (!_dark)
        {
            base.OnRenderItemCheck(e);
            return;
        }

        Rectangle box = e.ImageRectangle;
        using var pen = new Pen(Foreground, 1.6f);

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(pen, new[]
        {
            new PointF(box.Left + box.Width * 0.28f, box.Top + box.Height * 0.52f),
            new PointF(box.Left + box.Width * 0.44f, box.Top + box.Height * 0.70f),
            new PointF(box.Left + box.Width * 0.74f, box.Top + box.Height * 0.32f)
        });
    }
}

// The tray icon and its menu. The menu repeats the macOS version, item for item.
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
    public event Action? UpdateRequested;
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
            Text = AppParameters.Identity.Name
        };

        // Left button: the status window. Right button: the menu. As in the macOS version.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) StatsRequested?.Invoke();
            else if (e.Button == MouseButtons.Right) ShowMenu();
        };

        _icon.BalloonTipClicked += (_, _) => Follow();
    }

    // A click on a notification that carried a link. The link is dropped afterwards: the next
    // notification may have none, and an old one must not open then.
    private void Follow()
    {
        string? link = _link;
        _link = null;

        if (link is not { Length: > 0 }) return;

        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"{link} did not open", ex);
        }
    }

    // Shows the next animation frame.
    public void ShowFrame(Icon frame) => _icon.Icon = frame;

    // The tooltip under the pointer.
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

    // Fills the submenu with the attached screens. Nothing here is to be picked, so a name only
    // says whether that screen answers over DDC: one that does not is greyed out.
    public void ShowDisplays(IReadOnlyList<(string Name, bool Controllable)> displays)
    {
        // The list is rebuilt whenever the screens are looked at again — hourly at the least, so
        // the old items are let go of rather than left for the collector.
        foreach (ToolStripItem item in _displaysItem.DropDownItems.Cast<ToolStripItem>().ToList())
            item.Dispose();

        _displaysItem.DropDownItems.Clear();

        foreach ((string name, bool controllable) in displays)
            _displaysItem.DropDownItems.Add(new ToolStripMenuItem(name) { Enabled = controllable });

        Repaint(_displaysItem);
    }

    // A notification in the Windows action centre. With a link, a click on it opens that link —
    // there is nowhere else for a tray app to put one.
    public void Notify(string message, string? link = null)
    {
        _link = link;

        _icon.BalloonTipTitle = AppParameters.Identity.Name;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = ToolTipIcon.Info;
        _icon.ShowBalloonTip(NotificationSeconds * 1000);
    }

    private const int NotificationSeconds = 10;

    private string? _link;

    // --- Building the menu. The item order follows the macOS version. ---

    // Subscriptions for the permanent items.
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

    // Builds the menu again. The permanent items get new titles, the submenus are built from
    // scratch: what they hold depends on what the config says.
    private void BuildMenu()
    {
        _autoStartItem.Text = Text.MenuAutoStart;
        _displaysItem.Text = Text.MenuDisplays;
        _alwaysOsdItem.Text = Text.MenuAlwaysCustomOsd;
        _languageItem.Text = Text.MenuSystemLanguage;
        _externalAddressItem.Text = Text.MenuExternalAddress;
        _invertItem.Text = Text.MenuInvertRotation;
        _overlayItem.Text = Text.MenuOverlay;

        // The config and the log open in a text editor rather than whatever the shell picks:
        // .conf and .log may have no association, and a click would offer "choose a program".
        var config = new ToolStripMenuItem(Text.MenuOpenConfig);
        config.Click += (_, _) => Edit(_cfg.Path, AppConfig.PortablePath, AppConfig.UserPath);

        var log = new ToolStripMenuItem(Text.MenuOpenLog);
        log.Click += (_, _) => Edit(Log.Path);

        var update = new ToolStripMenuItem(Text.MenuCheckUpdate);
        update.Click += (_, _) => UpdateRequested?.Invoke();

        var about = new ToolStripMenuItem(Text.MenuAbout);
        about.Click += (_, _) => ShowAbout();

        var exit = new ToolStripMenuItem(Text.MenuExit);
        exit.Click += (_, _) => ExitRequested?.Invoke();

        // Clear() only takes the items out of the menu. The bitmaps and the submenus built last
        // time have to be let go of by hand, or every language change leaves a set behind.
        ReleasePreviews();
        ReleaseBuiltItems();

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
            new ToolStripSeparator(),
            update,
            about,
            exit
        });

        // Empty until the screens are polled.
        ShowDisplays(Array.Empty<(string, bool)>());
    }

    // Opens the menu at the pointer.
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

        // Activation goes to an invisible tool window of ours: something must be in the foreground
        // for the menu to close on a click elsewhere, and a menu doing that gets a taskbar button.
        Win32.SetForegroundWindow(_owner.Handle);

        _menu.Show(pointer, direction);
    }

    // An invisible window the menu can be activated through.
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
                RefreshSpinnerAvailability();
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
            // A set of one frame has only the first; everywhere else the second is taken — the
            // first is often the empty start of a cycle.
            using Stream? stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(
                SpinnerCatalog.ResourceName(style, style.FrameCount > 1 ? 1 : 0));

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

        RefreshSpinnerAvailability();
        return root;
    }

    // What the chosen set allows: a drawing that lives by its own colours takes no silhouette,
    // which would turn it into a blob, and a set of one frame has no rotation to reverse.
    private void RefreshSpinnerAvailability()
    {
        SpinnerStyle style = SpinnerCatalog.Validate(_cfg.Spinner.Style);

        if (_effectsItem is not null) _effectsItem.Enabled = style.SupportsEffect;
        _invertItem.Enabled = style.FrameCount > 1;
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

    // Rebuilds the menu in another language.
    public void Rebuild()
    {
        BuildMenu();
        ApplyTheme();
        ShowAutoStart(Startup.AutoStart.IsEnabled());
    }

    // The renderer the menu was last painted with. The submenu of screens is filled
    // after the painting and has to be brought to the same colours by itself.
    private MenuRenderer? _renderer;

    // Paints the menu to match the Windows theme.
    public void ApplyTheme()
    {
        var renderer = new MenuRenderer(Theme.AreWindowsDark());
        _renderer = renderer;

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

    // Brings one rebuilt submenu to the colours the menu already carries.
    private void Repaint(ToolStripMenuItem item)
    {
        if (_renderer is null) return;

        item.DropDown.RenderMode = ToolStripRenderMode.Professional;
        item.DropDown.Renderer = _renderer;
        Paint(item.DropDownItems, _renderer);
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

    // The only window with a button in the whole program: what this is and which version.
    private void ShowAbout() => new Views.AboutWindow(AppParameters.Identity.Version).ShowAbout();

    // Opens the first existing file in Notepad.
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
                .GetManifestResourceStream(AppParameters.Identity.IconResource);
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

    // Everything the menu builds anew each time, the first-frame previews among them. The
    // permanent items live in fields and are reused, so they are kept.
    private void ReleaseBuiltItems()
    {
        var permanent = new HashSet<ToolStripItem>
        {
            _overlayItem, _autoStartItem, _languageItem, _externalAddressItem,
            _displaysItem, _alwaysOsdItem, _invertItem
        };

        foreach (ToolStripItem item in _menu.Items.Cast<ToolStripItem>().ToList())
        {
            if (permanent.Contains(item)) continue;

            _menu.Items.Remove(item);
            item.Dispose();
        }
    }

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
