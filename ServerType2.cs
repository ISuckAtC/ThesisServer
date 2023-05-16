using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

public class ServerType2
{
    public static List<TcpClient> overHeads = new List<TcpClient>();
    public static Dictionary<int, (UdpClient client, int lastLatency)> underHeads = new Dictionary<int, (UdpClient, int)>();
    public static TcpListener overHeadServer = new TcpListener(IPAddress.Any, 6969);
    public static int playerCountCurrent = 0;
    public static int overHeadPort = 6969;
    public static bool running = false;
    public static int tickInterval = 33;
    public static int inPacketSize = 28;
    /*
        0:  latency
        4:  packetId
        12: senderId
        16: data
    */
    public static int outPacketSize = inPacketSize - 4;
    /*
        0:  packetId
        8:  senderId
        12: data
    */
    public static int PacketTotalSize { get { return outPacketSize * playerCountCurrent; } }
    public static byte[] currentPacket = new byte[PacketTotalSize];
    public static byte[] currentWorkingPacket = new byte[PacketTotalSize];
    public static byte[] inbetweenPacket = new byte[PacketTotalSize];

    public static Dictionary<int, byte[]> currentPackets = new Dictionary<int, byte[]>();

    public static bool logging = true;
    public static Process p = Process.GetCurrentProcess();

    public static List<float> cpuUsageList = new List<float>();
    public static List<long> ramUsageList = new List<long>();

    public static PerformanceCounter cpuCounter;
    public static PerformanceCounter ramCounter;

    public static PacketType packetType = PacketType.update;

