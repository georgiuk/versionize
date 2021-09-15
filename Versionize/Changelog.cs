using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Version = NuGet.Versioning.SemanticVersion;
using static Versionize.CommandLine.CommandLineUI;

namespace Versionize
{
    public class Changelog
    {
        private string Preamble = "# Change Log" + Environment.NewLine + Environment.NewLine +
            @"All notable changes to this project will be documented in this file. See [versionize](https://github.com/saintedlama/versionize) for commit guidelines." + 
            Environment.NewLine;

        public Changelog(string file)
        {
            FilePath = file;
        }

        public string FilePath { get; }

        public void Write(Version version, DateTimeOffset versionTime, IChangelogLinkBuilder linkBuilder, IEnumerable<ConventionalCommit> commits,
            bool includeAllCommitsInChangelog = false)
        {
            var versionTagLink = string.IsNullOrWhiteSpace(linkBuilder.BuildVersionTagLink(version)) ? version.ToString() : $"[{version}]({linkBuilder.BuildVersionTagLink(version)})";

            string versionId = $"{version.ToFullString().Replace(".", "_")}";

            var markdown = $"<a name=\"{versionId}\"></a>";
            markdown += "\n";
            markdown += $"## <a id=\"{versionId}\"></a> {versionTagLink} ({versionTime.Year}-{versionTime.Month}-{versionTime.Day})";
            markdown += Environment.NewLine;
            markdown += Environment.NewLine;

            var bugFixes = BuildBlock("Bug Fixes", linkBuilder, commits.Where(commit => commit.IsFix), versionId);

            if (!string.IsNullOrWhiteSpace(bugFixes))
            {
                markdown += bugFixes;
                markdown += Environment.NewLine;
            }

            var features = BuildBlock("Features", linkBuilder, commits.Where(commit => commit.IsFeature), versionId);

            if (!string.IsNullOrWhiteSpace(features))
            {
                markdown += features;
                markdown += Environment.NewLine;
            }

            var breaking = BuildBlock("Breaking Changes", linkBuilder, commits.Where(commit => commit.IsBreakingChange), versionId);

            if (!string.IsNullOrWhiteSpace(breaking))
            {
                markdown += breaking;
                markdown += Environment.NewLine;
            }

            if (includeAllCommitsInChangelog)
            {
                var other = BuildBlock("Other", linkBuilder, commits.Where(commit => !commit.IsFix && !commit.IsFeature && !commit.IsBreakingChange), versionId);

                if (!string.IsNullOrWhiteSpace(other))
                {
                    markdown += other;
                    markdown += Environment.NewLine;
                }
            }

            if (File.Exists(FilePath))
            {
                var contents = File.ReadAllText(FilePath);

                var firstReleaseHeadlineIdx = contents.IndexOf("<a name=\"", StringComparison.Ordinal);

                Match result = Regex.Match(contents, "a name=\"" +@"([\s\S]*?)" +"\"></a>", RegexOptions.ECMAScript);
                if(result.Groups.Count > 1 && result.Groups[1].Value != null)
                {
                    Version lastFoundVersion = null;
                    if(Version.TryParse(result.Groups[1].Value.Replace("_","."), out lastFoundVersion) && lastFoundVersion >= version)
                    {
                        Exit($"Could not append changelog for version {version}. The most recent version found is {lastFoundVersion} which is larger or equal to {version}.", 0);
                    }
                }           

                if (firstReleaseHeadlineIdx >= 0)
                {
                    markdown = contents.Insert(firstReleaseHeadlineIdx, markdown);
                }
                else
                {
                    markdown = contents + Environment.NewLine + Environment.NewLine + markdown;
                }

                File.WriteAllText(FilePath, markdown);
            }
            else
            {
                File.WriteAllText(FilePath, Preamble + "\n" + markdown);
            }
        }

        public static string BuildBlock(string header, IChangelogLinkBuilder linkBuilder, IEnumerable<ConventionalCommit> commits, string versionId)
        {
            if (!commits.Any())
            {
                return null;
            }

            var block = $"### <a id=\"{versionId}-{header.Replace(" ", "_")}\"></a> {header}";
            block += Environment.NewLine;
            block += Environment.NewLine;

            return commits
                .OrderBy(c => c.Scope)
                .ThenBy(c => c.Subject)
                .Aggregate(block, (current, commit) => current + BuildCommit(commit, linkBuilder) + Environment.NewLine);
        }

        public static string BuildCommit(ConventionalCommit commit, IChangelogLinkBuilder linkBuilder)
        {
            var sb = new StringBuilder("* ");

            if (!string.IsNullOrWhiteSpace(commit.Scope))
            {
                sb.Append($"**{commit.Scope}:** ");
            }

            sb.Append(commit.Subject);

            var commitLink = linkBuilder.BuildCommitLink(commit);

            if (!string.IsNullOrWhiteSpace(commitLink))
            {
                sb.Append($" ([{commit.Sha.Substring(0, 7)}]({commitLink}))");
            }

            return sb.ToString();
        }

        public static Changelog Discover(string directory)
        {
            var changelogFile = Path.Combine(directory, "CHANGELOG.md");

            return new Changelog(changelogFile);
        }
    }
}
