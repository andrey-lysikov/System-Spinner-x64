using SystemSpinnerX64.Startup;
using Xunit;

namespace SystemSpinnerX64.Tests;

// Comparing the running version with the newest release. The notification depends on this, and
// a wrong answer here means either a nagging app or one that never mentions an update.
public class UpdateCheckerTests
{
    [Theory]
    [InlineData("0.6.0", "0.5.0")]
    [InlineData("1.0.0", "0.9.9")]
    [InlineData("0.5.1", "0.5.0")]
    [InlineData("0.10.0", "0.9.0")]   // as text "0.10.0" sorts before "0.9.0"
    public void Более_новая_версия_распознаётся(string latest, string current) =>
        Assert.True(UpdateChecker.IsNewer(latest, current));

    [Theory]
    [InlineData("0.5.0", "0.5.0")]    // the same one
    [InlineData("0.4.0", "0.5.0")]    // older on the server than here
    [InlineData("0.9.0", "0.10.0")]
    public void Не_более_новая_версия_не_считается_обновлением(string latest, string current) =>
        Assert.False(UpdateChecker.IsNewer(latest, current));

    [Theory]
    [InlineData("", "0.5.0")]
    [InlineData("latest", "0.5.0")]
    [InlineData("0.6.0", "")]
    public void Непонятный_номер_не_принимается_за_обновление(string latest, string current) =>
        // A rewritten tag format must not turn into a daily notification about nothing.
        Assert.False(UpdateChecker.IsNewer(latest, current));

    [Fact]
    public void Своя_версия_состоит_из_трёх_чисел()
    {
        // The tags, the changelog and this check all speak in three numbers.
        string version = SystemSpinnerX64.AppParameters.Identity.Version;

        Assert.Equal(3, version.Split('.').Length);
    }
}
