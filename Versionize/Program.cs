using System.IO;
using McMaster.Extensions.CommandLineUtils;
using NuGet.Versioning;
using Versionize.CommandLine;

namespace Versionize
{
    [Command(
        Name = "Versionize",
        Description = "Automatic versioning and CHANGELOG generation, using conventional commit messages")]
    public class Program
    {
        public static int Main(string[] args)
        {
            var app = new CommandLineApplication();
            app.Name = "versionize";
            app.HelpOption();
            app.VersionOption("-v|--version", GetVersion());

            var optionWorkingDirectory = app.Option("-w|--workingDir <WORKING_DIRECTORY>", "Directory containing projects to version", CommandOptionType.SingleValue);
            var optionDryRun = app.Option("-d|--dry-run", "Skip changing versions in projects, changelog generation and git commit", CommandOptionType.NoValue);
            var optionSkipDirty = app.Option("--skip-dirty", "Skip git dirty check", CommandOptionType.NoValue);
            var optionReleaseAs = app.Option("-r|--release-as <VERSION>", "Specify the release version manually", CommandOptionType.SingleValue);
            var optionSilent = app.Option("--silent", "Suppress output to console", CommandOptionType.NoValue);

            var optionSkipCommit = app.Option("--skip-commit", "Skip commit and git tag after updating changelog and incrementing the version", CommandOptionType.NoValue);
            var optionSkipChangelog = app.Option("--skip-changelog", "Skip creating and commiting the changelog", CommandOptionType.NoValue);
            var optionSkipCommitProjects = app.Option("--skip-commit-projects", "Skip commiting project files", CommandOptionType.NoValue);
            var optionSkipNewTag = app.Option("--skip-tag", "Skip creating and commiting new tag", CommandOptionType.NoValue);
            var optionReadVersionFromTag = app.Option("--version-from-tag", "Read the version from the tag", CommandOptionType.NoValue);
            var optionSkipProjectVersioning = app.Option("--skip-project-versioning", "Skip writing and commiting project version", CommandOptionType.NoValue);
            var optionIgnoreInsignificant = app.Option("-i|--ignore-insignificant-commits", "Do not bump the version if no significant commits (fix, feat or BREAKING) are found", CommandOptionType.NoValue);
            var optionIncludeAllCommitsInChangelog = app.Option("--changelog-all", "Include all commits in the changelog not just fix, feat and breaking changes", CommandOptionType.NoValue);
            var optionReleaseCommitMessageSuffix = app.Option("--commit-suffix", "Suffix to be added to the end of the release commit message (e.g. [skip ci])", CommandOptionType.SingleValue);
            var optionVersionTagPrefix = app.Option("--version-tag-prefix", "The prefix used for the version tag", CommandOptionType.SingleValue);

            app.OnExecute(() =>
            {
                CommandLineUI.Verbosity = optionSilent.HasValue() ? LogLevel.Silent : LogLevel.All;

                WorkingCopy
                    .Discover(optionWorkingDirectory.Value() ?? Directory.GetCurrentDirectory())
                    .Versionize(
                        dryrun: optionDryRun.HasValue(),
                        skipDirtyCheck: optionSkipDirty.HasValue(),
                        skipCommit: optionSkipCommit.HasValue(),
                        skipNewTag: optionSkipNewTag.HasValue(),
                        skipChangelog: optionSkipChangelog.HasValue(),
                        skipWriteProjectVersion: optionSkipProjectVersioning.HasValue(),
                        skipCommitProjectVersion: optionSkipCommitProjects.HasValue(),
                        readVersionFromTag: optionReadVersionFromTag.HasValue(),
                        releaseVersion: optionReleaseAs.Value(),
                        ignoreInsignificant: optionIgnoreInsignificant.HasValue(),
                        includeAllCommitsInChangelog: optionIncludeAllCommitsInChangelog.HasValue(),
                        releaseCommitMessageSuffix: optionReleaseCommitMessageSuffix.Value(),
                        versionTagPrefix: optionVersionTagPrefix.Value()
                    );

                return 0;
            });

            return app.Execute(args);
        }

        static string GetVersion() => typeof(Program).Assembly.GetName().Version.ToString();
    }
}
