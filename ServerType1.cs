using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

public class ServerType1
{
    public static List<TcpClient> overHeads = new List<TcpClient>();
    public static TcpListener overHeadServer = new TcpListener(IPAddress.Any, 6969);
    public static Dictionary<int, int> udpPorts = new Dictionary<int, int>();
    public static int playerCountCurrent = 0;
    public static int playerCountTarget = 2;
    public static int overHeadPort = 6969;
    public static bool running = false;
    public static int tickInterval = 33;
    public static Process p = Process.GetCurrentProcess();

    public static async Task Start()
    {
        try
        {
            overHeadServer.Start();
            Task listenerTask = Task.Run(async () => await ListenTcp());

            while (!running) await Task.Delay(tickInterval);

            Console.WriteLine("Enough players connected, running...");

            while (running)
            {
                Task waitTask = Task.Delay(tickInterval);


                await waitTask;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n\n" + e.StackTrace);
        }
    }

    public static async Task ListenTcp()
    {
        try
        {
            int playersConnecting = 0;
            while (playersConnecting < playerCountTarget)
            {
                Console.WriteLine("Awaiting connection...");

                TcpClient acceptedClient = await overHeadServer.AcceptTcpClientAsync();

                Console.WriteLine("Player connected, port: " + (acceptedClient.Client.RemoteEndPoint as IPEndPoint).Port);

                int id = playerCountCurrent;
                playersConnecting++;

                Task handleTcp = Task.Run(async () => await HandleTcp(acceptedClient, id));
            }

            while (playerCountCurrent < playerCountTarget) await Task.Delay(10);

            byte[] buffer = new byte[8 * playerCountTarget];

            for (int i = 0; i < playerCountTarget; ++i)
            {
                BitConverter.GetBytes(i).CopyTo(buffer, 8 * i);
                BitConverter.GetBytes(udpPorts[i]).CopyTo(buffer, (8 * i) + 4);
            }

            for (int i = 0; i < playerCountTarget; ++i)
            {
                await overHeads[i].GetStream().WriteAsync(buffer, 0, 8 * playerCountTarget);
            }

            Console.WriteLine("Sent confirms to clients");

            running = true;
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

            byte[] buffer = new byte[5];

            buffer[0] = 1;
            BitConverter.GetBytes(id).CopyTo(buffer, 1);

            await ns.WriteAsync(buffer, 0, 5);

            Console.WriteLine("Sent info to client");

            buffer = new byte[4];

            await ns.ReadAsync(buffer, 0, 4);

            Console.WriteLine("Received udp recieve port from client");

            int remotePort = BitConverter.ToInt32(buffer);

            Console.WriteLine("Port: " + remotePort);

            udpPorts.Add(id, remotePort);
            playerCountCurrent++;

            while (!running) await Task.Delay(tickInterval);

            buffer = new byte[1];

            await ns.ReadAsync(buffer, 0, 1);

            Console.WriteLine("Recieved ready");

            await ns.WriteAsync(buffer, 0, 1);

            Console.WriteLine("Sent start signal");
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n\n" + e.StackTrace);
        }
    }
}