    public static async Task Start()
    {
        try
        {
            PerformanceCounterCategory category = new PerformanceCounterCategory("Process");

            PerformanceCounter[] counters = category.GetCounters(p.ProcessName);

            foreach (PerformanceCounter counter in counters) Console.WriteLine(counter.CounterName);

            cpuCounter = new PerformanceCounter("Process", "% Processor Time", p.ProcessName, true);
            ramCounter = new PerformanceCounter("Process", "Working Set - Private", p.ProcessName, true);
            Task logger = Task.Run(async () => await LogCPUAndRAMUsage(100));
            overHeadServer.Start();

            running = true;

            Task listenerTask = Task.Run(async () => await ListenTcp());




            Console.WriteLine("Enough players connected, running...");

            while (running)
            {
                Task waitTask = Task.Delay(tickInterval);

                if (packetType == PacketType.rts)
                {
                    lock (currentPacket) lock (currentPackets)
                        {
                            int totalSize = 4;
                            for (int i = 0; i < currentPackets.Count; ++i)
                            {
                                totalSize += currentPackets.ElementAt(i).Value.Length;
                            }
                            byte[] workingPacket = new byte[totalSize];
                            BitConverter.GetBytes(currentPackets.Count).CopyTo(workingPacket, 0);

                            int currentLength = 4;

                            for (int i = 0; i < currentPackets.Count; ++i)
                            {
                                currentPackets[i].CopyTo(workingPacket, currentLength);
                                currentLength += currentPackets[i].Length;
                            }

                            currentPacket = workingPacket;
                        }
                }
                else
                {
                    lock (inbetweenPacket)
                    {
                        lock (currentWorkingPacket)
                        {
                            currentWorkingPacket.CopyTo(inbetweenPacket, 0);
                        }
                        lock (currentPacket)
                        {
                            inbetweenPacket.CopyTo(currentPacket, 0);
                        }
                    }
                }



                await waitTask;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n\n" + e.StackTrace);
        }
    }

    public static async Task LogCPUAndRAMUsage(int interval)
    {
        while (logging)
        {
            Task wait = Task.Delay(interval);

            float cpuUsage = cpuCounter.NextValue() / Environment.ProcessorCount;
            float ramUsage = ramCounter.NextValue();

            cpuUsageList.Add(cpuUsage);
            ramUsageList.Add((long)ramUsage);
            Console.WriteLine(cpuUsage + " | " + ramUsage);

            await wait;
        }
    }

    public static async Task ListenTcp()
    {
        try
        {
            while (running)
            {
                Console.WriteLine("Awaiting connection...");

                TcpClient acceptedClient = await overHeadServer.AcceptTcpClientAsync();

                Console.WriteLine("Player connected, port: " + (acceptedClient.Client.RemoteEndPoint as IPEndPoint).Port);

                int id = playerCountCurrent;
                playerCountCurrent++;

                if (packetType == PacketType.rts)
                {
                    byte[] defaultPacket = new byte[16];
                    BitConverter.GetBytes((long)0).CopyTo(defaultPacket, 0);
                    BitConverter.GetBytes(id).CopyTo(defaultPacket, 8);
                    BitConverter.GetBytes((int)0).CopyTo(defaultPacket, 12);
                    currentPackets.Add(id, defaultPacket);
                }
                else
                {
                    lock (currentWorkingPacket) lock (currentPacket) lock (inbetweenPacket)
                            {
                                byte[] buffer = new byte[PacketTotalSize + 4];
                                Array.Copy(currentWorkingPacket, 0, buffer, 0, (buffer.Length < currentWorkingPacket.Length) ? buffer.Length : currentWorkingPacket.Length);
                                currentWorkingPacket = buffer;

                                BitConverter.GetBytes(playerCountCurrent).CopyTo(currentWorkingPacket, 0);

                                byte[] buffer2 = new byte[PacketTotalSize + 4];
                                Array.Copy(currentPacket, 0, buffer2, 0, (buffer2.Length < currentPacket.Length) ? buffer2.Length : currentPacket.Length);
                                currentPacket = buffer2;

                                BitConverter.GetBytes(playerCountCurrent).CopyTo(currentPacket, 0);

                                inbetweenPacket = new byte[PacketTotalSize + 4];
                            }
                }

                Task handleTcp = Task.Run(async () => await HandleTcp(acceptedClient, id));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n\n" + e.StackTrace);
        }
    }

    public static async Task HandleTcp(TcpClient client, int id)
    {
        try
        {
            overHeads.Add(client);

            NetworkStream ns = client.GetStream();

            byte[] buffer = new byte[17];

            buffer[0] = 1;
            BitConverter.GetBytes(playerCountCurrent).CopyTo(buffer, 1);
            BitConverter.GetBytes(id).CopyTo(buffer, 5);


            UdpClient udpClient = new UdpClient(0, AddressFamily.InterNetwork);
            int port = ((IPEndPoint)udpClient.Client.LocalEndPoint).Port;


            underHeads.Add(id, (udpClient, 0));

            BitConverter.GetBytes(port).CopyTo(buffer, 9);
            BitConverter.GetBytes(port).CopyTo(buffer, 13);

            await ns.WriteAsync(buffer, 0, 17);

            Console.WriteLine("Sent info to client");

            buffer = new byte[4];

            await ns.ReadAsync(buffer, 0, 4);

            Console.WriteLine("Received udp port from client");

            int remotePort = BitConverter.ToInt32(buffer);

            Console.WriteLine("Port: " + remotePort);

            udpClient.Connect(((IPEndPoint)client.Client.RemoteEndPoint).Address, remotePort);

            buffer = new byte[1];
            buffer[0] = 1;

            await ns.WriteAsync(buffer, 0, 1);

            Console.WriteLine("Sent confirm to client");

            while (!running) await Task.Delay(tickInterval);

            Task udpSend = Task.Run(async () => await UdpSend(udpClient, id));
            Task udpRecieve = Task.Run(async () => await UdpRecieve(udpClient, id));

            int pCountLocal = playerCountCurrent;

            while (true)
            {
                while (pCountLocal == playerCountCurrent) await Task.Delay(tickInterval);
                pCountLocal = playerCountCurrent;
                await ns.WriteAsync(BitConverter.GetBytes(pCountLocal), 0, 4);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n\n" + e.StackTrace);
        }
    }

    public static async Task UdpSend(UdpClient client, int id)
    {
        try
        {
            while (running)
            {
                Task waitTask = Task.Delay(tickInterval);

                byte[] delayPacket;


                lock (currentPacket)
                {
                    delayPacket = new byte[currentPacket.Length];
                    Array.Copy(currentPacket, 0, delayPacket, 0, currentPacket.Length);
                }

                Task delaySend = Task.Run(async () => await DelayedSend(client, underHeads[id].lastLatency, delayPacket));
                //Console.WriteLine("sent packet");

                await waitTask;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n\n" + e.StackTrace);
        }
    }
    public static async Task DelayedSend(UdpClient client, int delay, byte[] bytes)
    {
        await Task.Delay(delay);
        //Console.WriteLine("Recieved packet (after delay)");
        await client.SendAsync(bytes, bytes.Length);
    }
    public static async Task UdpRecieve(UdpClient client, int id)
    {
        try
        {
            while (running)
            {
                UdpReceiveResult result = await client.ReceiveAsync();

                byte[] buffer = new byte[result.Buffer.Length - 4];

                int delay = BitConverter.ToInt32(result.Buffer, 0);

                underHeads[id] = (client, delay);

                Array.Copy(result.Buffer, 4, buffer, 0, buffer.Length);

                Task delayRecieve = Task.Run(async () => await DelayedRecieve(id, delay, buffer));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n\n" + e.StackTrace);
        }
    }

    public static async Task DelayedRecieve(int id, int delay, byte[] bytes)
    {
        try
        {
            await Task.Delay(delay);
            //Console.WriteLine("Recieved packet (after delay)");

            if (packetType == PacketType.rts)
            {
                lock (currentPackets)
                {
                    byte[] buffer = new byte[bytes.Length];
                    //if (buffer.Length != 16) Console.WriteLine(buffer.Length + " RECIEVE");
                    Array.Copy(bytes, 0, buffer, 0, bytes.Length);
                    currentPackets[id] = buffer;
                }
            }
            else
            {
                lock (currentWorkingPacket)
                {
                    Array.Copy(bytes, 0, currentWorkingPacket, 4 + outPacketSize * id, outPacketSize);
                    //Console.WriteLine(BitConverter.ToSingle(bytes, 12) + " | " + BitConverter.ToSingle(bytes, 16) + " | " + BitConverter.ToSingle(bytes, 20));
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n\n" + e.StackTrace);
            Console.WriteLine(bytes.Length);
            Console.WriteLine(currentWorkingPacket.Length);
            Console.WriteLine(outPacketSize);
            Console.WriteLine(id);
        }
    }
}