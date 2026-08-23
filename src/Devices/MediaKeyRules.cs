//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

namespace SystemSpinnerX64.Devices;

// Who answers a media key: the app, or Windows. The decision alone — nothing here touches a screen
// or the mixer, so the rules sit in one place and can be checked without hardware.
internal static class MediaKeyRules
{
    // Without a monitor driven over DDC, Windows does the job itself and shows its own panel.
    public static bool Takes(bool drivesOverDdc, bool alwaysCustomOsd) => drivesOverDdc || alwaysCustomOsd;

    // In HDR the monitor either ignores the brightness command or stops answering it altogether —
    // and a screen that answers nothing was never opened as one that can be driven. So HDR is asked
    // about first, before "is there anything to move": otherwise the screen looks like an ordinary
    // one with no brightness control, and the panel comes up with a number that is a lie.
    public static MediaKeyResult Brightness(bool drivesOverDdc, bool alwaysCustomOsd,
                                            bool targetFound, bool screenInHdr)
    {
        if (!Takes(drivesOverDdc, alwaysCustomOsd)) return MediaKeyResult.PassThrough;

        if (screenInHdr) return MediaKeyResult.Silent;

        if (!targetFound) return alwaysCustomOsd ? MediaKeyResult.Consumed : MediaKeyResult.PassThrough;

        return MediaKeyResult.Consumed;
    }

    public static MediaKeyResult Volume(bool moved, bool alwaysCustomOsd) =>
        moved || alwaysCustomOsd ? MediaKeyResult.Consumed : MediaKeyResult.PassThrough;
}
