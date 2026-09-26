//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Lighting;

// A lighting controller the sun can drive. Every LED shows the same colour at the same
// brightness, so all a driver has to do is take the controller over and fill it with one colour;
// how its channels and packets are laid out stays the driver's own business.
internal interface ILightDevice : IDisposable
{
    // The lighting technology, which names the menu item: Aura, RGB Fusion, Mystic Light.
    string Technology { get; }

    // One line for the log: which controller, which firmware, what it drives.
    string Describe();

    // Switches the controller from its own effect to colours sent from here. Called again after
    // it may have forgotten — a wake from sleep resets most of them.
    void TakeOver();

    // Every LED on every channel in this colour. Throws when the controller went away.
    void Show(Rgb color);
}

// The drivers the program knows. Every controller they find is lit, all in the same colour: the
// board's own headers and a hub beside it are one light. A new controller is one more line in the list.
internal static class LightDevices
{
    // ledsPerChannel is how many LEDs each addressable channel is taken to carry: no controller
    // can tell what is plugged into it. A driver clamps it to what its protocol allows.
    private static readonly (string Name, Func<int, IEnumerable<ILightDevice>> Open)[] Drivers =
    {
        ("ASUS Aura", leds => AuraDevice.Open(leds) is { } aura ? [aura] : []),
        ("Gigabyte RGB Fusion 2", leds => FusionDevice.Open(leds) is { } fusion ? [fusion] : []),
        ("MSI Mystic Light", leds => MysticLightDevice.Open(leds) is { } msi ? [msi] : []),
        ("ASRock Polychrome", _ => PolychromeDevice.Open() is { } asrock ? [asrock] : []),
        ("Nollie", leds => NollieDevice.OpenAll(leds))
    };

    // Every controller that answers, as one; null when the machine has none.
    public static ILightDevice? Open(int ledsPerChannel)
    {
        var found = new List<ILightDevice>();

        foreach ((string name, Func<int, IEnumerable<ILightDevice>> open) in Drivers)
        {
            try
            {
                found.AddRange(open(ledsPerChannel));
            }
            catch (Exception ex)
            {
                // One broken driver must not keep the next one from being tried.
                Log.Error($"lighting: the {name} driver failed while looking for a controller", ex);
            }
        }

        return found.Count switch
        {
            0 => null,
            1 => found[0],
            _ => new LightGroup(found)
        };
    }
}

// Several controllers driven as one. When any of them goes away the whole group is given up and
// looked for again, as a single controller would be.
internal sealed class LightGroup : ILightDevice
{
    private readonly IReadOnlyList<ILightDevice> _devices;

    public LightGroup(IReadOnlyList<ILightDevice> devices) => _devices = devices;

    // "Aura + Nollie" when the board and a hub beside it are lit together.
    public string Technology => string.Join(" + ", _devices.Select(d => d.Technology).Distinct());

    public string Describe() => string.Join("; ", _devices.Select(d => d.Describe()));

    public void TakeOver()
    {
        foreach (ILightDevice device in _devices) device.TakeOver();
    }

    public void Show(Rgb color)
    {
        foreach (ILightDevice device in _devices) device.Show(color);
    }

    public void Dispose()
    {
        foreach (ILightDevice device in _devices) device.Dispose();
    }
}
