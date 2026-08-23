//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

namespace SystemSpinnerX64.Spinner;

// How the animation frames are coloured.
public enum SpinnerEffect
{
    // As drawn — the frame is shown unchanged.
    Original = 1,

    // White silhouette: suits a dark taskbar.
    White = 2,

    // Black silhouette: suits a light one.
    Black = 3,

    // Silhouette matching the taskbar: black on light, white on dark.
    Auto = 4
}
