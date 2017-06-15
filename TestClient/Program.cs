using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace TestClient
{
    class MainClass
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            TcpClient client = new TcpClient();
            client.Connect(IPAddress.Loopback, 17777);

            Console.WriteLine("connected to server");

            for (;;)
            {
                Console.Write("enter message: ");
                var msg = Console.ReadLine();
                if (msg == "/q")
                {
                    Console.WriteLine("quit...");
                    break;
                }
                else
                {
                    using (var ms = new MemoryStream())
                    using (var bw = new BinaryWriter(ms))
                    {
                        bw.Write(0);
                        bw.Write(msg);
                        int len = (int)ms.Position;
                        Console.WriteLine($"len: {len}");
                        ms.Position = 0;
                        bw.Write(len);

                        var stream = client.GetStream();
                        {
                            stream.Write(ms.ToArray(), 0, len);
                        }
                    }
                }
            }

            client.Close();
            client.Dispose();

            Console.WriteLine("disconnected");
        }
    }
}
