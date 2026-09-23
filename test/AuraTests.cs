//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Lighting;
using SystemSpinnerX64.Spinner;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>The lighting carried over from sunlight-flow: the sky, the colours, the controller's
/// config table and the settings that drive it.</summary>
public class AuraTests
{
    [Fact]
    public void Таблица_контроллера_даёт_зоны_платы_и_ARGB()
    {
        // The reply of AULA3-AR32-0304 on the machine this was written on: one addressable header,
        // one LED on the board and two 12 V headers driven along with it.
        byte[] reply = Convert.FromHexString((
            "EC 30 00 00 1E 9F 01 01 00 00 78 3C 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 " +
            "00 01 02 02 01 F4 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 " +
            "00 00 00 00").Replace(" ", ""));
        Array.Resize(ref reply, 65);

        var zones = AuraDevice.ZonesFrom(reply, ledsPerChannel: 60);

        Assert.Equal(2, zones.Count);
        Assert.Equal(new AuraZone("board", 0x00, 0x04, 3), zones[0]);
        Assert.Equal(new AuraZone("ARGB 1", 0x01, 0x00, 60), zones[1]);
    }

    [Fact]
    public void Канал_Aura_не_длиннее_того_что_адресует_протокол()
    {
        // 300 per channel is the config default; an Aura header addresses no more than 120, and
        // the board zone keeps the count its own table gives.
        var reply = new byte[65];
        reply[4 + 0x02] = 1;
        reply[4 + 0x1B] = 1;

        var zones = AuraDevice.ZonesFrom(reply, ledsPerChannel: 300);

        Assert.Equal(1, zones[0].LedCount);
        Assert.Equal(AuraDevice.MaxArgbLeds, zones[1].LedCount);
    }

