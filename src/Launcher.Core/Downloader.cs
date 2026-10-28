using System.Net.Http;
using System.Security.Cryptography;

namespace Launcher.Core;

/// <summary>
/// 带 SHA1 校验、重试和多源回退的文件下载器。
/// </summary>
public static class Downloader
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate }) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("McLauncher/0.1 (+https://github.com/Anjichen584/Launcher)");
        return client;
    }

    /// <summary>
    /// 依次尝试每个地址（每个地址最多重试 3 次）。若提供 sha1，则校验通过才落盘。
    /// 已存在且校验通过的文件会被跳过。
    /// </summary>
    public static async Task DownloadAsync(
        IEnumerable<string> urls,
        string destination,
        string? sha1,
        CancellationToken ct = default)
    {
        if (sha1 is not null && File.Exists(destination)
            && string.Equals(await HashFileAsync(destination, ct), sha1, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var dir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        Exception? last = null;
        foreach (var url in urls)
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var tmp = destination + ".part";
                try
                {
                    using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    await using (var fs = File.Create(tmp))
                    {
                        await response.Content.CopyToAsync(fs, ct);
                    }

                    if (sha1 is not null)
                    {
                        var actual = await HashFileAsync(tmp, ct);
                        if (!string.Equals(actual, sha1, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(tmp);
                            throw new IOException($"SHA1 校验失败: {url}");
                        }
                    }

                    File.Move(tmp, destination, overwrite: true);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    last = ex;
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
            }
        }

        throw new IOException($"下载失败: {destination}", last);
    }

    /// <summary>下载并返回文本内容（用于版本清单、版本 JSON 等）。</summary>
    public static async Task<string> GetStringAsync(IEnumerable<string> urls, CancellationToken ct = default)
    {
        Exception? last = null;
        foreach (var url in urls)
        {
            try
            {
                return await Http.GetStringAsync(url, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }
        }
        throw new IOException("请求失败", last);
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
