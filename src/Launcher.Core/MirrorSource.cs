namespace Launcher.Core;

/// <summary>
/// 下载源映射。优先使用国内镜像（BMCLAPI），失败后由调用方回退到官方地址。
/// </summary>
public static class MirrorSource
{
    public const string BmclApi = "https://bmclapi2.bangbang93.com";

    /// <summary>把官方 URL 改写为镜像 URL；无法识别的地址原样返回。</summary>
    public static string ToMirror(string officialUrl)
    {
        return officialUrl
            .Replace("https://piston-meta.mojang.com", BmclApi)
            .Replace("https://launchermeta.mojang.com", BmclApi)
            .Replace("https://launcher.mojang.com", BmclApi)
            .Replace("https://libraries.minecraft.net", BmclApi + "/maven")
            .Replace("https://resources.download.minecraft.net", BmclApi + "/assets")
            .Replace("https://maven.fabricmc.net", BmclApi + "/maven");
    }

    /// <summary>返回候选地址列表：镜像优先，官方兜底。</summary>
    public static IReadOnlyList<string> Candidates(string officialUrl)
    {
        var mirror = ToMirror(officialUrl);
        return mirror == officialUrl
            ? new[] { officialUrl }
            : new[] { mirror, officialUrl };
    }
}
