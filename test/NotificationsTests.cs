using SystemSpinnerX64.Platform;
using Xunit;

namespace SystemSpinnerX64.Tests;

// Whether Windows will show a notification. The answer decides between a balloon and a dialog,
// so a wrong one means the user is told nothing at all.
public class NotificationsTests
{
    [Fact]
    public void Ответ_читается_без_ошибок()
    {
        // Whatever the machine says, the call must return rather than throw: it runs at the very
        // moment the app is failing to start.
        bool enabled = Notifications.AreEnabled();

        Assert.True(enabled || !enabled);
    }
}
