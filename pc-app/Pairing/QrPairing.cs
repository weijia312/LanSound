using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using QRCoder;

namespace pc_app.Pairing;

public static class QrPairing
{
    /// <summary>
    /// 枚举本机所有可用网卡的 IPv4 地址（排除未启用/回环/169.254 链路本地地址）。
    /// 自动探测的出口 IP 排在第一位作为默认选项。
    /// </summary>
    public static List<string> GetLanIps()
    {
        var ips = new List<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var s = ua.Address.ToString();
                if (s.StartsWith("169.254.")) continue;
                if (!ips.Contains(s)) ips.Add(s);
            }
        }

        var preferred = GetLanIp();
        if (ips.Remove(preferred))
            ips.Insert(0, preferred);
        else if (!ips.Contains(preferred))
            ips.Insert(0, preferred);
        return ips;
    }

    /// <summary>
    /// 获取系统默认出口的局域网 IPv4 地址。
    /// 通过 UDP "伪连接" 让系统选定出口网卡（不产生实际流量），失败时回退到 DNS 枚举。
    /// </summary>
    public static string GetLanIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 80);
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch
        {
            var addr = Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return addr?.ToString() ?? "127.0.0.1";
        }
    }

    /// <summary>生成二维码 PNG 字节（pixelsPerModule 越大分辨率越高，缩放显示越清晰）。</summary>
    public static byte[] GeneratePng(string content, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }
}
