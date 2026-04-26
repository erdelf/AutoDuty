using global::AutoDuty.Managers;

namespace AutoDuty.Multibox;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.PartyFunctions;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Helpers;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;

public static class MultiboxUtility
{
    [JsonObject(MemberSerialization.OptOut)]
    public class MultiboxConfiguration
    {
        [JsonIgnore]
        [field: JsonIgnore]
        public bool MultiBox
        {
            get;
            set
            {
                if (field == value)
                    return;
                field = value;

                MultiboxUtility.Set(field);
            }
        } = false;

        internal bool          SynchronizePath { get; set; } = true;
        public   bool          Host            { get; set; } = false;
        public   string        PipeName        { get; set; } = "AutoDutyPipe";
        public   string        ServerName      { get; set; } = ".";
        public   TransportType TransportType   { get; set; } = TransportType.NamedPipe;
        public   string        ServerAddress   { get; set; } = "127.0.0.1";
        public   int           ServerPort      { get; set; } = 1716;
    }

    public static MultiboxConfiguration Config => ConfigurationMain.Instance.multibox;

    private const string SERVER_AUTH_KEY = "AD_Server_Auth!";
    private const string CLIENT_AUTH_KEY = "AD_Client_Auth!";
    private const string CLIENT_CID_KEY  = "CLIENT_CID";
    private const string PARTY_INVITE    = "PARTY_INVITE";

    private const string KEEPALIVE_KEY          = "KEEP_ALIVE";
    private const string KEEPALIVE_RESPONSE_KEY = "KEEP_ALIVE received";

    private const string DUTY_QUEUE_KEY = "DUTY_QUEUE";
    private const string DUTY_EXIT_KEY  = "DUTY_EXIT";

    private const string DEATH_KEY       = "DEATH";
    private const string UNDEATH_KEY     = "UNDEATH";
    private const string DEATH_RESET_KEY = "DEATH_RESET";

    private const string PATH_STEPS = "PATH_STEPS";

    private const string STEP_COMPLETED = "STEP_COMPLETED";
    private const string STEP_START     = "STEP_START";

    internal static bool stepBlock = false;
    public static bool MultiboxBlockingNextStep
    {
        get
        {
            if (!Config.MultiBox)
                return false;

            return stepBlock;
        }
        set
        {
            DebugLog($"blocking step: {stepBlock} to {value}");
            if (!Config.MultiBox)
                return;

            if (!value)
                if (Config.Host)
                    Server.SendStepStart();

            if (stepBlock == value)
                return;

            stepBlock = value;

            if(stepBlock)
                if (Config.Host)   
                {
                    Plugin.action = Loc.Get("Overlay.Actions.WaitingForClients");
                    Server.CheckStepProgress();
                }
                else
                {
                    Client.SendStepCompleted();
                }
        }
    }

    public static void IsDead(bool dead)
    {
        if (Config.MultiBox)
            return;

        if(!Config.Host)
            Client.SendDeath(dead);
        else
            Server.CheckDeaths();
    }

    public static void Set(bool on)
    {
        if(on)
            ConfigurationMain.Instance.GetCurrentConfig.DutyModeEnum = DutyMode.Regular;

        if (Config.Host)
            Server.Set(on);
        else
            Client.Set(on);
    }

    internal static class Server
    {
        public const             int             MAX_SERVERS   = 3;
        private static readonly  StreamString?[] streams       = new StreamString?[MAX_SERVERS];
        internal static readonly ClientInfo?[]   clients       = new ClientInfo?[MAX_SERVERS];
        private static readonly  Queue<string>[] messageQueues = [new(), new(), new()];

        internal static readonly DateTime[] keepAlives    = new DateTime[MAX_SERVERS];
        internal static readonly bool[]     stepConfirms  = new bool[MAX_SERVERS];
        private static readonly  bool[]     deathConfirms = new bool[MAX_SERVERS];

        private static ITransport?              transport;
        private static CancellationTokenSource? serverCts;

        public static void Set(bool on)
        {
            try
            {
                if (on)
                    StartServer();
                else
                    StopServer();
            }
            catch (Exception ex)
            {
                ErrorLog(ex.ToString());
            }
        }

