//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Lighting;

// A lighting controller the sun can drive. Every LED shows the same colour at the same
// brightness, so all a driver has to do is take the controller over and fill it with one colour;
// how its channels and packets are laid out stays the driver's own business.
internal interface ILightDevice : IDisposable
{
    // One line for the log: which controller, which firmware, what it drives.
    string Describe();

    // Switches the controller from its own effect to colours sent from here. Called again after
    // it may have forgotten — a wake from sleep resets most of them.
    void TakeOver();

    // Every LED on every channel in this colour. Throws when the controller went away.
    void Show(Rgb color);
}

// The drivers the program knows, tried in turn. A new controller is one more line in the list.
internal static class LightDevices
{
    // ledsPerChannel is how many LEDs each addressable channel is taken to carry: no controller
    // can tell what is plugged into it. A driver clamps it to what its protocol allows.
    private static readonly (string Name, Func<int, ILightDevice?> Open)[] Drivers =
    {
        ("ASUS Aura", leds => AuraDevice.Open(leds))
    };

    // The first controller that answers, or null when the machine has none.
    public static ILightDevice? Open(int ledsPerChannel)
    {
        foreach ((string name, Func<int, ILightDevice?> open) in Drivers)
        {
            try
            {
                if (open(ledsPerChannel) is { } device) return device;
            }
            catch (Exception ex)
            {
                // One broken driver must not keep the next one from being tried.
                Log.Error($"lighting: the {name} driver failed while looking for a controller", ex);
            }
        }

        return null;
    }
}
