//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

namespace SystemSpinnerX64.Configuration;

// The custom on-screen display for volume and brightness, and what drives it.
public sealed class OsdConfig
{
    // Show the custom OSD even when there is nothing to control — a single built-in screen, say.
    public bool AlwaysUseCustomOsd { get; set; }

    // Steps the 0…100 scale is divided into: one key press is one step.
    public int AdjustmentSteps { get; set; } = 16;

    // Drive external monitor brightness over DDC/CI.
    public bool ControlExternalBrightness { get; set; } = true;

    // Move the monitor's own volume over DDC/CI in step with the Windows mixer. Both carry the
    // same number then: the mixer alone would leave the monitor as a second attenuator nothing on
    // screen shows, and the monitor alone would leave the Windows slider standing still while the
    // sound changed. With no monitor to drive this makes no difference — the mixer is all there is.
    public bool ControlExternalVolume { get; set; } = true;

    // The pair standing in for brightness keys the keyboard has not got, dimmer first. "native"
    // is written here as soon as the real keys are seen, and then nothing is registered — a
    // keyboard with its own keys needs no combination held aside for it.
    public string BrightnessKeys { get; set; } = "Ctrl+F1/F2";

    // What the value means when the keyboard turns out to have the real keys.
    public const string NativeKeys = "native";
}
