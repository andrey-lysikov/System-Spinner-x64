//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace SystemSpinnerX64.Monitoring;

// Works out whose fan this is from the sensor name and the hardware it was found on.
internal static class FanClassifier
{
    // Words that give an AIO away, in the sensor name and in the controller name.
    private static readonly string[] AioMarkers =
    {
        "pump", "aio", "water", "liquid", "насос", "kraken", "capellix", "commander",
        "quadro", "octo", "d5", "ddc", "h100", "h115", "h150", "h170", "galahad"
    };

    public static FanRole Classify(string sensorName, IHardware owner, bool underGpu) =>
        Classify(sensorName, owner.Name, owner.HardwareType, underGpu || IsGpu(owner));

    // The same on plain values instead of a hardware object, so it can be tested: a stub for
    // IHardware would be longer than the logic under test.
    public static FanRole Classify(string sensorName, string hardwareName, HardwareType hardwareType, bool onGpu)
    {
        if (onGpu) return FanRole.Gpu;

        string name = sensorName.ToLowerInvariant();
        string hardware = hardwareName.ToLowerInvariant();

        // A pump is named in many ways but almost always with one of these words; fans hanging off
        // an AIO controller count as AIO too — they sit on its radiator.
        if (AioMarkers.Any(m => name.Contains(m, StringComparison.Ordinal))) return FanRole.Aio;

        if (hardwareType is HardwareType.Cooler &&
            AioMarkers.Any(m => hardware.Contains(m, StringComparison.Ordinal))) return FanRole.Aio;

        if (name.Contains("cpu", StringComparison.Ordinal)) return FanRole.Cpu;

        return FanRole.Case;
    }

    public static bool IsGpu(IHardware hw) =>
        hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
}
