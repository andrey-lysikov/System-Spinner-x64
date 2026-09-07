//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

namespace SystemSpinnerX64.Devices;

// Who answers a media key: the app, or Windows. The decision alone — nothing here touches a screen
// or the mixer, so the rules sit in one place and can be checked without hardware.
internal static class MediaKeyRules
{
    // Without a monitor driven over DDC, Windows does the job itself and shows its own panel.
    public static bool Takes(bool drivesOverDdc, bool alwaysCustomOsd) => drivesOverDdc || alwaysCustomOsd;

    // Nothing to move means the key goes back to Windows — unless our own panel was asked for in
    // every case, and then it comes up on the value as it stands.
    public static MediaKeyResult Brightness(bool drivesOverDdc, bool alwaysCustomOsd, bool targetFound)
    {
        if (!Takes(drivesOverDdc, alwaysCustomOsd)) return MediaKeyResult.PassThrough;

        if (!targetFound) return alwaysCustomOsd ? MediaKeyResult.Consumed : MediaKeyResult.PassThrough;

        return MediaKeyResult.Consumed;
    }

    public static MediaKeyResult Volume(bool moved, bool alwaysCustomOsd) =>
        moved || alwaysCustomOsd ? MediaKeyResult.Consumed : MediaKeyResult.PassThrough;
}