        private static void StartServer()
        {
            try
            {
                if (transport != null) return;
                    
                transport = Config.TransportType switch 
                {
                    TransportType.NamedPipe => new NamedPipeTransport(Config.PipeName),
                    TransportType.Tcp => new TcpTransport(Config.ServerPort),
                    _ => throw new NotImplementedException(Config.TransportType.ToString()),
                };

                transport.StartServer(MAX_SERVERS);
                serverCts = new CancellationTokenSource();
                Task.Run(() => AcceptLoop(serverCts.Token), serverCts.Token);
                DebugLog($"Server started with {Config.TransportType} transport");
            }
            catch (Exception ex)
            {
                ErrorLog($"StartServer error: {ex}");
            }
        }

        private static void StopServer()
        {
            try
            {
                serverCts?.Cancel();
                transport?.StopServer();
                transport?.Dispose();
                transport = null;
                serverCts = null;

                for (int i = 0; i < MAX_SERVERS; i++)
                {
                    streams[i] = null;
                    clients[i] = null;
                    messageQueues[i].Clear();
                    keepAlives[i]   = DateTime.MinValue;
                    stepConfirms[i] = false;
                }

                if (!InDungeon)
                {
                    Chat.ExecuteCommand("/partycmd breakup");

                    SchedulerHelper.ScheduleAction("MultiboxServer PartyBreakup Accept", () =>
                                                                                         {
                                                                                             unsafe
                                                                                             {
                                                                                                 Utf8String inviterName = InfoProxyPartyInvite.Instance()->InviterName;

                                                                                                 if (UniversalParty.Length <= 1)
                                                                                                 {
                                                                                                     SchedulerHelper.DescheduleAction("MultiboxServer PartyBreakup Accept");
                                                                                                     return;
                                                                                                 }

                                                                                                 if (GenericHelpers.TryGetAddonByName("SelectYesno", out AtkUnitBase* addonSelectYesno) &&
                                                                                                     GenericHelpers.IsAddonReady(addonSelectYesno))
                                                                                                 {
                                                                                                     AddonMaster.SelectYesno yesno = new(addonSelectYesno);
                                                                                                     if (yesno.Text.Contains(inviterName.ToString()))
                                                                                                         yesno.Yes();
                                                                                                     else
                                                                                                         yesno.No();
                                                                                                 }

                                                                                                 if (GenericHelpers.TryGetAddonByName("Social", out AtkUnitBase* addonSocial) &&
                                                                                                     GenericHelpers.IsAddonReady(addonSocial))
                                                                                                 {
                                                                                                     ErrorLog("/partycmd breakup opened the party menu instead");
                                                                                                     SchedulerHelper.DescheduleAction("MultiboxServer PartyBreakup Accept");
                                                                                                     return;
                                                                                                 }
                                                                                             }
                                                                                         }, 500, false);
                }

                DebugLog("Server stopped");
            }
            catch (Exception ex)
            {
                ErrorLog($"StopServer error: {ex}");
            }
        }

        private static async void AcceptLoop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    Stream s   = await transport!.AcceptConnectionAsync(ct);
                    int    idx = -1;
                    for (int i = 0; i < MAX_SERVERS; i++)
                    {
                        if (streams[i] == null)
                        {
                            idx = i;
                            break;
                        }
                    }
                    if (idx == -1)
                    {
                        try
                        {
                            await s.DisposeAsync();
                        }
                        catch (Exception ex)
                        {
                            ErrorLog(ex.ToString());
                        }
                        continue;
                    }

                    streams[idx] = new StreamString(s);

