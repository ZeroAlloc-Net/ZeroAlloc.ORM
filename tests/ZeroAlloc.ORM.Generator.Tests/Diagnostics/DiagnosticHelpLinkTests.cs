using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Xunit;
using ZeroAlloc.ORM.Generator.Diagnostics;

namespace ZeroAlloc.ORM.Generator.Tests.Diagnostics;

/// <summary>
/// v0.6 Phase B.2 — every <see cref="DiagnosticDescriptor"/> in the catalog
/// MUST have a <c>HelpLinkUri</c> that resolves to a real, non-empty markdown
/// page under <c>docs/diagnostics/</c>. This guards against the "broken link
/// shipped in the analyzer payload" failure mode where adopters click through
/// the IDE's diagnostic help link and land on a 404.
/// </summary>
public sealed class DiagnosticHelpLinkTests
{
    // Anchored, linear-time pattern with a hard timeout — guards against
    // ReDoS (MA0009) and keeps the test cheap even on adversarial input.
    private static readonly Regex HelpLinkPattern = new(
        @"docs/diagnostics/(?<filename>ZAO\d+\.md)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void Every_diagnostic_helpLinkUri_resolves_to_existing_docs_file()
    {
        var docsRoot = LocateDocsDiagnosticsFolder();

        var descriptors = typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .ToList();

        Assert.NotEmpty(descriptors);

        var gaps = new List<string>();
        foreach (var d in descriptors)
        {
            var uri = d.HelpLinkUri;
            if (string.IsNullOrWhiteSpace(uri))
            {
                gaps.Add($"{d.Id}: helpLinkUri is empty");
                continue;
            }

            var match = HelpLinkPattern.Match(uri);
            if (!match.Success)
            {
                gaps.Add($"{d.Id}: helpLinkUri doesn't match expected docs/diagnostics/ZAOxxx.md pattern: {uri}");
                continue;
            }

            var filename = match.Groups["filename"].Value;
            var path = Path.Combine(docsRoot, filename);
            if (!File.Exists(path))
            {
                gaps.Add($"{d.Id}: docs file not found at {path}");
                continue;
            }

            if (new FileInfo(path).Length == 0)
            {
                gaps.Add($"{d.Id}: docs file is empty at {path}");
            }
        }

        Assert.True(
            gaps.Count == 0,
            "Diagnostic helpLinkUri gaps:\n  " + string.Join("\n  ", gaps));
    }

    // #242 — the per-ID pages above prove the *page* exists, but they don't
    // prove the *index* at docs/diagnostics/README.md lists it. The README
    // (both the root one and the docs/diagnostics one) drifted out of sync
    // with the descriptor catalog more than once because nothing forced them
    // to move together. This test parses the canonical index table and fails
    // when a shipped descriptor has no row there, so the index can't go stale
    // silently again.
    [Fact]
    public void Every_diagnostic_has_a_row_in_the_canonical_docs_index()
    {
        var docsRoot = LocateDocsDiagnosticsFolder();
        var indexPath = Path.Combine(docsRoot, "README.md");
        Assert.True(File.Exists(indexPath), $"Canonical docs index not found at {indexPath}");

        var indexText = File.ReadAllText(indexPath);
        var idsInIndex = new HashSet<string>(
            IndexRowPattern.Matches(indexText).Select(m => m.Groups["id"].Value),
            StringComparer.Ordinal);

        var descriptors = typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
            .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
            .ToList();

        Assert.NotEmpty(descriptors);

        var missing = descriptors
            .Select(d => d.Id)
            .Where(id => !idsInIndex.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Descriptor IDs missing a row in docs/diagnostics/README.md:\n  " + string.Join("\n  ", missing));
    }

    // Matches a markdown table row that starts a cell with a ZAOxxx code, e.g.
    // "| ZAO081 | Error | ... |". Anchored to line start and linear-time, same
    // ReDoS guard as HelpLinkPattern above.
    private static readonly Regex IndexRowPattern = new(
        @"^\|\s*(?<id>ZAO\d+)\s*\|",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Multiline,
        TimeSpan.FromSeconds(1));

    private static string LocateDocsDiagnosticsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "diagnostics");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate docs/diagnostics/ from base directory '{AppContext.BaseDirectory}'.");
    }
}
