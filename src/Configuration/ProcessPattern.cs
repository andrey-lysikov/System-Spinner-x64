//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SystemSpinnerX64.Configuration;

// One name from BlackListApplications: * stands for any run of characters, ? for one of them.
internal sealed class ProcessPattern
{
    private static readonly char[] Slashes = { '\\', '/' };

    private readonly Regex _regex;

    // With a slash in it the pattern is a path and is held against the whole path of the exe.
    private readonly bool _wholePath;

    public string Text { get; }

    public ProcessPattern(string text)
    {
        Text = text.Trim();
        _wholePath = Text.IndexOfAny(Slashes) >= 0;

        string body = Regex.Escape(Text).Replace(@"\*", ".*").Replace(@"\?", ".");
        _regex = new Regex("^" + body + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    // Whether the exe at this path is the one meant. A bare name goes with or without ".exe".
    public bool Matches(string path)
    {
        if (_wholePath) return _regex.IsMatch(path);

        string file = path[(path.LastIndexOfAny(Slashes) + 1)..];
        if (_regex.IsMatch(file)) return true;

        int dot = file.LastIndexOf('.');
        return dot > 0 && _regex.IsMatch(file[..dot]);
    }

    // Blank entries are skipped: a stray comma in the list must not match every application.
    public static List<ProcessPattern> Compile(IEnumerable<string> names) =>
        names.Where(name => name.Trim().Length > 0).Select(name => new ProcessPattern(name)).ToList();

    // The first pattern the exe answers to, or null when none does.
    public static ProcessPattern? Find(IReadOnlyList<ProcessPattern> patterns, string path) =>
        patterns.FirstOrDefault(pattern => pattern.Matches(path));
}
