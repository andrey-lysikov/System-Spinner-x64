//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

namespace SystemSpinnerX64.Spinner;

// One set of tray animation frames.
public sealed record SpinnerStyle(string Name, int FrameCount, bool SupportsEffect, int SpeedCoefficient);
