//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using Xunit;

// The classes under test are not all separate from one another: Log is one static file for the
// whole program, and several of them write to it while they work. Run in parallel, a class that
// logs a warning holds the file open exactly when LogTests is rotating it, and the rotation is
// swallowed — a failure of the arrangement, not of the code. The whole suite takes a fifth of a
// second, so nothing is lost by running it one test at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
