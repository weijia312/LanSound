using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace pc_app.Pairing;

/// <summary>
/// 本地 CA 与服务器证书管理（任务 2）。
/// 根 CA 持久化于 %AppData%/LanMic/certs；服务器证书按当前网卡 IP 集合签发（SAN），
/// IP 集合变化或临近过期时自动重签。手机需先安装 root-ca.crt 信任根 CA，https/wss 才能用。
/// </summary>
public static class CertManager
{
    private static readonly string CertDir = Path.Combine(
        Environment.GetEnvironmentVariable("LANMIC_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanMic"),
        "certs");

    private static string CaPfxPath => Path.Combine(CertDir, "root-ca.pfx");
    private static string ServerPfxPath => Path.Combine(CertDir, "server.pfx");
    private static string ServerSanPath => Path.Combine(CertDir, "server.san.txt");

    /// <summary>
    /// 服务器证书的私钥存储参数——必须是<strong>持久化</strong>的密钥容器。
    /// <para>
    /// <c>CopyWithPrivateKey</c> / <c>RSA.Create()</c> 产生的是进程内临时密钥，SChannel 无法用它
    /// 完成 TLS 握手：表现为 TCP 连得上、但握手阶段被服务端直接断开
    /// （curl 报 "schannel: failed to receive handshake"）。这种证书在 Kestrel 里
    /// "看起来"启动成功，只有真机访问 https 才会暴露。
    /// </para>
    /// </summary>
    private const X509KeyStorageFlags ServerKeyFlags =
        X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet;

    /// <summary>
    /// 根 CA 的私钥存储参数——<strong>临时</strong>即可：CA 只用于给服务器证书签名，不参与 TLS。
    /// 特意不用 PersistKeySet：每次启动都把 CA 私钥重新持久化，会让后续
    /// <c>ca.Export()</c> 抛 CryptographicException（密钥容器重复导入冲突）。
    /// </summary>
    private const X509KeyStorageFlags CaKeyFlags = X509KeyStorageFlags.EphemeralKeySet;

    /// <summary>供手机下载的根证书（DER 格式 .crt）。</summary>
    public static string CaCrtPath => Path.Combine(CertDir, "root-ca.crt");

    /// <summary>
    /// 确保证书就绪并返回 Kestrel 使用的服务器证书（含私钥）。
    /// </summary>
    /// <param name="ips">需要签入 SAN 的局域网 IP（通常取全部网卡 IP，换网卡免重启）。</param>
    public static X509Certificate2 EnsureServerCertificate(IEnumerable<string> ips, Action<string>? log = null)
    {
        Directory.CreateDirectory(CertDir);
        using var ca = LoadOrCreateCa(log);
        // 保证 ca.crt 始终存在（手机下载用）
        File.WriteAllBytes(CaCrtPath, ca.Export(X509ContentType.Cert));

        var san = ips.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        var existing = LoadServerIfExists(ServerPfxPath);
        if (existing != null
            && existing.NotAfter > DateTime.Now.AddDays(30)
            && File.Exists(ServerSanPath)
            && File.ReadAllText(ServerSanPath) == string.Join(",", san))
        {
            return existing;
        }

        log?.Invoke(Strings.LogCertIssued(string.Join(", ", san)));
        var server = IssueServerCertificate(ca, san);
        File.WriteAllBytes(ServerPfxPath, server.Export(X509ContentType.Pfx));
        File.WriteAllText(ServerSanPath, string.Join(",", san));
        server.Dispose();
        // 统一从落盘的 PFX 重新载入：返回的证书私钥可被 SChannel 使用（见 ServerKeyFlags）
        return LoadServerWithPrivateKey(ServerPfxPath);
    }

    private static X509Certificate2 LoadOrCreateCa(Action<string>? log)
    {
        var existing = LoadFromPfx(CaPfxPath, CaKeyFlags);
        if (existing != null) return existing;

        log?.Invoke(Strings.LogCaCreated);
        using var key = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=LanMic Local Root CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        using var ca = req.CreateSelfSigned(
            DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(10));
        var pfx = ca.Export(X509ContentType.Pfx);
        File.WriteAllBytes(CaPfxPath, pfx);
        return new X509Certificate2(pfx, (string?)null, CaKeyFlags);
    }

    private static X509Certificate2 IssueServerCertificate(X509Certificate2 ca, List<string> ips)
    {
        using var key = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=LanMic PC", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true)); // serverAuth

        var sanBuilder = new SubjectAlternativeNameBuilder();
        foreach (var ip in ips)
            sanBuilder.AddIpAddress(IPAddress.Parse(ip));
        sanBuilder.AddDnsName("localhost");
        req.CertificateExtensions.Add(sanBuilder.Build());

        using var signed = req.Create(
            ca, DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(2),
            RandomNumberGenerator.GetBytes(16));
        // Create() 返回值不含私钥，与内存中的密钥合并。
        // 注意：这里得到的是临时密钥，调用方必须再经 PFX 落盘 + LoadServerWithPrivateKey 重载，
        // 否则 Kestrel 能启动但 TLS 握手会失败。
        return signed.CopyWithPrivateKey(key);
    }

    /// <summary>载入服务器证书，私钥持久化到用户密钥容器，确保 SChannel 可用于 TLS。</summary>
    private static X509Certificate2 LoadServerWithPrivateKey(string path)
        => LoadFromPfx(path, ServerKeyFlags)!;

    private static X509Certificate2? LoadServerIfExists(string path)
        => LoadFromPfx(path, ServerKeyFlags);

    /// <summary>从 PFX 载入证书（含私钥）。显式传 byte[] 以避开 X509Certificate2(string) 的 NTE_BAD_KEY_STATE。</summary>
    private static X509Certificate2? LoadFromPfx(string path, X509KeyStorageFlags flags)
        => File.Exists(path) ? new X509Certificate2(File.ReadAllBytes(path), (string?)null, flags) : null;
}
