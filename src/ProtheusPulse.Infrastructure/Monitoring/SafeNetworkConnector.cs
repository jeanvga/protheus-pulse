using System.Net;
using System.Net.Sockets;

namespace ProtheusPulse.Infrastructure.Monitoring;

internal static class SafeNetworkConnector
{
    public static async Task<Stream> ConnectStreamAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var allowed = addresses.Where(IsAllowed).ToArray();
        if (allowed.Length == 0)
        {
            throw new InvalidOperationException("O destino resolveu apenas para endereços bloqueados.");
        }

        Exception? lastError = null;
        foreach (var address in allowed)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                lastError = exception;
                if (exception is OperationCanceledException)
                {
                    throw;
                }
            }
        }

        throw new IOException("Não foi possível conectar ao destino configurado.", lastError);
    }

    public static bool IsAllowed(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast)
        {
            return false;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] != 0
            && !(bytes[0] == 169 && bytes[1] == 254)
            && bytes[0] < 224
            && !(bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255);
    }
}