                    int capturedIdx = idx;
                    _ = Task.Run(() => ConnectionHandler(s, capturedIdx, ct), ct);
                }
            }
            catch (OperationCanceledException)
            {
                DebugLog("AcceptLoop ended due to cancellation");
            }
            catch (Exception ex)
            {
                ErrorLog($"AcceptLoop error: {ex}");
            }
        }

        private static async void ConnectionHandler(Stream stream, int index, CancellationToken ct)
        {
            try
            {
                await using Stream s = stream;
                if (streams[index] == null)
                    return;
                StreamString ss = streams[index]!;
                ss.WriteString(SERVER_AUTH_KEY);
                if (ss.ReadString() != CLIENT_AUTH_KEY)
                    return;

                DebugLog($"Client {index} authenticated");
                keepAlives[index] = DateTime.Now;
                Task sendTask = Task.Run(async () => await ServerSendThread(index, ct), ct);

                while (!ct.IsCancellationRequested && !sendTask.IsCompleted)
                {
                    await Task.Delay(100, ct);
                    string   message = ss.ReadString().Trim();
                    string[] split   = message.Split("|");

                    switch (split[0])
                    {
                        case "" when message.Length == 0:
                            DebugLog($"Client {index} closed the connection.");
                            return;
                        case CLIENT_CID_KEY:
                            clients[index] = new ClientInfo(ulong.Parse(split[1]), split[2], ushort.Parse(split[3]));

                            _ = Svc.Framework.RunOnTick(() =>
                                                        {
                                                            unsafe
                                                            {
                                                                ClientInfo client = clients[index]!;
                                                                DebugLog($"Client Identification received: {client.CID} {client.CName} {client.WorldId}");

                                                                if (!PartyHelper.IsPartyMember(client.CID))
                                                                {
                                                                    if (client.WorldId == Player.CurrentWorld.RowId)
                                                                        InfoProxyPartyInvite.Instance()->InviteToParty(client.CID, client.CName, client.WorldId);
                                                                    else
                                                                        InfoProxyPartyInvite.Instance()->InviteToPartyContentId(client.CID, 0);

                                                                    ss.WriteString(PARTY_INVITE);
                                                                }
                                                                stepConfirms[index] = false;
                                                            }
                                                        }, cancellationToken: ct);
                            break;
                        case KEEPALIVE_KEY:
                            ss.WriteString(KEEPALIVE_RESPONSE_KEY);
                            break;
                        case KEEPALIVE_RESPONSE_KEY:
                            break;
                        case STEP_COMPLETED:
                            stepConfirms[index] = true;
                            CheckStepProgress();
                            break;
                        case DEATH_KEY:
                            deathConfirms[index] = true;
                            CheckDeaths();
                            break;
                        case UNDEATH_KEY:
                            deathConfirms[index] = false;
                            break;
                        default:
                            ss.WriteString($"Unknown Message from {index}: {message}");
                            continue;
                    }
                    keepAlives[index] = DateTime.Now;
                }
            }
            catch (OperationCanceledException)
            {
                DebugLog($"Connection handler ended due to cancellation {index}");
            }
            catch (Exception e)
            {
                ErrorLog($"ConnectionHandler error {index}: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                streams[index] = null;
                clients[index] = null;
            }
        }

        private static async Task ServerSendThread(int index, CancellationToken ct)
        {
            try
            {
                DebugLog("SEND Initialized with " + index);

                while (!ct.IsCancellationRequested && streams[index] != null)
                {
                    if (messageQueues[index].Count > 0)
                    {
                        string message = messageQueues[index].Dequeue();
                        streams[index]?.WriteString(message);
                    } 
                    else if ((DateTime.Now - keepAlives[index]).TotalSeconds > 15)
                    {
                        // if no messages to send and the connection is stale, send a keepalive to check. (Usually this is the clients job but the tcp socket doesn't die immediately)
                        streams[index]?.WriteString(KEEPALIVE_KEY);
                        await Task.Delay(1000, ct);
                    }
                    await Task.Delay(100, ct);
                }
            }
            catch (OperationCanceledException)
            {
                DebugLog($"SendLoop ended due to cancellation for {index}");
            }
            catch (Exception e)
            {
                ErrorLog($"SERVER SEND ERROR for {index}: " + e);
            }
        }

        public static bool AllInParty()
        {
            for (int i = 0; i < MAX_SERVERS; i++)
            {
                if (clients[i] == null || !PartyHelper.IsPartyMember(clients[i]!.CID))
                    return false;
            }

            return true;
        }

        public static void CheckDeaths()
        {
            if (deathConfirms.All(x => x) && Player.IsDead)
            {
                for (int i = 0; i < deathConfirms.Length; i++)
                    deathConfirms[i] = false;

                DebugLog("All dead");
                SendToAllClients(DEATH_RESET_KEY);
            }
            else
            {
                DebugLog("Not all clients are dead yet, waiting for more death.");
            }
        }

        public static void CheckStepProgress()
        {
            if((Plugin.Stage != Stage.Looping && Plugin.indexer >= 0 && Plugin.indexer < Plugin.Actions.Count && Plugin.Actions[Plugin.indexer].Tag == ActionTag.Treasure || stepConfirms.All(x => x)) && stepBlock)
            {
                for (int i = 0; i < stepConfirms.Length; i++)
                    stepConfirms[i] = false;

                DebugLog("All clients completed the step");
                stepBlock = false;
            }
            else
            {
                DebugLog("Not all clients have completed the step yet, waiting for more confirmations.");
            }
        }

        public static void SendStepStart()
        {
            DebugLog("Synchronizing Clients to Server step");
            SendToAllClients($"{STEP_START}|{Plugin.indexer}");
        }

        public static void ExitDuty()
        {
            DebugLog("exiting duty");
            SendToAllClients(DUTY_EXIT_KEY);
            for (int i = 0; i < stepConfirms.Length; i++)
                stepConfirms[i] = false;
        }

        public static void Queue()
        {
            DebugLog("Queue initiated");
            SendToAllClients(DUTY_QUEUE_KEY);
            for (int i = 0; i < stepConfirms.Length; i++)
                stepConfirms[i] = false;
            stepBlock = false;
        }

        public static void SendPath() => 
            SendToAllClients($"{PATH_STEPS}|{JsonConvert.SerializeObject(Plugin.Actions, ConfigurationMain.JsonSerializerSettings)}");

        private static void SendToAllClients(string message)
        {
            DebugLog("Enqueuing to send: " + message);
            foreach (Queue<string> queue in messageQueues)
                queue.Enqueue(message);
        }

        internal record ClientInfo(ulong CID, string CName, ushort WorldId)
        {
            public string World => field ??= Svc.Data.Excel.GetSheet<World>().GetRow(this.WorldId).Name.GetText();
        }
    }

    internal static class Client
    {
        private static StreamString?            clientSS;
        private static CancellationTokenSource? clientCts;

        public static void Set(bool on)
        {
            if (on)
            {
                clientCts = new CancellationTokenSource();
                Task.Run(() => ClientConnectionThread(clientCts.Token), clientCts.Token);
            }
            else
            {
                try
                {
                    clientCts?.Cancel();
                }
                catch (Exception ex)
                {
                    ErrorLog(ex.ToString());
                }
                clientSS  = null;
                clientCts = null;
            }
        }

        private static async void ClientConnectionThread(CancellationToken ct)
        {
            try
            {
                using ITransport transport = Config.TransportType switch 
                {
                    TransportType.NamedPipe => new NamedPipeTransport(Config.PipeName, Config.ServerName),
                    TransportType.Tcp => new TcpTransport(Config.ServerAddress, Config.ServerPort),
                    _ => throw new NotImplementedException(Config.TransportType.ToString()),
                };

                DebugLog($"Connecting to server ({Config.TransportType})...\n");
                await using Stream clientStream = await transport.ConnectToServerAsync(ct);

                clientSS = new StreamString(clientStream);

                if (clientSS.ReadString() == SERVER_AUTH_KEY)
                {
                    clientSS.WriteString(CLIENT_AUTH_KEY);

                    _ = Svc.Framework.RunOnTick(() =>
                                                {
                                                    if (Player.CID != 0)
                                                        clientSS.WriteString($"{CLIENT_CID_KEY}|{Player.CID}|{Player.Name}|{Player.CurrentWorld.RowId}");
                                                }, cancellationToken: ct);

                    _ = Task.Run(() => ClientKeepAliveThread(ct), ct);
                    while (!ct.IsCancellationRequested)
                    {
                        string   message = clientSS.ReadString().Trim();
                        string[] split   = message.Split("|");

                        switch (split[0])
                        {
                            case "" when message.Length == 0:
                                DebugLog("Server closed the connection.");
                                return;
                            case STEP_START:
                                if (int.TryParse(split[1], out int step))
                                {
                                    Plugin.indexer = step;
                                    stepBlock      = false;
                                    Plugin.Stage   = Stage.Idle;
                                    Plugin.Stage   = Stage.Reading_Path;
                                }
                                break;
                            case KEEPALIVE_KEY:
                                clientSS.WriteString(KEEPALIVE_RESPONSE_KEY);
                                break;
                            case KEEPALIVE_RESPONSE_KEY:
                                break;
                            case DUTY_QUEUE_KEY:
                                QueueHelper.InvokeAcceptOnly();
                                break;
                            case DUTY_EXIT_KEY:
                                stepBlock = false;
                                ExitDutyHelper.Invoke();
                                break;
                            case PARTY_INVITE:
                                SchedulerHelper.ScheduleAction("MultiboxClient PartyInvite Accept", () =>
                                                                                                    {
                                                                                                        unsafe
                                                                                                        {
                                                                                                            if(UniversalParty.Length > 1)
                                                                                                            {
                                                                                                                PartyHelper.LeaveParty();
                                                                                                                return;
                                                                                                            }

                                                                                                            Utf8String inviterName = InfoProxyPartyInvite.Instance()->InviterName;
                                                                                                            if (InfoProxyPartyInvite.Instance()->InviterWorldId != 0                               && 
                                                                                                                UniversalParty.Length                           <= 1                               &&
                                                                                                                GenericHelpers.TryGetAddonByName("SelectYesno", out AtkUnitBase* addonSelectYesno) &&
                                                                                                                GenericHelpers.IsAddonReady(addonSelectYesno))
                                                                                                            {
                                                                                                                AddonMaster.SelectYesno yesno = new(addonSelectYesno);
                                                                                                                if (yesno.Text.Contains(inviterName.ToString()))
                                                                                                                {
                                                                                                                    yesno.Yes();
                                                                                                                    SchedulerHelper.DescheduleAction("MultiboxClient PartyInvite Accept");
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    yesno.No();
                                                                                                                }
                                                                                                            }
                                                                                                        }
                                                                                                    }, 500, false);
                                break;
                            case PATH_STEPS:
                                List<PathAction>? steps = JsonConvert.DeserializeObject<List<PathAction>>(message[(split[0].Length+1)..], ConfigurationMain.JsonSerializerSettings);
                                if (steps is { Count: > 0 })
                                {
                                    DebugLog("setting steps from host");
                                    Plugin.Actions = steps;
                                }
                                break;
                            default:
                                ErrorLog("Unknown response: " + message);
                                break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                DebugLog("ClientConnection ended due to cancellation");
            }
            catch (Exception e)
            {
                ErrorLog($"Client ERROR: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                Config.MultiBox = false;
            }
        }

        private static async void ClientKeepAliveThread(CancellationToken ct)
        {
            try
            {
                await Task.Delay(1000, ct);
                while (!ct.IsCancellationRequested && clientSS != null)
                {
                    clientSS?.WriteString(KEEPALIVE_KEY);
                    await Task.Delay(10000, ct);
                }
            }
            catch (OperationCanceledException)
            {
                DebugLog("ClientKeepalive ended due to cancellation");
            }
            catch (Exception e)
            {
                ErrorLog("Client KEEPALIVE Error: " + e);
            }
        }

        public static void SendStepCompleted()
        {
            if (clientSS == null)
            {
                DebugLog("Client not connected, cannot send step completed.");
                return;
            }
            Plugin.action = Loc.Get("Overlay.Actions.WaitingForOthers");
            clientSS.WriteString(STEP_COMPLETED);
            DebugLog("Step completed sent to server.");
        }

        public static void SendDeath(bool dead)
        {
            if (clientSS == null)
            {
                DebugLog("Client not connected, cannot send death.");
                return;
            }
            clientSS.WriteString(dead ? DEATH_KEY : UNDEATH_KEY);
            DebugLog("Death sent to server.");
        }
    }


    private static void DebugLog(string message) => 
        Svc.Log.Debug($"Pipe Connection: {message}");

    private static void ErrorLog(string message) => 
        Svc.Log.Error($"Pipe Connection: {message}");

    private class StreamString(Stream ioStream)
    {
        private readonly UnicodeEncoding streamEncoding = new();

        public string ReadString()
        {
            int b1 = ioStream.ReadByte();
            int b2 = ioStream.ReadByte();

            if (b1 == -1 || b2 == -1)
            {
                DebugLog("End of stream reached.");
                return string.Empty;
            }

            int    len      = b1 * 256 + b2;
            byte[] inBuffer = new byte[len];
            int    n        = 0;
            while (n < len)
            {
                int c = ioStream.Read(inBuffer, n, len-n);
                if (c == 0)
                {
                    ErrorLog("Stream closed unexpectedly");
                    return string.Empty;
                }
                n += c;
            }

            string readString = this.streamEncoding.GetString(inBuffer);

            DebugLog("Reading: " + readString);
            return readString;
        }

        public int WriteString(string outString)
        {
            DebugLog("Writing: " + outString);

            byte[] outBuffer = this.streamEncoding.GetBytes(outString);
            int    len       = outBuffer.Length;
            if (len > ushort.MaxValue)
                throw new ArgumentException("String too long to write to stream");
            ioStream.WriteByte((byte)(len / 256));
            ioStream.WriteByte((byte)(len & 255));
            ioStream.Write(outBuffer, 0, len);
            ioStream.Flush();

            return outBuffer.Length + 2;
        }
    }
}