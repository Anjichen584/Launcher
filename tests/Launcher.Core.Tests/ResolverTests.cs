using System.Text.Json;
using Launcher.Core;

namespace Launcher.Core.Tests;

public class MavenCoordinatesTests
{
    [Fact]
    public void Simple_Coordinate()
    {
        Assert.Equal("net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar",
            MavenCoordinates.ToRelativePath("net.fabricmc:fabric-loader:0.20.1"));
    }

    [Fact]
    public void Coordinate_WithClassifier()
    {
        Assert.Equal("org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-linux.jar",
            MavenCoordinates.ToRelativePath("org.lwjgl:lwjgl:3.3.1:natives-linux"));
    }

    [Fact]
    public void Invalid_Throws()
    {
        Assert.Throws<FormatException>(() => MavenCoordinates.ToRelativePath("bad"));
    }
}

public class VersionResolverTests : IDisposable
{
    private readonly string _versions = Path.Combine(Path.GetTempPath(), "mc-resolver-" + Guid.NewGuid().ToString("N"));

    public VersionResolverTests()
    {
        Directory.CreateDirectory(_versions);
        Write("1.20.1", """
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "assetIndex": {"id": "5", "url": "https://example.com/5.json", "sha1": "x", "size": 1},
          "downloads": {"client": {"url": "https://example.com/client.jar", "sha1": "abc", "size": 1}},
          "javaVersion": {"component": "java-runtime-gamma", "majorVersion": 17},
          "arguments": {"game": ["--vanilla"], "jvm": ["-Dvanilla=1"]},
          "libraries": [{"name": "com.example:base:1.0"}]
        }
        """);
        Write("fabric-loader-0.20.1-1.20.1", """
        {
          "id": "fabric-loader-0.20.1-1.20.1",
          "inheritsFrom": "1.20.1",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "arguments": {"game": ["--fabric"], "jvm": ["-DFabricMcEmu=net.minecraft.client.main.Main"]},
          "libraries": [
            {"name": "net.fabricmc:fabric-loader:0.20.1", "url": "https://maven.fabricmc.net/", "sha1": "s1", "size": 2}
          ]
        }
        """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_versions, recursive: true); } catch { }
    }

    private void Write(string id, string json)
    {
        var dir = Path.Combine(_versions, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, id + ".json"), json);
    }

    [Fact]
    public void Child_Inherits_Parent_Jar_Java_And_Assets()
    {
        var resolved = VersionResolver.Resolve(_versions, "fabric-loader-0.20.1-1.20.1");

        Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", resolved.Json.MainClass);
        Assert.Equal("1.20.1", resolved.JarId);
        Assert.Equal(17, resolved.Json.RequiredJavaMajor);
        Assert.Equal("5", resolved.Json.AssetIndex!.Id);
        Assert.NotNull(resolved.Json.Downloads);
    }

    [Fact]
    public void Arguments_And_Libraries_Are_Merged_Parent_First_For_Args_And_Child_First_For_Libraries()
    {
        var resolved = VersionResolver.Resolve(_versions, "fabric-loader-0.20.1-1.20.1");

        var game = resolved.Json.Arguments!.Game.Select(e => e.GetString()).ToList();
        Assert.Equal(new[] { "--vanilla", "--fabric" }, game);

        Assert.Equal("net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar",
            VersionService.ResolveArtifact(resolved.Json.Libraries[0]).RelPath?.Replace('\\', '/'));
        Assert.Equal("com.example:base:1.0", resolved.Json.Libraries[1].Name);
    }

    [Fact]
    public void Loader_Library_Uses_Url_And_Sha1()
    {
        var lib = new Library
        {
            Name = "net.fabricmc:fabric-loader:0.20.1",
            Url = "https://maven.fabricmc.net/",
            Sha1 = "s1",
        };
        var (rel, url, sha1) = VersionService.ResolveArtifact(lib);
        Assert.Equal("net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar", rel);
        Assert.Equal("https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar", url);
        Assert.Equal("s1", sha1);
    }

    [Fact]
    public void Missing_Parent_Throws()
    {
        Write("orphan", """{"id":"orphan","inheritsFrom":"does-not-exist","mainClass":"X"}""");
        Assert.ThrowsAny<Exception>(() => VersionResolver.Resolve(_versions, "orphan"));
    }

    [Fact]
    public void Cycle_Is_Detected()
    {
        Write("a", """{"id":"a","inheritsFrom":"b","mainClass":"X"}""");
        Write("b", """{"id":"b","inheritsFrom":"a","mainClass":"X"}""");
        Assert.Throws<InvalidDataException>(() => VersionResolver.Resolve(_versions, "a"));
    }
}

public class PlatformTests
{
    [Fact]
    public void Natives_Match_Only_Current_Platform()
    {
        var current = Platform.OsName switch
        {
            "windows" => "org.lwjgl:lwjgl:3.3.1:natives-windows",
            "osx" => "org.lwjgl:lwjgl:3.3.1:natives-macos",
            _ => "org.lwjgl:lwjgl:3.3.1:natives-linux",
        };
        Assert.True(VersionService.IsNativesLibraryForThisPlatform(current));
    }

    [Fact]
    public void Natives_Of_Other_Platform_Are_Ignored()
    {
        Assert.False(VersionService.IsNativesLibraryForThisPlatform("org.lwjgl:lwjgl:3.3.1:natives-solaris"));
        Assert.False(VersionService.IsNativesLibraryForThisPlatform("org.lwjgl:lwjgl:3.3.1"));
    }

    [Fact]
    public void Native_Binary_Detection()
    {
        Assert.True(Platform.IsNativeBinary("lwjgl.dll"));
        Assert.True(Platform.IsNativeBinary("libglfw.so"));
        Assert.True(Platform.IsNativeBinary("liblwjgl.dylib"));
        Assert.False(Platform.IsNativeBinary("readme.txt"));
    }
}