    [Fact]
    public void Без_светодиодов_ARGB_зона_не_заводится()
    {
        var reply = new byte[65];
        reply[4 + 0x02] = 2;    // two addressable headers
        reply[4 + 0x1B] = 1;    // one LED on the board

        var zones = AuraDevice.ZonesFrom(reply, ledsPerChannel: 0);

        Assert.Single(zones);
        Assert.Equal("board", zones[0].Name);
    }

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData(60.0, 0.0)]     // well below 85 - 15
    [InlineData(70.0, 0.0)]     // where the colour starts to move
    [InlineData(77.5, 0.5)]
    [InlineData(85.0, 1.0)]     // at the threshold it has gone all the way
    [InlineData(99.0, 1.0)]
    public void Оттенок_растёт_к_порогу_температуры(double? cpu, double expected) =>
        Assert.Equal(expected, WarnHeat.Of(cpu, 85, null, 83), 3);

    [Fact]
    public void Оттенок_берётся_по_самому_горячему() =>
        Assert.Equal(1.0, WarnHeat.Of(50, 85, 90, 83), 3);

    [Fact]
    public void Нулевой_порог_выключает_оттенок() =>
        Assert.Equal(0.0, WarnHeat.Of(120, 0, 120, 0));

    [Theory]
    [InlineData(0x0078FF, 0xFF3000)]   // blue       -> red
    [InlineData(0x00FF00, 0xFF8000)]   // green      -> orange
    [InlineData(0xFF0000, 0xFFD000)]   // red        -> yellow
    [InlineData(0x808080, 0xFF3000)]   // grey       -> red
    public void Предупреждающий_цвет_по_палитре_sunlight_flow(uint baseColor, uint warn) =>
        Assert.Equal(Rgb.FromHex(warn), ColorMath.WarnColorFor(Rgb.FromHex(baseColor)));

    [Fact]
    public void Все_светодиоды_получают_один_цвет_эффекта()
    {
        var solid = new AuraLook(AuraEffect.Solid, Rgb.FromHex(0x0078FF), 5);
        var breathing = new AuraLook(AuraEffect.Breathing, Rgb.FromHex(0xFFFFFF), 5);

        Assert.Equal(Rgb.FromHex(0x0078FF), Effects.Render(solid, 0.37));
        Assert.Equal(Rgb.Black, Effects.Render(breathing, 0.0));          // breathing starts dark
        Assert.Equal(Rgb.FromHex(0xFFFFFF), Effects.Render(breathing, 0.5));
        Assert.Equal(Rgb.FromHex(0xFF0000), Effects.Render(new AuraLook(AuraEffect.Rainbow, Rgb.Black, 5), 0));
    }

    [Fact]
    public void Палитра_двенадцать_на_двенадцать_без_повторов()
    {
        var seen = new System.Collections.Generic.HashSet<Rgb>();
        for (int row = 0; row < Lighting.Palette.Size; row++)
            for (int column = 0; column < Lighting.Palette.Size; column++)
                seen.Add(Lighting.Palette.At(row, column));

        Assert.Equal(144, seen.Count);
    }

    [Fact]
    public void Палитра_от_белого_к_чёрному_и_чистые_тона_посередине()
    {
        Assert.Equal(Rgb.FromHex(0xFFFFFF), Lighting.Palette.At(0, 0));
        Assert.Equal(Rgb.FromHex(0x000000), Lighting.Palette.At(0, 11));

        // Row 6 is the full colour: red in the first column, blue in the ninth.
        Assert.Equal(Rgb.FromHex(0xFF0000), Lighting.Palette.At(6, 0));
        Assert.Equal(Rgb.FromHex(0x0000FF), Lighting.Palette.At(6, 8));
    }

    [Theory]
    [InlineData("Основной цвет", "●  Постоянный")]
    [InlineData("Couleur principale", "○  Arc-en-ciel")]     // the longest caption and label there are
    public void Палитра_стоит_в_подменю_с_равными_полями(string title, string effect)
    {
        // Built as the tray builds it. A menu drop-down would keep a check-mark strip on the left
        // and an arrow strip on the right, empty round the palette; a plain one frames it evenly.
        var palette = new Lighting.PaletteControl { Title = title };

        using var drop = new System.Windows.Forms.ToolStripDropDown
        {
            LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.VerticalStackWithOverflow,
            Padding = new System.Windows.Forms.Padding(8, 4, 8, 4)
        };

        var slider = new Lighting.BrightnessSlider { Title = "Luminosité maximale", Value = 80 };
        slider.FitWidth(palette.Width);

        var host = new Lighting.MenuHost(palette);
        var sliderHost = new Lighting.MenuHost(slider);
        drop.Items.Add(host);
        drop.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        drop.Items.Add(new System.Windows.Forms.ToolStripMenuItem(effect));
        drop.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        drop.Items.Add(sliderHost);

        drop.PerformLayout();
        int width = drop.Width;
        drop.PerformLayout();   // a second pass must change nothing: no creeping growth

        Assert.Equal(width, drop.Width);
        Assert.Equal(host.Bounds.X, drop.Width - host.Bounds.Right);
        Assert.Equal(palette.Size, host.Size);

        // The slider lines up under the palette, edge to edge.
        Assert.Equal(host.Bounds.X, sliderHost.Bounds.X);
        Assert.Equal(host.Bounds.Width, sliderHost.Bounds.Width);
    }

    [Theory]
    [InlineData(0, 5)]        // never down to nothing: the slider starts at five
    [InlineData(47, 45)]
    [InlineData(48, 50)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    public void Ползунок_яркости_ходит_шагами_по_пять(double value, int expected) =>
        Assert.Equal(expected, Lighting.BrightnessSlider.Snap(value));

    [Fact]
    public void Цвет_из_палитры_переживает_запись_в_файл()
    {
        // The menu writes the swatch as #RRGGBB; the engine must read back the very same colour.
        Rgb swatch = Lighting.Palette.At(3, 5);

        Assert.True(Rgb.TryParse(swatch.ToString(), out Rgb read));
        Assert.Equal(swatch, read);
    }

    [Theory]
    [InlineData("#FF8000", 0xFF8000)]
    [InlineData("Orange", 0xFFA500)]
    public void Цвет_читается_и_как_код_и_как_имя(string text, uint expected)
    {
        Assert.True(Rgb.TryParse(text, out Rgb color));
        Assert.Equal(Rgb.FromHex(expected), color);
    }

    [Fact]
    public void Не_цвет_не_принимается() => Assert.False(Rgb.TryParse("не цвет", out _));

    [Fact]
    public void Солнце_в_полдень_высоко_а_в_полночь_под_горизонтом()
    {
        // Moscow at the equinox: noon is about 11:30 local, midnight well below the horizon.
        double noon = Sun.Elevation(new DateTime(2026, 3, 20, 9, 30, 0, DateTimeKind.Utc), 55.76, 37.62);
        double night = Sun.Elevation(new DateTime(2026, 3, 20, 21, 30, 0, DateTimeKind.Utc), 55.76, 37.62);

        Assert.InRange(noon, 30, 40);
        Assert.True(night < -20);
    }

    [Fact]
    public void Фаза_луны_полнолуние_и_новолуние()
    {
        // 2026-03-03 is a full moon with a total lunar eclipse; 2026-03-19 is a new moon.
        Assert.InRange(Moon.Illumination(Moon.Phase(new DateTime(2026, 3, 3, 11, 0, 0, DateTimeKind.Utc))), 0.97, 1.0);
        Assert.InRange(Moon.Illumination(Moon.Phase(new DateTime(2026, 3, 19, 1, 0, 0, DateTimeKind.Utc))), 0.0, 0.03);
    }

    [Theory]
    [InlineData("+03:00", 3)]
    [InlineData("UTC-5", -5)]
    [InlineData("5:30", 5.5)]
    public void Смещение_пояса_разбирается(string text, double hours)
    {
        Assert.True(Geo.TryParseOffset(text, out TimeSpan offset));
        Assert.Equal(hours, offset.TotalHours);
    }

    [Fact]
    public void Настройки_Aura_переживают_запись_и_чтение()
    {
        var written = new AppConfig
        {
            Warn = { EnableWarnColor = false },
            Spinner = { Style = SpinnerCatalog.SunAndMoon, DimAbove = 6, FullBelow = -3, TimeZone = "+03:00" },
            Aura =
            {
                Enable = true, Color = "Orange", Effect = AuraEffect.Breathing, Speed = 8,
                Brightness = 70, VisibleFrom = 20, WeatherCloud = false, LedsPerChannel = 90
            }
        };

        AppConfig read = ConfFormat.Read(ConfFormat.Write(written));

        Assert.False(read.Warn.EnableWarnColor);
        Assert.Equal(SpinnerCatalog.SunAndMoon, read.Spinner.Style);   // "&" in the name survives
        Assert.Equal(6, read.Spinner.DimAbove);
        Assert.Equal(-3, read.Spinner.FullBelow);
        Assert.Equal("+03:00", read.Spinner.TimeZone);
        Assert.True(read.Aura.Enable);
        Assert.Equal("Orange", read.Aura.Color);
        Assert.Equal(AuraEffect.Breathing, read.Aura.Effect);
        Assert.Equal(8, read.Aura.Speed);
        Assert.Equal(70, read.Aura.Brightness);
        Assert.Equal(20, read.Aura.VisibleFrom);
        Assert.False(read.Aura.WeatherCloud);
        Assert.Equal(90, read.Aura.LedsPerChannel);
    }

    [Fact]
    public void Нелепые_значения_приводятся_в_рамки()
    {
        AppConfig cfg = ConfFormat.Read(
            "[Spinner]\nDimAbove = 0\nFullBelow = 5\n[Aura]\nSpeed = 40\nLedsPerChannel = 5000\n");

        Assert.Equal(10, cfg.Aura.Speed);
        Assert.Equal(1000, cfg.Aura.LedsPerChannel);
        Assert.Equal(10, cfg.Spinner.DimAbove);     // a band the wrong way round falls back to the defaults
        Assert.Equal(-6, cfg.Spinner.FullBelow);
    }

    [Fact]
    public void Пороги_солнца_в_Aura_больше_не_читаются()
    {
        // They live under [Spinner] now: the sun of the spinner and of the lighting is one.
        AppConfig cfg = ConfFormat.Read("[Aura]\nDimAbove = 3\n");

        Assert.Equal(10, cfg.Spinner.DimAbove);
    }

    [Theory]
    [InlineData("[Aura]\nEffect = Wave\n")]    // the per-LED effects are gone on purpose
    [InlineData("[Aura]\nEnable = ага\n")]
    public void Ошибка_в_секции_Aura_не_проходит_молча(string text) =>
        Assert.ThrowsAny<FormatException>(() => ConfFormat.Read(text));

    [Fact]
    public void Солнце_и_луна_есть_среди_спиннеров()
    {
        SpinnerStyle? sky = SpinnerCatalog.Find(SpinnerCatalog.SunAndMoon);

        Assert.NotNull(sky);
        Assert.True(sky!.Drawn);
        Assert.Equal(1, sky.FrameCount);
    }

    [Fact]
    public void Солнце_и_луна_рисуются_без_ресурсов()
    {
        var animator = new SpinnerAnimator();
        animator.Load(SpinnerCatalog.Validate(SpinnerCatalog.SunAndMoon), SpinnerEffect.Auto, 16, lightTheme: true);

        // One drawn frame, standing still: it is redrawn by the minute, not animated.
        Assert.True(animator.HasFrames);
        Assert.False(animator.IsSpinning);

        animator.Dispose();
    }

    [Theory]
    [InlineData(true, 8, 0)]
    [InlineData(false, 0, 8)]     // full moon
    [InlineData(false, 0, 0)]     // new moon: the outline alone
    public void Иконка_неба_что_то_рисует(bool sunUp, int fill, int phase)
    {
        using Bitmap icon = SkyIcon.Render(new SkyIcon.State(sunUp, fill, phase), 32, Color.White);

        bool drawn = false;
        for (int y = 0; y < icon.Height && !drawn; y++)
            for (int x = 0; x < icon.Width && !drawn; x++)
                drawn = icon.GetPixel(x, y).A > 0;

        Assert.True(drawn);
    }
}
