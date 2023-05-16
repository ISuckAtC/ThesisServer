using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Numerics;

public class ServerType3
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
    public static List<UpdatePacket> updatePackets = new List<UpdatePacket>();
    public static Dictionary<int, long> lastPacketId = new Dictionary<int, long>();
    public static byte[] currentPacket = new byte[PacketTotalSize];

    public static Dictionary<int, byte[]> currentPackets = new Dictionary<int, byte[]>();

    public static bool logging = true;
    public static Process p = Process.GetCurrentProcess();

    public static List<float> cpuUsageList = new List<float>();
    public static List<long> ramUsageList = new List<long>();

    public static PerformanceCounter cpuCounter;
    public static PerformanceCounter ramCounter;

    public static PacketType packetType = PacketType.update;

    public static Dictionary<int, List<RTSSolider>> soliderGroups = new Dictionary<int, List<RTSSolider>>();
    public static List<UpdateSolider> updateSoliders = new List<UpdateSolider>();
    public static List<InputSolider> inputSoliders = new List<InputSolider>();

    public static long bytesRead = 0;
    public static long bytesWritten = 0;

    public static async Task Start()
    {
        try
        {
            PerformanceCounterCategory category = new PerformanceCounterCategory("Process");

            PerformanceCounter[] counters = category.GetCounters(p.ProcessName);

            foreach (PerformanceCounter counter in counters) Console.WriteLine(counter.CounterName);

            cpuCounter = new PerformanceCounter("Process", "% Processor Time", p.ProcessName, true);
            ramCounter = new PerformanceCounter("Process", "Working Set - Private", p.ProcessName, true);
            Task logger = Task.Run(async () => await LogCPUAndRAMUsage(100, 200, 10000));
            overHeadServer.Start();

            running = true;

            Task listenerTask = Task.Run(async () => await ListenTcp());




            Console.WriteLine("Enough players connected, running...");

            while (running)
            {
                Task waitTask = Task.Delay(tickInterval);

                if (packetType == PacketType.rts)
                {
                    for (int i = 0; i < soliderGroups.Count; ++i)
                    {
                        for (int k = 0; k < 32; ++k)
                        {
                            soliderGroups.ElementAt(i).Value[k].Update();
                        }
                    }
                    lock (currentPackets)
                    {
                        for (int i = 0; i < currentPackets.Count; ++i)
                        {
                            long packetId = BitConverter.ToInt64(currentPackets[i], 0);
                            int senderId = BitConverter.ToInt32(currentPackets[i], 8);
                            int packetCount = BitConverter.ToInt32(currentPackets[i], 12);
                            for (int k = 0; k < packetCount; ++k)
                            {
                                byte[] packetBuffer = new byte[16];
                                Array.Copy(currentPackets[i], 16 + (16 * k), packetBuffer, 0, 16);
                                RTSPacket packet = RTSPacket.Deserialize(packetBuffer);
                                for (int j = 0; j < 32; ++j)
                                {
                                    RTSSolider solider = soliderGroups[senderId][j];
                                    if (packet.selectedUnits[j]) solider.SetTarget(packet.target);

                                    UpdatePacket updatePacket = updatePackets[(solider.owner * 32) + j];
                                    updatePacket.position = new Vector3(solider.current.X, 1f, solider.current.Y);
                                    updatePackets[(solider.owner * 32) + j] = updatePacket;
                                }
                            }
                        }
                        currentPackets.Clear();
                    }
                }
                if (packetType == PacketType.update)
                {
                    for (int i = 0; i < updateSoliders.Count; ++i)
                    {
                        UpdateSolider solider = updateSoliders[i];
                        solider.Update();

                        if (updatePackets.Count >= updateSoliders.Count)
                        {
                            UpdatePacket updatePacket = updatePackets[i];
                            updatePacket.position = solider.position;
                            updatePackets[i] = updatePacket;
                        }

                    }
                }
                if (packetType == PacketType.input)
                {
                    for (int i = 0; i < inputSoliders.Count; ++i)
                    {
                        InputSolider solider = inputSoliders[i];
                        solider.Update();

                        UpdatePacket updatePacket = updatePackets[i];
                        updatePacket.position = solider.position;
                        updatePackets[i] = updatePacket;
                    }
                }

                lock (currentPacket) lock (updatePackets) lock (lastPacketId)
                        {
                            int packetAmount = updatePackets.Count;
                            int idAmount = lastPacketId.Count;

                            byte[] buffer = new byte[8 + (packetAmount * 16) + (idAmount * 12)];

                            BitConverter.GetBytes(packetAmount).CopyTo(buffer, 0);
                            BitConverter.GetBytes(idAmount).CopyTo(buffer, 4);

                            for (int i = 0; i < packetAmount; ++i)
                            {
                                //Console.WriteLine(i + ": " + updatePackets[i].position);
                                updatePackets[i].Serialize().CopyTo(buffer, 8 + (16 * i));
                            }

                            for (int i = 0; i < idAmount; ++i)
                            {
                                BitConverter.GetBytes(lastPacketId.ElementAt(i).Key).CopyTo(buffer, 8 + (16 * packetAmount) + (12 * i));
                                BitConverter.GetBytes(lastPacketId.ElementAt(i).Value).CopyTo(buffer, 8 + (16 * packetAmount) + (12 * i) + 4);
                            }

                            currentPacket = buffer;
                        }




                await waitTask;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n\n" + e.StackTrace);
        }
    }

    public static async Task LogCPUAndRAMUsage(int interval, int captureAmount, int delay)
    {
        await Task.Delay(delay);
        string cpu = "";
        string ram = "";
        string networkRead = "";
        string networkWrite = "";
        long prevRead = 0;
        long prevWritten = 0;
        long read = 0;
        long written = 0;
        for (int i = 0; i < captureAmount; ++i)
        {
            Task wait = Task.Delay(interval);

            read = bytesRead;
            written = bytesWritten;

            cpu += cpuCounter.NextValue() / Environment.ProcessorCount;
            cpu += ";";
            ram += ramCounter.NextValue();
            ram += ";";
            networkRead += (read - prevRead) * (1000 / interval);
            networkRead += ";";
            networkWrite += (written - prevWritten) * (1000 / interval);
            networkWrite += ";";

            prevRead = read;
            prevWritten = written;

            await wait;
        }

        File.WriteAllText("../outputs/outServer.csv", cpu + "\n" + ram + "\n" + networkRead + "\n" + networkWrite);
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
            lastPacketId.Add(id, 0);

            if (packetType == PacketType.rts)
            {
                List<RTSSolider> soliders = new List<RTSSolider>();
                for (int i = 0; i < 32; ++i)
                {
                    RTSSolider solider = new RTSSolider(Vector2.Zero, id, 1f);
                    soliders.Add(solider);
                    UpdatePacket updatePacket = new UpdatePacket();
                    updatePacket.Id = (id * 32) + i;
                    updatePacket.position = new Vector3(solider.current.X, 1f, solider.current.Y);
                    updatePackets.Add(updatePacket);
                }
                soliderGroups.Add(id, soliders);
            }
            if (packetType == PacketType.update)
            {
                UpdateSolider solider = new UpdateSolider(id, new Vector3(0f, 1f, 0f));
                updateSoliders.Add(solider);
                UpdatePacket updatePacket = new UpdatePacket();
                updatePacket.Id = solider.id;
                updatePacket.position = solider.position;
                updatePackets.Add(updatePacket);
            }
            if (packetType == PacketType.input)
            {
                InputSolider solider = new InputSolider(id, new Vector3(0f, 1f, 0f));
                inputSoliders.Add(solider);
                UpdatePacket updatePacket = new UpdatePacket();
                updatePacket.Id = solider.id;
                updatePacket.position = solider.position;
            }

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
        bytesWritten += bytes.Length;
        await client.SendAsync(bytes, bytes.Length);
    }
    public static async Task UdpRecieve(UdpClient client, int id)
    {
        try
        {
            while (running)
            {
                UdpReceiveResult result = await client.ReceiveAsync();

                bytesRead = result.Buffer.Length;

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

            long packetId = BitConverter.ToInt64(bytes, 0);
            int senderId = BitConverter.ToInt32(bytes, 8);

            lastPacketId[senderId] = packetId;

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
                byte[] serializable = new byte[16];
                Array.Copy(bytes, 8, serializable, 0, 16);

                if (packetType == PacketType.update)
                {
                    UpdatePacket updatePacket = UpdatePacket.Deserialize(serializable);
                    if (updatePacket.Id < updateSoliders.Count) updateSoliders[updatePacket.Id].position = updatePacket.position;

                }
                if (packetType == PacketType.input)
                {
                    InputPacket inputPacket = InputPacket.Deserialize(serializable);
                    if (inputPacket.Id < inputSoliders.Count) inputSoliders[inputPacket.Id].Input(inputPacket.buttons, inputPacket.analog);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n\n" + e.StackTrace);
            Console.WriteLine(bytes.Length);
            Console.WriteLine(outPacketSize);
            Console.WriteLine(id);
        }
    }
}