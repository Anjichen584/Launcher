using System.Security.Cryptography;
using System.Text;

namespace Launcher.Core;

/// <summary>离线模式账号：与原版服务端的离线 UUID 规则一致（UUIDv3 基于 "OfflinePlayer:" + 用户名）。</summary>
public static class OfflineAccount
{
    public static string UuidFor(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("用户名不能为空", nameof(username));

        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30); // version 3
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80); // IETF variant

        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }
}
