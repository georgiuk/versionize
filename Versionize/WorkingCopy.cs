using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using static Versionize.CommandLine.CommandLineUI;
using Version = NuGet.Versioning.SemanticVersion;

namespace Versionize
{
    public class WorkingCopy
    {
        private readonly DirectoryInfo _directory;

        private WorkingCopy(DirectoryInfo directory)
        {
            _directory = directory;
        }

        public Version Versionize(bool dryrun = false,
            bool skipDirtyCheck = false,
            bool skipCommit = false,
            bool skipChangelog = false,
            bool skipNewTag = false,
            bool skipWriteProjectVersion = false,
            bool skipCommitProjectVersion = false,
            bool readVersionFromTag = false,
            string releaseVersion = null,
            bool ignoreInsignificant = false,
            bool includeAllCommitsInChangelog = false,
            string releaseCommitMessageSuffix = null,
            string versionTagPrefix = "v")
        {

            var workingDirectory = _directory.FullName;

            using (var repo = new Repository(workingDirectory))
            {
                var isDirty = repo.RetrieveStatus(new StatusOptions()).IsDirty;

                if (!skipDirtyCheck && isDirty)
                {
                    Exit($"Repository {workingDirectory} is dirty. Please commit your changes.", 1);
                }

                var projects = Projects.Discover(workingDirectory);

                Tag currentVersionTag = null;
                Version currentVersion = null;

                if (!readVersionFromTag)
                {
                    if (projects.IsEmpty())
                    {
                        Exit($"Could not find any projects files in {workingDirectory} that have a <Version> defined in their csproj file.", 1);
                    }

                    if (projects.HasInconsistentVersioning())
                    {
                        Exit($"Some projects in {workingDirectory} have an inconsistent <Version> defined in their csproj file. Please update all versions to be consistent or remove the <Version> elements from projects that should not be versioned", 1);
                    }

                    Information($"Discovered {projects.GetProjectFiles().Count()} versionable projects");
                    foreach (var project in projects.GetProjectFiles())
                    {
                        Information($"  * {project}");
                    }

                    currentVersion = projects.Version;
                    currentVersionTag = repo.SelectVersionTag(currentVersion);
                }
                else
                {
                    if (String.IsNullOrEmpty(versionTagPrefix))
                    {
                        currentVersionTag = repo.Tags.LastOrDefault();
                    }
                    else
                    {
                        currentVersionTag = repo.Tags.LastOrDefault(o => o.FriendlyName.StartsWith(versionTagPrefix));
                    }

                    var curVersionFromTag = currentVersionTag.FriendlyName.Replace(versionTagPrefix, "");
                    Version.TryParse(curVersionFromTag, out currentVersion);
                }

                var commitsInVersion = repo.GetCommitsSinceLastVersion(currentVersionTag);

                var conventionalCommits = ConventionalCommitParser.Parse(commitsInVersion);

                var versionIncrement = VersionIncrementStrategy.CreateFrom(conventionalCommits);

                var nextVersion = currentVersionTag == null ? currentVersion : versionIncrement.NextVersion(currentVersion, ignoreInsignificant);

                if (ignoreInsignificant && nextVersion == currentVersion)
                {
                    Exit($"Version was not affected by commits since last release ({currentVersion}), since you specified to ignore insignificant changes, no action will be performed.", 0);
                }

                if (!string.IsNullOrWhiteSpace(releaseVersion))
                {
                    try
                    {
                        nextVersion = Version.Parse(releaseVersion);
                    }
                    catch (Exception)
                    {
                        Exit($"Could not parse the specified release version {releaseVersion} as valid version", 1);
                    }
                }

                var versionTime = DateTimeOffset.Now;


                if(!skipWriteProjectVersion)
                {
                    // Write next version to project files (csproj) and stage
                    if (!dryrun && (nextVersion != currentVersion))
                    {
                        projects.WriteVersion(nextVersion);

                        foreach (var projectFile in projects.GetProjectFiles())
                        {
                            Commands.Stage(repo, projectFile);
                        }
                    }

                    Step($"bumping version from {currentVersion} to {nextVersion} in projects");
                }

                if(!skipChangelog)
                {
                    var changelog = Changelog.Discover(workingDirectory);

                    if (!dryrun)
                    {
                        var changelogLinkBuilder = ChangelogLinkBuilderFactory.CreateFor(repo);
                        changelog.Write(nextVersion, versionTime, changelogLinkBuilder, conventionalCommits, includeAllCommitsInChangelog);
                    }

                    Step("updated CHANGELOG.md");

                    if (!dryrun && !skipCommit)
                    {
                        Commands.Stage(repo, changelog.FilePath);
                    }
                }

                // skip commiting projects
                bool skipCommitProjectsInAnyCase = skipCommitProjectVersion | skipWriteProjectVersion;

                if (!dryrun && !skipCommit && (skipChangelog == false || skipNewTag == false || skipWriteProjectVersion == false))
                {
                    var author = repo.Config.BuildSignature(versionTime);
                    var committer = author;

                    // init to last commit
                    Commit versionCommit = commitsInVersion.FirstOrDefault();
                    if (!skipChangelog || !skipCommitProjectsInAnyCase)
                    {
                        // TODO: Check if tag exists before commit
                        var releaseCommitMessage = $"chore(release): {nextVersion} {releaseCommitMessageSuffix}".TrimEnd();
                        versionCommit = repo.Commit(releaseCommitMessage, author, committer);

                        if(!skipChangelog && !skipCommitProjectsInAnyCase)
                            Step("committed changes in projects and CHANGELOG.md");
                        else if (!skipChangelog)
                            Step("committed changes in CHANGELOG.md");
                        else if (!skipCommitProjectsInAnyCase)
                            Step("committed changes in projects");
                    }

                    if (!skipNewTag)
                    {
                        repo.Tags.Add($"{versionTagPrefix}{nextVersion}", versionCommit, author, $"{nextVersion}");
                        Step($"tagged release as {nextVersion}");
                    }

                    Information("");
                    Information("i Run `git push --follow-tags origin master` to push all changes including tags");
                }
                else if (skipCommit)
                {
                    Information("");
                    Information($"i Commit and tagging of release was skipped. Tag this release as `{versionTagPrefix}{nextVersion}` to make versionize detect the release");
                }

                return nextVersion;
            }
        }

        public static WorkingCopy Discover(string workingDirectory)
        {
            var workingCopyCandidate = new DirectoryInfo(workingDirectory);

            if (!workingCopyCandidate.Exists)
            {
                Exit($"Directory {workingDirectory} does not exist", 2);
            }

            do
            {
                var isWorkingCopy = workingCopyCandidate.GetDirectories(".git").Any();

                if (isWorkingCopy)
                {
                    return new WorkingCopy(workingCopyCandidate);
                }

                workingCopyCandidate = workingCopyCandidate.Parent;
            }
            while (workingCopyCandidate.Parent != null);

            Exit($"Directory {workingDirectory} or any parent directory do not contain a git working copy", 3);

            return null;
        }
    }
}
