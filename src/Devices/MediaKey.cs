//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

namespace SystemSpinnerX64.Devices;

// Which key was pressed. The macOS set minus the keyboard backlight.
public enum MediaKey
{
    VolumeUp,
    VolumeDown,
    Mute,
    BrightnessUp,
    BrightnessDown
}

// What to do with the press. PassThrough means there was nothing to control and the key goes back
// to Windows, which then shows its own panel.
public enum MediaKeyResult
{
    PassThrough,
    Consumed,

    // Taken, with nothing to show for it: a monitor in HDR ignores the brightness command.
    Silent
}
