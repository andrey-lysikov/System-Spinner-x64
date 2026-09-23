//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;

namespace SystemSpinnerX64.Lighting;

// What the lighting does over time. Every LED shows the same colour at the same brightness, so
// only effects that change the whole light at once are offered.
public enum AuraEffect
{
    // One colour.
    Solid,

    // One colour fading down to dark and back.
    Breathing,

    // The whole spectrum in turn.
    Rainbow
}

// What the lighting shows before the sun and the heat have their say.
internal sealed record AuraLook(AuraEffect Effect, Rgb Color, int Speed)
{
    public bool IsAnimated => Effect != AuraEffect.Solid;

    // The colour a hot machine drifts towards. The rainbow has no base to oppose; the other two
    // take the colour the table picks against their own.
    public Rgb WarnColor => Effect == AuraEffect.Rainbow
        ? ColorMath.DefaultWarn
        : ColorMath.WarnColorFor(Color);

    public override string ToString() => $"{Effect} {Color}, speed {Speed}";
}

internal static class Effects
{
    // Cycles per second: a full cycle takes 25 s at the slowest and 2.5 s at the fastest.
    public static double Rate(AuraLook look) => 0.04 * look.Speed;

    // The colour of the whole light at this phase, 0..1.
    public static Rgb Render(AuraLook look, double phase) => look.Effect switch
    {
        AuraEffect.Breathing => ColorMath.Scale(look.Color, Wave(phase)),
        AuraEffect.Rainbow => ColorMath.FromHue(phase * 360),
        _ => look.Color
    };

    // Smooth 0 -> 1 -> 0 over one period, so the light flows rather than jumps at the seam.
    private static double Wave(double t) => 0.5 - 0.5 * Math.Cos(2 * Math.PI * t);
}

// How far the machine has gone towards its temperature limits: 0 is cool, 1 is at a threshold.
internal static class WarnHeat
{
    // Zero switches a threshold off, as it does for the overlay highlighting.
    public static double Of(double? cpuTemp, double cpuLimit, double? gpuTemp, double gpuLimit) =>
        Math.Max(Ramp(cpuTemp, cpuLimit), Ramp(gpuTemp, gpuLimit));

    // The colour starts to move this far below the threshold and has gone all the way at it.
    private static double Ramp(double? temp, double limit)
    {
        if (temp is not double t || limit <= 0) return 0;

        double span = AppParameters.Aura.WarnRampDegrees;
        return Math.Clamp((t - (limit - span)) / span, 0, 1);
    }
}
