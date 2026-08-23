//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using SystemSpinnerX64.Devices;
using Xunit;

namespace SystemSpinnerX64.Tests;

public class DdcQueueTests
{
    [Fact]
    public void Устаревшая_команда_вытесняется_новой()
    {
        var ran = new List<int>();
        var done = new ManualResetEventSlim();

        // The first command blocks the worker while the rest queue up behind it, so they are all
        // waiting under the same name by the time it lets go — as they are when a key is held.
        var busy = new ManualResetEventSlim();

        DdcQueue.Run("test:gate", () => busy.Wait(TimeSpan.FromSeconds(5)));

        for (int value = 1; value <= 5; value++)
        {
            int step = value;
            DdcQueue.Run("test:brightness", () =>
            {
                ran.Add(step);
                done.Set();
            });
        }

        busy.Set();

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)), "the queue did not get to the command");

        // Give the worker a moment to make the mistake this is about — running the ones it dropped.
        Thread.Sleep(200);

        Assert.Equal(new[] { 5 }, ran);
    }

    [Fact]
    public void Разные_имена_выполняются_оба()
    {
        var ran = new List<string>();
        var done = new CountdownEvent(2);

        DdcQueue.Run("test:one", () => { lock (ran) ran.Add("one"); done.Signal(); });
        DdcQueue.Run("test:two", () => { lock (ran) ran.Add("two"); done.Signal(); });

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)), "the queue did not get to the commands");

        Assert.Contains("one", ran);
        Assert.Contains("two", ran);
    }
}
