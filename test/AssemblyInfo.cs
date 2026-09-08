//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using Xunit;

// Log is one static file the whole program writes to, so tests running in parallel would hold it
// open while LogTests rotates it. The suite takes a fifth of a second, so nothing is lost.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
