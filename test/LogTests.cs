//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SystemSpinnerX64.Diagnostics;
using Xunit;

namespace SystemSpinnerX64.Tests;

/// <summary>The log file: which lines survive without Debug, and how the numbered generations
/// are kept. The class shares one static Log, so xunit runs these one after another.</summary>
public class LogTests : IDisposable
{
    private readonly List<string> _folders = new();

    // A rotation test writes megabytes; they do not belong in the temp folder after the run.
    public void Dispose()
    {
        foreach (string folder in _folders)
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (IOException) { }
        }
    }

    private string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SystemSpinnerLogTests", Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
        _folders.Add(folder);
        return folder;
    }

    private static string Open(string folder)
    {
        Log.Start(folder);
        return Path.Combine(folder, AppParameters.Identity.LogFile);
    }

    // Fills the log to the rotation size without going through Log: what is being checked is the
    // rotation, not how fast a megabyte can be written a line at a time.
    private static void FillToRotationSize(string path) =>
        File.WriteAllText(path, new string('x', (int)AppParameters.Logging.MaxBytes + 1));

    [Fact]
    public void Без_отладки_остаются_события_предупреждения_и_ошибки()
    {
        string folder = NewFolder();
        string path = Open(folder);

        Log.SetVerbose(false);

        Log.Info("course of work");
        Log.Key("a key press");
        Log.Event("the machine changed");
        Log.Warn("worth noticing");
        Log.Error("got in the way");

        string written = File.ReadAllText(path);

        Assert.DoesNotContain("course of work", written);
        Assert.DoesNotContain("a key press", written);
        Assert.Contains("EVENT the machine changed", written);
        Assert.Contains("WARN  worth noticing", written);
        Assert.Contains("ERROR got in the way", written);
    }

    [Fact]
    public void С_отладкой_пишется_весь_ход_работы()
    {
        string folder = NewFolder();
        string path = Open(folder);

        Log.SetVerbose(true);

        Log.Info("course of work");
        Log.Key("a key press");

        string written = File.ReadAllText(path);

        Assert.Contains("INFO  course of work", written);
        Assert.Contains("KEY   a key press", written);
    }

    [Fact]
    public void Продолжение_строки_отбивается_под_поле()
    {
        string folder = NewFolder();
        string path = Open(folder);

        Log.SetVerbose(true);
        Log.Info("first line\nsecond line");

        string[] lines = File.ReadAllLines(path);
        string continuation = lines.Single(line => line.EndsWith("second line"));

        Assert.Equal(new string(' ', AppParameters.Logging.ContinuationIndent) + "second line", continuation);
    }

    [Fact]
    public void Переполненный_журнал_уходит_в_первое_поколение()
    {
        string folder = NewFolder();
        string path = Open(folder);

        FillToRotationSize(path);
        Open(folder);

        Assert.True(File.Exists(path + ".1"));
        Assert.True(new FileInfo(path).Length < AppParameters.Logging.MaxBytes);
    }

    [Fact]
    public void Поколений_хранится_не_больше_чем_сказано()
    {
        string folder = NewFolder();
        string path = Open(folder);

        // Two more turns than there are generations: what falls past the last number has to go
        // rather than pile up under a number of its own.
        for (int turn = 0; turn < AppParameters.Logging.MaxRotations + 2; turn++)
        {
            FillToRotationSize(path);
            Open(folder);
        }

        for (int number = 1; number <= AppParameters.Logging.MaxRotations; number++)
            Assert.True(File.Exists($"{path}.{number}"), $"generation {number} is missing");

        Assert.False(File.Exists($"{path}.{AppParameters.Logging.MaxRotations + 1}"));
    }
}
