//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using SystemSpinnerX64.Configuration;
using SystemSpinnerX64.Localization;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>Choosing the interface language. The log and the config are always English.</summary>
public class LanguageTests
{
    [Fact]
    public void Явный_язык_переопределяет_системный()
    {
        Language before = Text.Current;
        try
        {
            Text.Use(Language.En);
            Assert.Equal("Quit", Text.MenuExit);

            Text.Use(Language.Ru);
            Assert.Equal("Выход", Text.MenuExit);
        }
        finally { Text.Use(before); }
    }

    [Fact]
    public void Auto_даёт_один_из_двух_языков()
    {
        Language before = Text.Current;
        try
        {
            Text.Use(Language.Auto);

            // Which one depends on the machine the tests run on; what matters is that it is not
            // empty and not a mixture: Auto has to resolve to one specific language.
            Assert.Contains(Text.Current, new[] { Language.Ru, Language.En });
            Assert.Equal(Text.Current == Language.Ru ? "Выход" : "Quit", Text.MenuExit);
        }
        finally { Text.Use(before); }
    }

    [Fact]
    public void Язык_читается_из_конфига()
    {
        Assert.Equal(Language.En, ConfFormat.Read("[General]\nLanguage = En\n").Language);
        Assert.Equal(Language.Ru, ConfFormat.Read("[General]\nLanguage = ru\n").Language);
    }

    [Fact]
    public void Без_параметра_язык_автоматический()
    {
        Assert.Equal(Language.Auto, ConfFormat.Read("[General]\nGpuIndex = 0\n").Language);
    }

    [Fact]
    public void Неизвестный_язык_не_проходит_молча()
    {
        Assert.ThrowsAny<System.FormatException>(
            () => ConfFormat.Read("[General]\nLanguage = Deutsch\n"));
    }

    [Fact]
    public void Описания_в_конфиге_всегда_английские()
    {
        // The file is read when something goes wrong, and one language there is safer: the path
        // from a log line must not depend on which system it was seen on.
        Language before = Text.Current;
        try
        {
            Text.Use(Language.Ru);
            string written = ConfFormat.Write(new AppConfig());

            Assert.Contains("System-Spinner settings.", written);
            Assert.DoesNotContain("настройки", written);
        }
        finally { Text.Use(before); }
    }
}
