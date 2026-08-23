//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

namespace SystemSpinnerX64.Monitoring;

// One detected fan sensor: what it is, where it sits and what it read while scanning.
public sealed record FanSensor(string Name, string HardwareName, FanRole Role, double? Rpm)
{
    // Line for the scan report in the log.
    public string Describe =>
        $"[{Role}] {HardwareName} / {Name} = {(Rpm is null ? "—" : Rpm.Value.ToString("0"))} rpm";
}
