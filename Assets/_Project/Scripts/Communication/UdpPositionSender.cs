using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using IndustrialConveyor.Simulation.Movement;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Communication
{
    /// <summary>User-editable UDP sender configuration, persisted in PlayerPrefs.</summary>
    public sealed class UdpSettings
    {
        private const string Prefix = "IndustrialConveyor.Udp.";

        public string LocalIp = "0.0.0.0";
        public string DestinationIp = "127.0.0.1";
        public int DestinationPort = 5000;
        public int IntervalMs = 100;

        public static UdpSettings Load()
        {
            return new UdpSettings
            {
                LocalIp = PlayerPrefs.GetString(Prefix + "LocalIp", "0.0.0.0"),
                DestinationIp = PlayerPrefs.GetString(Prefix + "DestinationIp", "127.0.0.1"),
                DestinationPort = PlayerPrefs.GetInt(Prefix + "DestinationPort", 5000),
                IntervalMs = PlayerPrefs.GetInt(Prefix + "IntervalMs", 100)
            };
        }

        public void Save()
        {
            PlayerPrefs.SetString(Prefix + "LocalIp", LocalIp);
            PlayerPrefs.SetString(Prefix + "DestinationIp", DestinationIp);
            PlayerPrefs.SetInt(Prefix + "DestinationPort", DestinationPort);
            PlayerPrefs.SetInt(Prefix + "IntervalMs", IntervalMs);
            PlayerPrefs.Save();
        }
    }

    /// <summary>One IPv4 address of an active network adapter.</summary>
    public readonly struct NetworkAdapterInfo
    {
        public NetworkAdapterInfo(string name, string address)
        {
            Name = name;
            Address = address;
        }

        public string Name { get; }
        public string Address { get; }
        public string Label => $"{Name}  -  {Address}";
    }

    /// <summary>Lists the IPv4 addresses the simulation can send from.</summary>
    public static class NetworkAdapters
    {
        public static List<NetworkAdapterInfo> ListIPv4()
        {
            var result = new List<NetworkAdapterInfo> { new NetworkAdapterInfo("Any adapter (OS routing)", "0.0.0.0") };
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up)
                        continue;
                    foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                            result.Add(new NetworkAdapterInfo(adapter.Name, unicast.Address.ToString()));
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not enumerate network adapters: {exception.Message}");
            }
            return result;
        }
    }

    /// <summary>
    /// Sends <see cref="PositionTelegram"/> packets on a dedicated thread. The telegram is refreshed from the
    /// simulation every frame; the thread resends the latest snapshot at the configured interval.
    /// </summary>
    public sealed class UdpPositionSender : MonoBehaviour
    {
        public const int MinimumIntervalMs = 10;
        public const int MaximumIntervalMs = 2000;

        private readonly object gate = new object();
        private readonly byte[] latest = new byte[PositionTelegram.TotalSize];
        private TrolleySimulationManager simulationManager;
        private Thread worker;
        private Socket socket;
        private volatile bool running;
        private volatile string lastError;
        private bool hasData;
        private long packetsSent;

        public bool IsRunning => running;
        public long PacketsSent => Interlocked.Read(ref packetsSent);
        public string LastError => lastError;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint milliseconds);
