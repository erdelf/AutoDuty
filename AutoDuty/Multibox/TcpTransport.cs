using System.Net;
using System.Net.Sockets;
using AutoDuty.Logging;

namespace AutoDuty.Multibox
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using ECommons.DalamudServices;

    public sealed class TcpTransport : ITransport
    {
        private readonly IPAddress    address;
        private readonly int          port;
        private          TcpListener? listener;

        public TcpTransport(string address, int port)
        {
            this.address = IPAddress.Parse(address);
            this.port    = port;
        }
        public TcpTransport(int port)
        {
            this.address = IPAddress.Any;
            this.port    = port;
        }

        public void StartServer(int backlog = MultiboxUtility.Server.MAX_SERVERS)
        {
            if (this.listener != null) 
                return;
            this.listener = new TcpListener(this.address, this.port);
            this.listener.Start(backlog);
        }

        public void StopServer()
        {
            try
            {
                this.listener?.Stop();
            }
            catch (Exception ex)
            {
                DebugLog("Error during tcp socket closure: " + ex);
            }

            this.listener = null;
        }

        public async Task<Stream> AcceptConnectionAsync(CancellationToken ct)
        {
            if (this.listener == null) 
                throw new InvalidOperationException("Listener not started");
            TcpClient client = await this.listener.AcceptTcpClientAsync(ct);
            client.NoDelay = true;
            ct.Register(client.Dispose);
            return client.GetStream();
        }

        public async Task<Stream> ConnectToServerAsync(CancellationToken ct)
        {
            TcpClient client      = new();
            ValueTask connectTask = client.ConnectAsync(this.address, this.port, ct);
            await using (ct.Register(() =>
                                     {
                                         try
                                         {
                                             client.Close();
                                         }
                                         catch (Exception ex)
                                         {
                                             DebugLog("Error during tcp socket closure on cancel: " + ex);
                                         }
                                     }))
                await connectTask;

            client.NoDelay = true;
            ct.Register(client.Dispose);
            return client.GetStream();
        }

        public void Dispose() => 
            this.StopServer();

        private static void DebugLog(string message) => 
            ADLogging.Debug($"TCP Connection: {message}");

        private static void ErrorLog(string message) => 
            ADLogging.Error($"TCP Connection: {message}");
    }
}
