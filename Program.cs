using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

public enum PacketType
{
    rts,
    input,
    update
}

public class Program
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

    public static bool logging = true;
    public static Process p = Process.GetCurrentProcess();

    public static List<float> cpuUsageList = new List<float>();
    public static List<long> ramUsageList = new List<long>();

    public static PerformanceCounter cpuCounter;
    public static PerformanceCounter ramCounter;

    public static PacketType packetType = PacketType.rts;

    public static async Task Main(string[] args)
    {
        try
        {
            await ServerType3.Start();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n\n" + e.StackTrace);
        }
    }
}