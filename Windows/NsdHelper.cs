using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LocalShareWindows;

/// <summary>
/// A lightweight mDNS (Multicast DNS) helper for discovering and advertising LocalShare services.
/// Optimized for interoperability with Android's NsdManager.
/// </summary>
public class NsdHelper
{
    private const string ServiceType = "_localshare._tcp.local";
    private readonly string _instanceName;
    private readonly string _serviceFullName;

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    public Dictionary<string, string> DiscoveredPeers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public NsdHelper()
    {
        _instanceName = $"{Environment.MachineName}".ToLowerInvariant();
        _serviceFullName = $"{_instanceName}.{ServiceType}";
    }

    public void Start(int port)
    {
        if (_cts != null) Stop();

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
            _udp.JoinMulticastGroup(IPAddress.Parse("224.0.0.251"));

            Task.Run(() => ListenLoop(port, token), token);
            Task.Run(() => AnnounceLoop(port, token), token);
            Task.Run(() => QueryLoop(token), token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NSD Start failed: {ex.Message}");
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _udp?.Close();
        _udp = null;
    }

    private async Task ListenLoop(int port, CancellationToken token)
    {
        while (!token.IsCancellationRequested && _udp != null)
        {
            try
            {
                var result = await _udp.ReceiveAsync(token);
                var data = result.Buffer;

                if (data.Length < 12) continue;
                bool isQuery = (data[2] & 0x80) == 0;

                if (isQuery)
                {
                    HandleQuery(data, result.RemoteEndPoint, port);
                }
                else
                {
                    ParseResponse(data, result.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private void HandleQuery(byte[] data, IPEndPoint remote, int port)
    {
        string packetStr = Encoding.UTF8.GetString(data);
        if (packetStr.Contains("_localshare"))
        {
            // If anyone asks for _localshare, we send a full response with PTR, SRV, A, and TXT
            SendResponse(port);
        }
    }

    private void ParseResponse(byte[] data, IPEndPoint remote)
    {
        try
        {
            string packetStr = Encoding.UTF8.GetString(data);
            if (packetStr.Contains("_localshare"))
            {
                var name = ExtractInstanceName(data) ?? remote.Address.ToString();
                if (name.Equals(_instanceName, StringComparison.OrdinalIgnoreCase)) return;

                // Try to find port in the packet. Look for SRV record (Type 33)
                int port = 8080;
                // In binary: 00 21 (33). We look for this sequence.
                int srvIdx = IndexOf(data, new byte[] { 0, 0x21 });
                if (srvIdx != -1 && srvIdx + 10 < data.Length)
                {
                    // Very crude: port is 2 bytes starting 8 bytes after the type if standard format
                    port = (data[srvIdx + 8] << 8) | data[srvIdx + 9];
                    if (port < 1024 || port > 65535) port = 8080;
                }

                lock (DiscoveredPeers)
                {
                    DiscoveredPeers[name] = $"http://{remote.Address}:{port}";
                }
            }
        }
        catch { }
    }

    private async Task AnnounceLoop(int port, CancellationToken token)
    {
        while (!token.IsCancellationRequested && _udp != null)
        {
            SendResponse(port);
            await Task.Delay(30000, token);
        }
    }

    private void SendResponse(int port)
    {
        try
        {
            var packet = BuildResponsePacket(port);
            _udp?.Send(packet, packet.Length, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353));
        }
        catch { }
    }

    private async Task QueryLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _udp != null)
        {
            try
            {
                var packet = BuildQueryPacket();
                await _udp.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353));
            }
            catch { }
            await Task.Delay(15000, token);
        }
    }

    private byte[] BuildQueryPacket()
    {
        var packet = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        AddName(packet, ServiceType);
        packet.AddRange(new byte[] { 0, 12, 0, 1 }); // PTR, IN
        return packet.ToArray();
    }

    private byte[] BuildResponsePacket(int port)
    {
        // Header: Response, Authoritative, 1 Answer, 3 Additional
        var packet = new List<byte> { 0, 0, 0x84, 0, 0, 1, 0, 0, 0, 0, 0, 3 };

        // 1. PTR Answer: _localshare._tcp.local -> InstanceName._localshare._tcp.local
        AddName(packet, ServiceType);
        packet.AddRange(new byte[] { 0, 12, 0, 1, 0, 0, 0, 120 }); // PTR, IN, TTL 120
        var ptrData = new List<byte>();
        AddName(ptrData, _serviceFullName);
        packet.Add((byte)(ptrData.Count >> 8));
        packet.Add((byte)(ptrData.Count & 0xFF));
        packet.AddRange(ptrData);

        // Additional Records (simplified)
        // 2. SRV: InstanceName._localshare._tcp.local -> Port, Target(InstanceName.local)
        AddName(packet, _serviceFullName);
        packet.AddRange(new byte[] { 0, 33, 0, 1, 0, 0, 0, 120 }); // SRV, IN, TTL 120
        var srvData = new List<byte> { 0, 0, 0, 0 }; // Priority 0, Weight 0
        srvData.Add((byte)(port >> 8));
        srvData.Add((byte)(port & 0xFF));
        AddName(srvData, $"{_instanceName}.local");
        packet.Add((byte)(srvData.Count >> 8));
        packet.Add((byte)(srvData.Count & 0xFF));
        packet.AddRange(srvData);

        // 3. TXT: InstanceName._localshare._tcp.local -> "ver=1"
        AddName(packet, _serviceFullName);
        packet.AddRange(new byte[] { 0, 16, 0, 1, 0, 0, 0, 120 }); // TXT, IN, TTL 120
        var txtData = Encoding.UTF8.GetBytes("\x05ver=1");
        packet.Add((byte)(txtData.Length >> 8));
        packet.Add((byte)(txtData.Length & 0xFF));
        packet.AddRange(txtData);

        // 4. A: InstanceName.local -> IP
        AddName(packet, $"{_instanceName}.local");
        packet.AddRange(new byte[] { 0, 1, 0, 1, 0, 0, 0, 120, 0, 4 }); // A, IN, TTL 120, Len 4
        var ip = NetUtils.GetLocalIpAddresses().FirstOrDefault() ?? "127.0.0.1";
        packet.AddRange(IPAddress.Parse(ip).GetAddressBytes());

        return packet.ToArray();
    }

    private void AddName(List<byte> packet, string name)
    {
        foreach (var part in name.Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(part);
            packet.Add((byte)bytes.Length);
            packet.AddRange(bytes);
        }
        packet.Add(0);
    }

    private string? ExtractInstanceName(byte[] data)
    {
        int index = IndexOf(data, Encoding.UTF8.GetBytes("_localshare"));
        if (index > 1)
        {
            int len = data[index - 1];
            if (index - 1 - len >= 0) return Encoding.UTF8.GetString(data, index - 1 - len, len);
        }
        return null;
    }

    private int IndexOf(byte[] data, byte[] pattern)
    {
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}
