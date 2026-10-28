using System.Text.Json;
using Launcher.Core;

namespace Launcher.Core.Tests;

public class RuleEvaluatorTests
{
    [Fact]
    public void NoRules_IsAllowed()
    {
        Assert.True(RuleEvaluator.IsAllowed(null));
        Assert.True(RuleEvaluator.IsAllowed(new List<Rule>()));
    }

    [Fact]
    public void OsSpecificAllow_MatchesOnlyCurrentPlatform()
    {
        var current = new List<Rule>
        {
            new() { Action = "allow", Os = new OsRule { Name = Platform.OsName } },
        };
        Assert.True(RuleEvaluator.IsAllowed(current));

        var other = new List<Rule>
        {
            new() { Action = "allow", Os = new OsRule { Name = "solaris" } },
        };
        Assert.False(RuleEvaluator.IsAllowed(other));
    }

    [Fact]
    public void DisallowCurrentOs_ExcludesLibrary()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow" },
            new() { Action = "disallow", Os = new OsRule { Name = Platform.OsName } },
        };
        Assert.False(RuleEvaluator.IsAllowed(rules));
    }

    [Fact]
    public void FeatureRequiringTrue_IsNotMatched()
    {
        var rules = new List<Rule>
        {
            new() { Action = "allow", Features = new Dictionary<string, bool> { ["is_demo_user"] = true } },
        };
        Assert.False(RuleEvaluator.IsAllowed(rules));
    }
}

public class OfflineAccountTests
{
    [Fact]
    public void Uuid_MatchesVanillaOfflineUuid()
    {
        // 原版离线模式下 "Notch" 的已知 UUID
        Assert.Equal("b50ad385-829d-3141-a216-7e7d7539ba7f", OfflineAccount.UuidFor("Notch"));
    }

    [Fact]
    public void EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => OfflineAccount.UuidFor("  "));
    }
}

public class JavaLocatorTests
{
    [Theory]
    [InlineData("openjdk version \"17.0.2\" 2022-01-18", 17)]
    [InlineData("java version \"1.8.0_381\"", 8)]
    [InlineData("openjdk version \"21\" 2023-09-19", 21)]
    public void ParsesMajorVersion(string output, int expected)
    {
        Assert.Equal(expected, JavaLocator.ParseMajorVersion(output));
    }

    [Fact]
    public void UnknownOutput_ReturnsNull()
    {
        Assert.Null(JavaLocator.ParseMajorVersion("no version here"));
    }
}

public class MirrorSourceTests
{
    [Fact]
    public void MojangLibraryUrl_IsRewrittenToMirror()
    {
        var url = "https://libraries.minecraft.net/com/mojang/brigadier/1.0.18/brigadier-1.0.18.jar";
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/maven/com/mojang/brigadier/1.0.18/brigadier-1.0.18.jar",
            MirrorSource.ToMirror(url));
    }

    [Fact]
    public void Candidates_MirrorFirstThenOfficial()
    {
        var official = "https://piston-meta.mojang.com/v1/packages/x.json";
        var list = MirrorSource.Candidates(official);
        Assert.Equal(2, list.Count);
        Assert.StartsWith(MirrorSource.BmclApi, list[0]);
        Assert.Equal(official, list[1]);
    }

    [Fact]
    public void UnknownHost_HasSingleCandidate()
    {
        var url = "https://example.com/file.jar";
        Assert.Equal(new[] { url }, MirrorSource.Candidates(url));
    }
}

public class CommandBuilderTests
{
    private static LaunchOptions Options(string separator = ";") => new()
    {
        JavaPath = @"C:\Java\bin\java.exe",
        GameDirectory = @"C:\mc",
        AssetsDirectory = @"C:\mc\assets",
        LibrariesDirectory = @"C:\mc\libraries",
        NativesDirectory = @"C:\mc\versions\1.20.1\natives",
        ClientJar = @"C:\mc\versions\1.20.1\1.20.1.jar",
        ClasspathEntries = new[] { @"C:\mc\libraries\a.jar", @"C:\mc\libraries\b.jar" },
        Username = "Steve",
        Uuid = "00000000-0000-0000-0000-000000000000",
        ClasspathSeparator = separator,
    };

    [Fact]
    public void LegacyArguments_AreExpandedAndClasspathBuilt()
    {
        var version = JsonSerializer.Deserialize<VersionJson>("""
        {
          "id": "1.7.10",
          "mainClass": "net.minecraft.client.main.Main",
          "minecraftArguments": "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory}"
        }
        """)!;

        var cmd = CommandBuilder.Build(version, Options());

        Assert.Equal(@"C:\Java\bin\java.exe", cmd.FileName);
        Assert.Contains("-Xmx2048M", cmd.Arguments);
        Assert.Contains("net.minecraft.client.main.Main", cmd.Arguments);
        var cp = cmd.Arguments[cmd.Arguments.ToList().IndexOf("-cp") + 1];
        Assert.Equal(@"C:\mc\libraries\a.jar;C:\mc\libraries\b.jar;C:\mc\versions\1.20.1\1.20.1.jar", cp);
        Assert.Contains("Steve", cmd.Arguments);
        Assert.Contains("1.7.10", cmd.Arguments);
    }

    [Fact]
    public void ModernArguments_RespectRulesAndNoPlaceholdersLeft()
    {
        var version = JsonSerializer.Deserialize<VersionJson>("""
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": {
            "jvm": [
              "-Djava.library.path=${natives_directory}",
              {"rules":[{"action":"allow","os":{"name":"osx"}}],"value":"-XstartOnFirstThread"},
              "-cp",
              "${classpath}"
            ],
            "game": [
              "--username", "${auth_player_name}",
              {"rules":[{"action":"allow","features":{"is_demo_user":true}}],"value":"--demo"},
              {"rules":[{"action":"allow"}],"value":["--width","854"]}
            ]
          }
        }
        """)!;

        var cmd = CommandBuilder.Build(version, Options());

        Assert.DoesNotContain("-XstartOnFirstThread", cmd.Arguments);
        Assert.DoesNotContain("--demo", cmd.Arguments);
        Assert.Contains("--username", cmd.Arguments);
        Assert.Contains("Steve", cmd.Arguments);
        Assert.DoesNotContain(cmd.Arguments, a => a.Contains("${"));
    }
}
