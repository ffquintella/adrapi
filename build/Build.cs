using System;
using System.Linq;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Docker;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.GitHub.ChangeLogExtensions;
using static Nuke.Docker.DockerBuildSettings;
using static Nuke.Docker.DockerTasks;
using System.IO;


class Build : NukeBuild
{
    
    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion] readonly GitVersion GitVersion;

    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath PackDirectory => RootDirectory / "artifacts/nupkg";
    AbsolutePath AppDirectory => RootDirectory / "artifacts/app";

    AbsolutePath DockerFile => RootDirectory / "Dockerfile";

    string ChangeLogFile => RootDirectory / "CHANGELOG.md";


    string[] Authors = { "Felipe F Quintella" };

    Target Clean => _ => _
        .Executes(() =>
        {
            DeleteDirectories(GlobDirectories(TestsDirectory, "**/bin", "**/obj"));
            EnsureCleanDirectory(ArtifactsDirectory);
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            Logger.Log("Restoring packages!");
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            EnsureExistingDirectory(AppDirectory);

            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(GitVersion.GetNormalizedAssemblyVersion())
                .SetFileVersion(GitVersion.GetNormalizedFileVersion())
                .SetInformationalVersion(GitVersion.InformationalVersion)
                .SetOutputDirectory(AppDirectory)
                .EnableNoRestore());
        });

    private Target Local_Publish => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            Logger.Log("Publishing to artifacts...");
            EnsureExistingDirectory(AppDirectory);
            DotNetPublish(s => s
                .SetConfiguration(Configuration)
                .SetAuthors(Authors)
                .SetVersion(GitVersion.GetNormalizedFileVersion())
                .SetTitle("ADRAPI")
                .SetOutput(AppDirectory)
                .SetWorkingDirectory(RootDirectory)
                .SetProject(Solution)
            );
          
            CopyFile(RootDirectory + "/adrapi/nLog.prod.config", AppDirectory + "/nlog.config", FileExistsPolicy.OverwriteIfNewer);

            string fileName = AppDirectory + "/version.txt";
            using (StreamWriter sw = new StreamWriter(fileName, false))
            {
                sw.WriteLine(GitVersion.GetNormalizedFileVersion());
            }
            
        });

    Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {

            Logger.Log("Creating Nupackages...");
            var changeLog = GetCompleteChangeLog(ChangeLogFile)
                .EscapeStringPropertyForMsBuild();

            DotNetPack(s => s
                .SetProject(Solution)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(PackDirectory)
                .SetPackageReleaseNotes(changeLog));
                
        });

    Target Create_Docker_Image => _ => _
        .DependsOn(Local_Publish)
        .Executes(() =>
        {
            Logger.Log("Creating Docker Image...");

            DockerBuild(s => s
                .AddLabel("adrapi")
                .SetTag("ffquintella/adrapi:" + GitVersion.GetNormalizedFileVersion())
                .SetFile(DockerFile)
                .SetForceRm(true)
                .SetPath(RootDirectory)
                );

        });

    private Target Deploy_Docker_Image => _ => _
        .DependsOn(Create_Docker_Image)
        .Executes(() =>
        {
            DockerPush(s => s
                .SetWorkingDirectory(RootDirectory)
                .SetName("ffquintella/adrapi:" + GitVersion.GetNormalizedFileVersion())
            );
        });
}