#endif

        private void Awake()
        {
            simulationManager = GetComponent<TrolleySimulationManager>();
        }

        private void Update()
        {
            if (!running || simulationManager == null)
                return;

            var trackLengthMm = simulationManager.TrackPath != null
                ? (uint)Mathf.Max(0, Mathf.RoundToInt(simulationManager.TrackPath.TotalLengthMeters * 1000f))
                : 0u;
            var simulationTimeMs = (uint)Mathf.Max(0, Mathf.RoundToInt(simulationManager.SimulationTimeSeconds * 1000f));
            lock (gate)
            {
                PositionTelegram.Write(latest, simulationTimeMs, trackLengthMm, simulationManager.Trolleys);
                hasData = true;
            }
        }

        private void OnDestroy()
        {
            Stop();
        }

        /// <summary>Validates the settings, binds the socket to the chosen local IP and starts sending.</summary>
        public void Begin(UdpSettings settings)
        {
            Stop();
            if (!IPAddress.TryParse(settings.LocalIp, out var localAddress) ||
                localAddress.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("Simulation IP must be a valid IPv4 address.");
            if (!IPAddress.TryParse(settings.DestinationIp, out var destinationAddress) ||
                destinationAddress.AddressFamily != AddressFamily.InterNetwork)
                throw new ArgumentException("Destination IP must be a valid IPv4 address.");
            if (settings.DestinationPort < 1 || settings.DestinationPort > 65535)
                throw new ArgumentException("Port must be between 1 and 65535.");
            if (destinationAddress.Equals(IPAddress.Any))
                throw new ArgumentException("Destination IP cannot be 0.0.0.0; use e.g. 127.0.0.1 for this computer.");
            if (!localAddress.Equals(IPAddress.Any) && IPAddress.IsLoopback(localAddress) != IPAddress.IsLoopback(destinationAddress))
                throw new ArgumentException("Simulation IP and destination must both be loopback (127.x.x.x) or both on the network. " +
                                            "For this computer use Simulation IP 0.0.0.0 or 127.0.0.1 with destination 127.0.0.1.");
            if (settings.IntervalMs < MinimumIntervalMs || settings.IntervalMs > MaximumIntervalMs)
                throw new ArgumentException($"Interval must be between {MinimumIntervalMs} and {MaximumIntervalMs} ms.");

            var newSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                EnableBroadcast = true
            };
            try
            {
                newSocket.Bind(new IPEndPoint(localAddress, 0));
            }
            catch
            {
                newSocket.Dispose();
                throw;
            }

            var destination = new IPEndPoint(destinationAddress, settings.DestinationPort);
            var interval = settings.IntervalMs;
            socket = newSocket;
            lastError = null;
            Interlocked.Exchange(ref packetsSent, 0);
            lock (gate)
            {
                hasData = false;
            }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Default Windows timer resolution (~15 ms) would break 10 ms intervals.
            timeBeginPeriod(1);
#endif
            running = true;
            worker = new Thread(() => SendLoop(newSocket, destination, interval))
            {
                IsBackground = true,
                Name = "UDP position sender"
            };
            worker.Start();
        }

        /// <summary>Stops sending and releases the socket.</summary>
        public void Stop()
        {
            if (worker == null)
                return;

            running = false;
            worker.Join(1000);
            worker = null;
            socket?.Dispose();
            socket = null;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            timeEndPeriod(1);
#endif
        }

        private void SendLoop(Socket sendSocket, IPEndPoint destination, int intervalMs)
        {
            var sendBuffer = new byte[PositionTelegram.TotalSize];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double next = 0d;
            uint sequence = 0;

            while (running)
            {
                var remaining = next - clock.Elapsed.TotalMilliseconds;
                if (remaining > 1.5d)
                {
                    Thread.Sleep(1);
                    continue;
                }
                if (remaining > 0d)
                {
                    Thread.SpinWait(100);
                    continue;
                }

                next += intervalMs;
                var now = clock.Elapsed.TotalMilliseconds;
                if (now - next > intervalMs)
                    next = now;

                bool ready;
                lock (gate)
                {
                    ready = hasData;
                    if (ready)
                        Buffer.BlockCopy(latest, 0, sendBuffer, 0, PositionTelegram.TotalSize);
                }
                if (!ready)
                    continue;

                PositionTelegram.WriteSequence(sendBuffer, ++sequence);
                try
                {
                    sendSocket.SendTo(sendBuffer, 0, PositionTelegram.TotalSize, SocketFlags.None, destination);
                    Interlocked.Increment(ref packetsSent);
                    lastError = null;
                }
                catch (Exception exception)
                {
                    lastError = exception.Message;
                }
            }
        }
    }
}
