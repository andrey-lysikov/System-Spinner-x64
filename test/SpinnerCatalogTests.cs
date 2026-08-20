using System;
using System.Linq;
using SystemSpinnerX64.Spinner;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>
/// The animation catalogue is built from the assembly resource names. A table that drifted from
/// what the assembly actually holds would mean an empty tray icon, so there is no table — but
/// the name parsing still has to be checked.
/// </summary>
public class SpinnerCatalogTests
{
    [Fact]
    public void Кадры_группируются_по_имени_набора()
    {
        var styles = SpinnerCatalog.Group(new[]
        {
            "Spinners/Blue Ball/0.png",
            "Spinners/Blue Ball/1.png",
            "Spinners/Blue Ball/2.png",
            "Spinners/Cat/0.png",
            "SystemSpinnerX64.icon.ico"   // an unrelated resource must not become a set
        });

        Assert.Equal(2, styles.Count);
        Assert.Equal(3, styles.Single(s => s.Name == "Blue Ball").FrameCount);
        Assert.Equal(1, styles.Single(s => s.Name == "Cat").FrameCount);
    }

    [Fact]
    public void Число_кадров_считается_по_наибольшему_номеру()
    {
        // A gap in the middle cuts the animation short rather than shifting it: frames run from
        // zero upwards, and "there is 0, 1 and 3" means going past the second is not allowed.
        var styles = SpinnerCatalog.Group(new[]
        {
            "Spinners/Loader/0.png",
            "Spinners/Loader/1.png",
            "Spinners/Loader/3.png"
        });

        Assert.Equal(4, styles.Single().FrameCount);
    }

    [Theory]
    [InlineData("Spinners/Loader/no number.png")]
    [InlineData("Spinners/Loader/1.jpg")]
    [InlineData("Loader/1.png")]
    public void Посторонние_имена_пропускаются(string resource) =>
        Assert.Empty(SpinnerCatalog.Group(new[] { resource }));

    [Fact]
    public void Наборы_из_сборки_не_пусты()
    {
        // The frames sit in the exe as resources: if the build loses them, the tray icon stays
        // a still picture, and there is no other way to notice.
        Assert.NotEmpty(SpinnerCatalog.All);
        Assert.All(SpinnerCatalog.All, style => Assert.True(style.FrameCount > 0));
    }

    [Fact]
    public void Неизвестное_имя_сводится_к_запасному_набору() =>
        Assert.Equal(SpinnerCatalog.Fallback.Name, SpinnerCatalog.Validate("нет такого").Name);

    [Fact]
    public void Известное_имя_находится_без_учёта_регистра() =>
        Assert.Equal("Blue Ball", SpinnerCatalog.Validate("blue ball").Name);

    [Fact]
    public void Имя_ресурса_собирается_из_набора_и_номера()
    {
        SpinnerStyle style = new("Color Well", 20, true, 1);

        Assert.Equal("Spinners/Color Well/7.png", SpinnerCatalog.ResourceName(style, 7));
    }
}
