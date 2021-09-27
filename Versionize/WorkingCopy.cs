using System;
using System.Collections.Generic;
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
            bool skipWriteProjectVersion = true,
            bool skipCommitProjectVersion = true,
            bool readVersionFromTag = true,
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
                Version nextVersion = null;

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

                nextVersion = currentVersionTag == null ? currentVersion : versionIncrement.NextVersion(currentVersion, ignoreInsignificant);

                if (ignoreInsignificant && nextVersion == currentVersion)
                {
                    Exit($"Version was not affected by commits since last release ({currentVersion}), since you specified to ignore insignificant changes, no action will be performed.", 0);
                }

                

                var versionTime = DateTimeOffset.Now;


                if(!skipWriteProjectVersion)
                {
                    // Write next version to project files (csproj) and stage
                    if (!dryrun && (nextVersion != currentVersion))
                    {
                        projects.WriteVersion(nextVersion);

                        if(!skipCommit)
                        {
                            foreach (var projectFile in projects.GetProjectFiles())
                            {
                                Commands.Stage(repo, projectFile);
                            }
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

        public void UpdateProjectVersion(
            string path = null,
            string releaseVersion = null)
        {
            Version nextVersion = null;

            if (string.IsNullOrWhiteSpace(releaseVersion))
            {
                Exit($"Could not parse the specified release version {releaseVersion} as valid version", 1);
            }

            try
            {
                nextVersion = Version.Parse(releaseVersion);
            }
            catch (Exception)
            {
                Exit($"Could not parse the specified release version {releaseVersion} as valid version", 1);
            }
            

            if (String.IsNullOrWhiteSpace(path))
            {
                Exit($"Path {path} does not exist", 2);
            }

            List<Project> projects = new List<Project>();
            bool isFile = false;
            if (path.EndsWith(".sln"))
            {
                if(!File.Exists(path))
                    Exit($"Path {path} does not exist", 2);

                var slnFInfo = new FileInfo(path);

                var slnLines = File.ReadAllLines(path);
                foreach(var line in slnLines)
                {
                    if(line.Contains(".csproj"))
                    {
                        var parts = line.Split(new char[] { ',' });
                        foreach(var part in parts)
                        {
                            if(part.Contains(".csproj"))
                            {
                                var projRelPath = part.Trim().Trim('"');
                                var projPath = slnFInfo.Directory.FullName + Path.DirectorySeparatorChar + projRelPath;
                                projects.Add(Project.Create(new FileInfo(projPath).FullName, true));
                                break;
                            }
                        }
                    }
                }
            }
            else if(path.EndsWith(".csproj"))
            {
                projects.Add(Project.Create(new FileInfo(path).FullName, true));
            }
            else if(Directory.Exists(path))
            {
                var files = Directory
                    .GetFiles(path, "*.csproj", SearchOption.AllDirectories);
                foreach(var file in files)
                {
                    projects.Add(Project.Create(file, true));
                }
            }

            foreach(var project in projects)
            {
                project.WriteVersion(nextVersion);
            }   
        }

        public void BuildChangelog(bool dryrun = false,
            bool skipDirtyCheck = false,
            bool skipCommit = false,
            string since = null,
            string until = null,
            bool includeAllCommitsInChangelog = false,
            bool changelogNoLinks = true,
            string releaseCommitMessageSuffix = null,
            string changelogFile = null,
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

                Tag untilVersionTag = null;
                Version untilVersion = null;
                if (!String.IsNullOrEmpty(until))
                {
                    untilVersionTag = repo.Tags.LastOrDefault(o => o.FriendlyName == versionTagPrefix + until);
                    if (untilVersionTag != null)
                    {
                        var untilVersionFromTag = untilVersionTag.FriendlyName.Replace(versionTagPrefix, "");
                        Version.TryParse(untilVersionFromTag, out untilVersion);
                    }
                }
                else
                {
                    untilVersionTag = repo.Tags.LastOrDefault(o => o.FriendlyName.StartsWith(versionTagPrefix));
                    if (untilVersionTag != null)
                    {
                        var untilVersionFromTag = untilVersionTag.FriendlyName.Replace(versionTagPrefix, "");
                        Version.TryParse(untilVersionFromTag, out untilVersion);
                    }
                }

                if (untilVersionTag == null)
                {
                    Exit($"Could not find any versioned tags in repo", 0);
                }

                Information($"Until version tag is {untilVersion.ToFullString()} .");

                Tag sinceVersionTag = null;
                Version sinceVersion = null;
                if (!String.IsNullOrEmpty(since))
                {
                    sinceVersionTag = repo.Tags.LastOrDefault(o => o.FriendlyName == versionTagPrefix + since);
                    if (sinceVersionTag != null)
                    {
                        var sinceVersionFromTag = sinceVersionTag.FriendlyName.Replace(versionTagPrefix, "");
                        Version.TryParse(sinceVersionFromTag, out sinceVersion);
                    }
                }
                else
                {
                    foreach (var tag in repo.Tags.Reverse())
                    {
                        if(tag.FriendlyName.StartsWith(versionTagPrefix))
                        {
                            var tagVersionOnly = tag.FriendlyName.Replace(versionTagPrefix, "");
                            Version.TryParse(tagVersionOnly, out sinceVersion);

                            if (sinceVersion < untilVersion)
                                break;
                        }
                    }
                }

                if(sinceVersion != null && untilVersion <= sinceVersion)
                {
                    Exit($"Since version {sinceVersion} is great or equal to until version {untilVersion} .", 0);
                }

                if(sinceVersion != null)
                    Information($"Since version tag is {sinceVersion.ToFullString()} .");
                else
                    Information($"Since version tag not found.");

                var commitsFound = repo.GetCommitsBetweenTags(sinceVersionTag, untilVersionTag);

                var conventionalCommits = ConventionalCommitParser.Parse(commitsFound);

                var versionTime = DateTimeOffset.Now;

                string changelogGitDirectory = null;
                var changelogFileFullPath = Path.Combine(workingDirectory, changelogFile);
                if (changelogFile.Length > 1 && (changelogFile[1] == ':' || changelogFile.StartsWith("\\")) || changelogFile.StartsWith("/"))
                {
                    changelogFileFullPath = changelogFile;

                    FileInfo fInfo = new FileInfo(changelogFileFullPath);
                    DirectoryInfo dInfo = new DirectoryInfo(workingDirectory);
                    if(!fInfo.FullName.StartsWith(dInfo.FullName + Path.DirectorySeparatorChar))
                    {
                        var parentDir = fInfo.Directory;
                        while(parentDir != null)
                        {
                            DirectoryInfo gitDir = new DirectoryInfo(parentDir.FullName + Path.DirectorySeparatorChar + ".git");
                            if (gitDir.Exists)
                            {
                                changelogGitDirectory = gitDir.FullName;
                                break;
                            }

                            parentDir = parentDir.Parent;                 
                        } 
                    }
                }

                var changelog = new Changelog(changelogFileFullPath);
                if (String.IsNullOrEmpty(changelogFile))
                    changelog = Changelog.Discover(workingDirectory);


                // check if it is another repo
                Repository changelogRepo = repo;
                if (changelogGitDirectory != null)
                    changelogRepo = new Repository(changelogGitDirectory);

                try
                {
                    if (!dryrun)
                    {
                        var changelogLinkBuilder = ChangelogLinkBuilderFactory.CreateFor(changelogRepo, changelogNoLinks);
                        changelog.Write(untilVersion, versionTime, changelogLinkBuilder, conventionalCommits, includeAllCommitsInChangelog);
                    }

                    Step("updated CHANGELOG.md");

                    if (!dryrun && !skipCommit)
                    {
                        Commands.Stage(changelogRepo, changelog.FilePath);
                    }

                    if (!dryrun && !skipCommit)
                    {
                        var author = changelogRepo.Config.BuildSignature(versionTime);
                        var committer = author;

                        var releaseCommitMessage = $"docs: New Changelog for version {untilVersion} {releaseCommitMessageSuffix}".TrimEnd();
                        var versionCommit = changelogRepo.Commit(releaseCommitMessage, author, committer);

                        Step("committed changes in CHANGELOG.md");
                        Information("");
                        Information("i Run `git push origin master` to push all changes");
                    }
                    else if (skipCommit)
                    {
                        Information("");
                        Information($"i Commit of changelog was skipped.");
                    }
                }
                finally
                {
                    if (changelogGitDirectory != null)
                        changelogRepo.Dispose();
                }
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
