using System;
using System.Linq;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.GitHub.ChangeLogExtensions;
using System.IO;
using Microsoft.Build.Tasks;
using Nuke.Common.IO;
using Nuke.Common.Tools.Docker;
using Serilog;
using Serilog.Events;
using LogLevel = Nuke.Common.LogLevel;


class Build : NukeBuild
{
    
    public static int Main () => Execute<Build>(x => x.Compile);

    //[Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    //readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;
    //readonly Configuration Configuration = Configuration.Release;

    [Parameter]
    string Configuration { get; } = IsLocalBuild ? "Debug" : "Release";

    [Solution] readonly Solution Solution;

    static Int16 majorVersion = 1;
    static Int16 minorVersion = 2;
    string version = string.Format("{0}.{1}", majorVersion.ToString(), minorVersion.ToString());


    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath PackDirectory => RootDirectory / "artifacts/nupkg";
    AbsolutePath AppDirectory => RootDirectory / "artifacts/app";

    AbsolutePath DockerFile
    {
        get
        {
            if(Configuration == "Debug") return RootDirectory / "DockerfileDev";
            else return RootDirectory / "Dockerfile";
        }
    }

    string ChangeLogFile => RootDirectory / "CHANGELOG.md";


    string[] Authors = { "Felipe F Quintella" };

    Target Clean => _ => _
        .Executes(() =>
        {
            //DeleteDirectory(ArtifactsDirectory);
            DeleteDirectory(RootDirectory + "/adrapi/obj");
            DeleteDirectory(RootDirectory + "/adrapi/bin");
            DeleteDirectory(RootDirectory + "/domain/obj");
            DeleteDirectory(RootDirectory + "/domain/bin");
            
            //DeleteDirectories(GlobDirectories(TestsDirectory, "**/bin", "**/obj"));
            EnsureCleanDirectory(ArtifactsDirectory);
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            Log.Write(LogEventLevel.Information, "Restoring packages!");
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
                .SetAssemblyVersion(version)
                //.SetFileVersion(GitVersion.AssemblySemFileVer)
                .SetOutputDirectory(AppDirectory)
                .EnableNoRestore());
            
            /*MSBuild(o => o
                .SetTargetPath(Solution)
                .SetTargets("Clean", "Build")
                .SetConfiguration(Configuration)
                .EnableNodeReuse());
                */
        });

    private Target Local_Publish => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            Log.Write(LogEventLevel.Information, "Publishing to artifacts...");
            EnsureExistingDirectory(AppDirectory);
            DotNetPublish(s => s
                .SetConfiguration(Configuration)
                .SetAuthors(Authors)
                .SetVersion(version)
                .SetTitle("ADRAPI")
                .SetOutput(AppDirectory)
                //.SetWorkingDirectory(RootDirectory)
                .SetProject(Solution)
            );
          
            DeleteFile(AppDirectory + "/appsettings.Development.json");
            CopyFile(RootDirectory + "/adrapi/nLog.prod.config", AppDirectory + "/nlog.config", FileExistsPolicy.OverwriteIfNewer);

            string fileName = AppDirectory + "/version.txt";
            using (StreamWriter sw = new StreamWriter(fileName, false))
            {
                sw.WriteLine(version);
            }
            
        });

    Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {

            Log.Write( LogEventLevel.Information,"Creating Nupackages...");
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
            Log.Write( LogEventLevel.Information, "Creating Docker Image...");

            string lversion = "latest";

            if (Configuration != "Debug") lversion = version;
            
            
            DockerTasks.DockerBuild(s => s
                .AddLabel("adrapi")
                .SetTag("ffquintella/adrapi:" + lversion)
                .SetFile(DockerFile)
                .SetForceRm(true)
                .SetPath(RootDirectory)
            );



        });

    private Target Deploy_Docker_Image => _ => _
        .DependsOn(Create_Docker_Image)
        .Executes(() =>
        {
            /*DockerPush(s => s
                .SetWorkingDirectory(RootDirectory)
                .SetName("ffquintella/adrapi:" + GitVersion.GetNormalizedFileVersion())
            );*/
        });
}
