//  Copyright © AndreyLysikov
//  SPDX-License-Identifier: Apache-2.0

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using SystemSpinnerX64.Diagnostics;

namespace SystemSpinnerX64.Startup;

// Asks GitHub whether a newer release exists. The same idea as the macOS version: one quiet check
// a day, plus a manual one from the tray menu.
internal static class UpdateChecker
{
    // What the check came back with. Nothing is downloaded or installed — the answer is a version
    // number and the page to get it from.
    internal sealed record Result(string Current, string Latest, bool IsNewer);

    // GitHub refuses a request without a User-Agent, and the header is also how the traffic is
    // recognised on their side.
    private static readonly HttpClient Http = new() { Timeout = AppParameters.Network.RequestTimeout };

    static UpdateChecker()
    {
        Http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(AppParameters.Identity.AppFolder, AppParameters.Identity.Version));

        // Without it the API answers with whatever it feels like; this pins the shape of the JSON.
        Http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public static async Task<Result?> Check()
    {
        try
        {
            using HttpResponseMessage answer = await Http.GetAsync(AppParameters.Links.LatestReleaseApi);

            // Nothing published yet: that is not a failure, it is "no updates".
            if (answer.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Log.Info("update check: no releases published yet");
                return new Result(AppParameters.Identity.Version, AppParameters.Identity.Version, IsNewer: false);
            }

            answer.EnsureSuccessStatusCode();
            string json = await answer.Content.ReadAsStringAsync();

            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("tag_name", out JsonElement tag)) return null;

            string latest = (tag.GetString() ?? "").TrimStart('v', 'V');
            if (latest.Length == 0) return null;

            string current = AppParameters.Identity.Version;
            Log.Info($"update check: running {current}, latest {latest}");

            return new Result(current, latest, IsNewer(latest, current));
        }
        catch (Exception ex)
        {
            // No network, a rate limit, a rewritten API — none of that is worth a crash.
            Log.Warn($"the update check did not go through: {ex.Message}");
            return null;
        }
    }

    // Compared as numbers rather than as text: "0.10.0" is newer than "0.9.0", though it sorts
    // before it. Anything that will not parse is treated as no news.
    internal static bool IsNewer(string latest, string current) =>
        Version.TryParse(latest, out Version? l) &&
        Version.TryParse(current, out Version? c) &&
        l > c;
}
