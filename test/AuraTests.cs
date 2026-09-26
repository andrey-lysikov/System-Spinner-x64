//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
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
    [InlineData(null, null, 0.0)]
    [InlineData(50.0, 60.0, 0.0)]    // an ordinary desktop load leaves the colour alone
    [InlineData(75.0, null, 0.0)]    // where the colour starts to move: 95 - 20
    [InlineData(85.0, 10.0, 0.5)]
    [InlineData(10.0, 85.0, 0.5)]    // the busier of the two, whichever it is
    [InlineData(20.0, 99.0, 1.0)]
    public void Оттенок_по_нагрузке_берётся_по_самому_загруженному(double? cpu, double? gpu, double expected) =>
        Assert.Equal(expected, WarnHeat.OfLoad(cpu, 95, gpu, 95), 3);

    [Theory]
    [InlineData("", WarnColorMode.Heat)]                                       // the default
    [InlineData("EnableWarnColor = false\n", WarnColorMode.Off)]               // a file from before the choice
    [InlineData("EnableWarnColor = true\n", WarnColorMode.Heat)]
    [InlineData("EnableWarnColor = false\nWarnColorBy = Load\n", WarnColorMode.Load)]   // the new key wins
    public void Режим_предупреждающего_цвета_читается_и_из_старого_ключа(string lines, WarnColorMode expected) =>
        Assert.Equal(expected, ConfFormat.Read("[Hardware]\n" + lines).Warn.WarnColorBy);

    [Fact]
    public void Нулевой_порог_нагрузки_выключает_оттенок() =>
        Assert.Equal(0.0, WarnHeat.OfLoad(100, 0, 100, 0));

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
    [InlineData("Основной цвет", "●  Постоянный", "Максимальная яркость: 100 %")]
    [InlineData("Couleur principale", "○  Arc-en-ciel", "Luminosité maximale: 100 %")]   // the longest there are
    public void Палитра_стоит_в_подменю_с_равными_полями(string title, string effect, string brightness)
    {
        // Built as the tray builds it. A menu drop-down would keep a check-mark strip on the left
        // and an arrow strip on the right, empty round the palette; a plain one frames it evenly.
        var palette = new Lighting.PaletteControl();

        using var drop = new System.Windows.Forms.ToolStripDropDown
        {
            LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.VerticalStackWithOverflow,
            Padding = new System.Windows.Forms.Padding(8, 4, 8, 4)
        };

        var slider = new Lighting.BrightnessSlider { Value = 100 };
        slider.FitWidth(palette.Width);

        // The captions are the menu's own labels, and none of them may be wider than the palette.
        var host = new Lighting.MenuHost(palette);
        var sliderHost = new Lighting.MenuHost(slider);
        drop.Items.Add(new System.Windows.Forms.ToolStripLabel(title));
        drop.Items.Add(host);
        drop.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        drop.Items.Add(new System.Windows.Forms.ToolStripMenuItem(effect));
        drop.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        drop.Items.Add(new System.Windows.Forms.ToolStripLabel(brightness));
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

    [Fact]
    public void Настройки_Aura_переживают_запись_и_чтение()
    {
        var written = new AppConfig
        {
            Warn = { WarnColorBy = WarnColorMode.Max },
            Spinner = { Style = "Sun & Moon", DimAbove = 6, FullBelow = -3 },
            Aura =
            {
                Enable = true, Color = "Orange", Effect = AuraEffect.Breathing, Speed = 8,
                Brightness = 70, VisibleFrom = 20, WeatherCloud = false, LedsPerChannel = 90
            }
        };

        AppConfig read = ConfFormat.Read(ConfFormat.Write(written));

        Assert.Equal(WarnColorMode.Max, read.Warn.WarnColorBy);
        Assert.Equal("Sun & Moon", read.Spinner.Style);   // "&" in the name survives
        Assert.Equal(6, read.Spinner.DimAbove);
        Assert.Equal(-3, read.Spinner.FullBelow);
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
        SpinnerStyle? sky = SpinnerCatalog.Find("Sun & Moon");

        Assert.NotNull(sky);
        Assert.True(sky!.Drawn);
        Assert.Equal(SkyIcon.FrameCount, sky.FrameCount);
    }

    [Fact]
    public void Кадры_солнца_не_отдельный_спиннер()
    {
        // The Sun frames are the day of Sun & Moon: they stay in the assembly but not in the menu.
        Assert.Null(SpinnerCatalog.Find("Sun"));
        for (int index = 0; index < SkyIcon.FrameCount; index++)
            Assert.NotNull(typeof(SkyIcon).Assembly.GetManifestResourceStream(SpinnerCatalog.ResourceName(SkyIcon.SunFrames, index)));
    }

    [Fact]
    public void Выбранное_солнце_становится_солнцем_и_луной()
    {
        AppConfig read = ConfFormat.Read("[Spinner]\nStyle = Sun\n");

        Assert.Equal("Sun & Moon", read.Spinner.Style);
    }

    [Theory]
    [InlineData(true, (int)SkyWeather.Clear)]
    [InlineData(true, (int)SkyWeather.Rainy)]
    [InlineData(false, (int)SkyWeather.Clear)]
    [InlineData(false, (int)SkyWeather.Cloudy)]
    public void Солнце_и_луна_крутятся_в_цвете(bool sunUp, int weather)
    {
        var state = new SkyIcon.State(sunUp, Dusk: 0, Phase: 5, Blood: false, (SkyWeather)weather);
        List<Bitmap> frames = SkyIcon.Frames(state, 16, tint: null);

        // Day and night move over the same loop: the sun turns, the moon's face turns, the cloud sways.
        Assert.Equal(SkyIcon.FrameCount, frames.Count);
        Assert.All(frames, f => Assert.Equal(16, f.Width));
        frames.ForEach(f => f.Dispose());
    }

    [Theory]
    [InlineData((int)SkyWeather.Clear, 1)]         // a plain glyph of the moon has nothing that moves
    [InlineData((int)SkyWeather.Rainy, SkyIcon.FrameCount)]
    public void Силуэт_луны_движется_только_под_тучей(int weather, int count)
    {
        var state = new SkyIcon.State(SunUp: false, Dusk: 0, Phase: 8, Blood: false, (SkyWeather)weather);
        List<Bitmap> frames = SkyIcon.Frames(state, 16, Color.White);

        Assert.Equal(count, frames.Count);
        frames.ForEach(f => f.Dispose());
    }

    [Fact]
    public void Солнце_краснеет_к_горизонту_от_порога_подсветки()
    {
        DateTime utc = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(SkyIcon.DuskSteps, SkyIcon.At(utc, elevation: 0.01, dimAbove: 10, SkyWeather.Clear).Dusk);
        Assert.Equal(0, SkyIcon.At(utc, elevation: 10, dimAbove: 10, SkyWeather.Clear).Dusk);
        // The haze follows DimAbove, where the lighting starts to come up.
        Assert.True(SkyIcon.At(utc, elevation: 10, dimAbove: 20, SkyWeather.Clear).Dusk > 0);
        Assert.Equal(0, SkyIcon.At(utc, elevation: -5, dimAbove: 10, SkyWeather.Clear).Dusk);
    }

    [Theory]
    [InlineData(0, (int)SkyWeather.Clear)]
    [InlineData(1, (int)SkyWeather.Clear)]
    [InlineData(2, (int)SkyWeather.Cloudy)]
    [InlineData(3, (int)SkyWeather.Cloudy)]
    [InlineData(45, (int)SkyWeather.Cloudy)]
    [InlineData(61, (int)SkyWeather.Rainy)]
    [InlineData(95, (int)SkyWeather.Rainy)]
    public void Коды_погоды_делятся_на_ясно_облачно_и_дождь(int code, int weather) =>
        Assert.Equal((SkyWeather)weather, OpenMeteo.FromCode(code));

    [Theory]
    [InlineData(1, 0.3, (int)SkyWeather.Clear)]
    [InlineData(1, 0.6, (int)SkyWeather.Cloudy)]     // "mainly clear" under half the sky of cloud
    [InlineData(0, 0.9, (int)SkyWeather.Cloudy)]
    [InlineData(61, 0.2, (int)SkyWeather.Rainy)]     // rain stays rain, whatever the cover
    public void Облачность_дорисовывает_тучку_когда_код_ясный(int code, double cover, int weather) =>
        Assert.Equal((SkyWeather)weather, OpenMeteo.Classify(code, cover));

    [Fact]
    public void Один_ответ_погоды_несёт_облачность_и_код()
    {
        WeatherReport? report = OpenMeteo.Parse(
            """{"current":{"time":"2026-09-24T09:00","interval":900,"cloud_cover":75,"weather_code":61}}""");

        Assert.NotNull(report);
        Assert.Equal(0.75, report!.CloudCover, 3);
        Assert.Equal(SkyWeather.Rainy, report.Weather);

        Assert.Null(OpenMeteo.Parse("""{"error":true,"reason":"bad"}"""));
        Assert.Equal("https://api.open-meteo.com/v1/forecast?latitude=55.76&longitude=37.62&current=cloud_cover,weather_code",
                     OpenMeteo.Url(new Location(55.7558, 37.6176, "test", ByIp: true)));
    }

    [Theory]
    [InlineData(0.30, 0.0, (int)SkyWeather.Clear)]
    [InlineData(0.694, 8e-7, (int)SkyWeather.Cloudy)]      // a trace of drizzle is not rain
    [InlineData(0.20, 5.6e-5, (int)SkyWeather.Rainy)]     // 0.2 mm an hour
    public void ProjectEOL_делится_на_ясно_облачно_и_дождь(double cover, double flux, int weather) =>
        Assert.Equal((SkyWeather)weather, ProjectEol.Classify(cover, flux));

    [Fact]
    public void Ответ_ProjectEOL_читается()
    {
        // The answer of weatherapi.projecteol.ru for Moscow, as it came, trimmed to what is read.
        WeatherReport? report = ProjectEol.Parse("""
            {"jsonrpc": "2.0", "id": 1, "result": {"content": [{"type": "text", "text": "…"}],
             "structuredContent": {"provider": "noaa-gfs", "latitude": 55.76, "longitude": 37.62,
              "forecast": [{"time": "2026-09-24T12:00:00Z", "values": {
                "surface.cloud_area_fraction": {"value": 0.694, "unit": "1"},
                "surface.precipitation_flux": {"value": 8.000000000039226e-07, "unit": "kg m-2 s-1"}}}]}}}
            """);

        Assert.NotNull(report);
        Assert.Equal(0.694, report!.CloudCover, 3);
        Assert.Equal(SkyWeather.Cloudy, report.Weather);

        Assert.Null(ProjectEol.Parse("""{"jsonrpc": "2.0", "id": 1, "result": {"isError": true, "content": []}}"""));
        Assert.Null(ProjectEol.Parse("""{"jsonrpc": "2.0", "id": 1, "error": {"code": -32602, "message": "bad"}}"""));
    }

    [Fact]
    public void Запрос_к_ProjectEOL_на_текущий_час()
    {
        string request = ProjectEol.Request(new Location(55.7558, 37.6176, "test", ByIp: true),
                                            new DateTime(2026, 9, 24, 12, 41, 5, DateTimeKind.Utc));

        Assert.Contains("\"method\":\"tools/call\"", request);
        Assert.Contains("\"name\":\"get_weather_forecast\"", request);
        Assert.Contains("\"latitude\":55.76", request);
        Assert.Contains("\"start\":\"2026-09-24T12:00:00Z\"", request);
        Assert.Contains("surface.precipitation_flux", request);
    }
}
