using ECommons;
using ECommons.DalamudServices;
using System.IO.Pipes;
using AutoDuty.Logging;

namespace AutoDuty.Multibox
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class NamedPipeTransport(string pipeName, string serverName = ".") : ITransport
    {
        private readonly List<NamedPipeServerStream> availablePipes = [];
        private readonly List<Task>                  connectTasks   = [];
        private readonly List<NamedPipeServerStream> usedPipes      = [];
        private          CancellationTokenSource?    cts;
        private          bool                        isRunning = false;
        private          int                         maxInstances;

        public void StartServer(int backlog = MultiboxUtility.Server.MAX_SERVERS)
        {
            if (this.isRunning)
                return;
            this.isRunning    = true;
            this.maxInstances = backlog;
            this.cts          = new CancellationTokenSource();

            for (int i = 0; i < backlog; i++)
            {
                NamedPipeServerStream pipe = new(pipeName, PipeDirection.InOut, this.maxInstances, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                this.availablePipes.Add(pipe);
                this.connectTasks.Add(pipe.WaitForConnectionAsync(this.cts.Token));
            }
        }

        public void StopServer()
        {
            this.isRunning = false;
            this.cts?.Cancel();
            Task.WaitAll(this.connectTasks);
            this.connectTasks.Clear();
            this.availablePipes.ForEach(pipe => pipe?.Dispose());
            this.availablePipes.Clear();
            this.usedPipes.ForEach(pipe => pipe?.Dispose());
            this.usedPipes.Clear();
            this.cts?.Dispose();
            this.cts = null;
        }

        public async Task<Stream> AcceptConnectionAsync(CancellationToken ct)
        {
            if (!this.isRunning)
                throw new InvalidOperationException("Server not started");

            if (this.availablePipes.Count == 0)
            {
                await Task.Run(async () =>
                               {
                                   while (this.usedPipes.All(pipe => pipe.IsConnected))
                                   {
                                       await Task.Delay(100, ct);
                                       ct.ThrowIfCancellationRequested();
                                   }
                               }, cancellationToken: ct);
                ct.ThrowIfCancellationRequested();
                List<NamedPipeServerStream> closedPipes = [..this.usedPipes.Where(pipe => !pipe.IsConnected)];

                closedPipes.Each(pipe =>
                                 {
                                     pipe.Dispose();
                                     this.usedPipes.Remove(pipe);
                                     NamedPipeServerStream newPipe = new(pipeName, PipeDirection.InOut, this.maxInstances, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                                     this.availablePipes.Add(newPipe);
                                     this.connectTasks.Add(newPipe.WaitForConnectionAsync(this.cts!.Token));
                                 });
            }

            Task? connectedTask;
            await using (ct.Register(() => this.cts?.Cancel()))
                connectedTask = await Task.WhenAny(this.connectTasks);

            ct.ThrowIfCancellationRequested();
            int index = this.connectTasks.IndexOf(connectedTask);
            this.connectTasks.RemoveAt(index);
            NamedPipeServerStream pipe = this.availablePipes[index];
            this.availablePipes.RemoveAt(index);
            this.usedPipes.Add(pipe);
            ct.Register(pipe.Dispose);
            return pipe;
        }

        public async Task<Stream> ConnectToServerAsync(CancellationToken ct)
        {
            // For named pipes, we use the configured serverName and pipeName
            NamedPipeClientStream client = new(serverName, pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            Task connectTask = client.ConnectAsync(ct);
            await using (ct.Register(() =>
                                     {
                                         try
                                         {
                                             client.Close();
                                         }
                                         catch (Exception ex)
                                         {
                                             DebugLog("Error during pipe closure: " + ex);
                                         }
                                     }))
                await connectTask;

            ct.Register(client.Dispose);
            return client;
        }

        public void Dispose() => 
            this.StopServer();

        private static void DebugLog(string message) => 
            ADLogging.Debug($"Pipe: {message}");

        private static void ErrorLog(string message) => 
            ADLogging.Error($"Pipe: {message}");
    }
}