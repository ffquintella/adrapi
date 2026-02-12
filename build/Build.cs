using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.GitHub.ChangeLogExtensions;
using Microsoft.Build.Tasks;
using Nuke.Common.IO;
using Nuke.Common.Tools.Docker;
using Serilog;
using Serilog.Events;
using LogLevel = Nuke.Common.LogLevel;

enum VersionBumpPart
{
    Major,
    Minor,
    Patch
}

class Build : NukeBuild
{
    
    public static int Main () => Execute<Build>(x => x.ListCommands);

    //[Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    //readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;
    //readonly Configuration Configuration = Configuration.Release;

    [Parameter]
    string Configuration { get; } = IsLocalBuild ? "Debug" : "Release";

    [Parameter("Mandatory. Version part to bump: Major, Minor or Patch.")]
    readonly VersionBumpPart? Part;

    [Solution] readonly Solution Solution;

    AbsolutePath VersionFile => RootDirectory / "VERSION";
    string BuildVersion => GetVersionString(ReadCurrentVersion());

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

    Target ListCommands => _ => _
        .Executes(() =>
        {
            var targets = GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(x => x.PropertyType == typeof(Target))
                .Select(x => x.Name)
                .OrderBy(x => x)
                .ToList();

            Log.Information("Available targets:");
            foreach (var target in targets)
                Log.Information("  - {TargetName}", target);
        });

    Target Clean => _ => _
        .Executes(() =>
        {
            //DeleteDirectory(ArtifactsDirectory);
            (RootDirectory / "adrapi/obj").DeleteDirectory();
            (RootDirectory / "adrapi/bin").DeleteDirectory();
            (RootDirectory / "domain/obj").DeleteDirectory();
            (RootDirectory / "domain/bin").DeleteDirectory();
            
            //DeleteDirectories(GlobDirectories(TestsDirectory, "**/bin", "**/obj"));
            ArtifactsDirectory.CreateOrCleanDirectory();
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
            AppDirectory.CreateDirectory();

            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(BuildVersion)
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
            AppDirectory.CreateDirectory();
            DotNetPublish(s => s
                .SetConfiguration(Configuration)
                .SetAuthors(Authors)
                .SetVersion(BuildVersion)
                .SetTitle("ADRAPI")
                .SetOutput(AppDirectory)
                //.SetWorkingDirectory(RootDirectory)
                .SetProject(Solution)
            );
          
            if (Configuration != "Debug") (AppDirectory / "appsettings.Development.json").DeleteFile();
            (RootDirectory / "adrapi/nLog.prod.config")
                .Copy(AppDirectory / "nlog.config", ExistsPolicy.FileOverwriteIfNewer);

            string fileName = AppDirectory + "/version.txt";
            using (StreamWriter sw = new StreamWriter(fileName, false))
            {
                sw.WriteLine(BuildVersion);
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

            if (Configuration != "Debug") lversion = BuildVersion;
            
            
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

    Target Bump => _ => _
        .Executes(() =>
        {
            if (Part is null)
                throw new Exception("Missing mandatory '--part' parameter. Use --part Major, --part Minor or --part Patch.");

            var current = ReadCurrentVersion();
            var selectedPart = Part.Value;
            var bumped = BumpVersionValue(current, selectedPart);
            var newVersion = GetVersionString(bumped);

            File.WriteAllText(VersionFile, newVersion + Environment.NewLine);

            var projects = GetProjectFiles();
            foreach (var projectFile in projects)
            {
                UpdateProjectVersion(projectFile, newVersion);
            }

            Log.Write(LogEventLevel.Information, "Version bumped: {0} -> {1} ({2})", GetVersionString(current), newVersion, selectedPart);
            Log.Write(LogEventLevel.Information, "Updated {0} project files and VERSION.", projects.Count);
        });

    Version ReadCurrentVersion()
    {
        const string defaultVersion = "1.2.0";

        var raw = File.Exists(VersionFile)
            ? File.ReadAllText(VersionFile).Trim()
            : defaultVersion;

        if (!System.Version.TryParse(raw, out var parsed))
            throw new Exception($"Invalid VERSION value '{raw}'. Expected semantic version format like 1.2.3.");

        var patch = parsed.Build < 0 ? 0 : parsed.Build;
        return new Version(parsed.Major, parsed.Minor, patch);
    }

    static Version BumpVersionValue(Version current, VersionBumpPart part)
    {
        var patch = current.Build < 0 ? 0 : current.Build;
        return part switch
        {
            VersionBumpPart.Major => new Version(current.Major + 1, 0, 0),
            VersionBumpPart.Minor => new Version(current.Major, current.Minor + 1, 0),
            VersionBumpPart.Patch => new Version(current.Major, current.Minor, patch + 1),
            _ => current
        };
    }

    static string GetVersionString(Version version)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Build}");
    }

    List<string> GetProjectFiles()
    {
        return Directory.GetFiles(RootDirectory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsInBuildArtifacts(path))
            .OrderBy(path => path)
            .ToList();
    }

    static bool IsInBuildArtifacts(string path)
    {
        return path.Contains("/bin/") || path.Contains("\\bin\\")
            || path.Contains("/obj/") || path.Contains("\\obj\\");
    }

    static void UpdateProjectVersion(string projectFile, string version)
    {
        var content = File.ReadAllText(projectFile);
        var updated = content;

        updated = Regex.Replace(updated, @"<Version>[^<]*</Version>", $"<Version>{version}</Version>");
        updated = Regex.Replace(updated, @"<AssemblyVersion>[^<]*</AssemblyVersion>", $"<AssemblyVersion>{version}</AssemblyVersion>");
        updated = Regex.Replace(updated, @"<FileVersion>[^<]*</FileVersion>", $"<FileVersion>{version}</FileVersion>");

        if (!Regex.IsMatch(updated, @"<Version>[^<]*</Version>"))
        {
            updated = Regex.Replace(
                updated,
                @"<PropertyGroup>\s*",
                $"<PropertyGroup>{Environment.NewLine}    <Version>{version}</Version>{Environment.NewLine}",
                RegexOptions.None,
                TimeSpan.FromSeconds(1));
        }

        if (!string.Equals(content, updated, StringComparison.Ordinal))
            File.WriteAllText(projectFile, updated);
    }
}
