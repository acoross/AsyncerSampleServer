using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Asyncer
{
    public class AsyncPipeline
    {
        bool run = false;
        public void Post(Action func)
        {
            func();
        }

        public void Run()
        {
            run = true;
            while(run)
            {
                ;
            }
        }

        public void Stop()
        {
            run = false;
        }
    }
}

namespace AsyncerSampleServer
{
    interface IPacketHandler
    {
        /// <summary>
        /// Handle the specified data, and return processed bytes.
        /// </summary>
        /// <returns>processed bytes</returns>
        /// <param name="data">Data.</param>
        /// <param name="offset">data start offset</param>
        /// <param name="count">data length to handle (not total byte[] length)</param>
        int Handle(byte[] data, int offset, int count);
    }

    class NetBuffer
    {
        public byte[] buffer = new byte[1024];
        public int readOffset { get; private set; }
        public int writeOffset { get; private set; }
        public int readableSpace { get { return writeOffset - readOffset; } }
        public int writableSpace { get { return buffer.Length - writeOffset; } }
        public bool writable { get { return writableSpace > 0; }}
        public void Write(int recv)
        {
            writeOffset += recv;
        }

        public void Read(int read)
        {
            readOffset += read;
        }

        public void Unwind()
        {
            Buffer.BlockCopy(buffer, readOffset, buffer, 0, readableSpace);
            writeOffset -= readOffset;
            readOffset = 0;
        }

        public void Clear()
        {
            readOffset = 0;
            writeOffset = 0;
        }
    }

    class Session : IDisposable
    {
        readonly Socket socket;
        readonly NetworkStream stream;
        IPacketHandler handler;
        readonly NetBuffer recvBuffer = new NetBuffer();

        int run = 0;
        bool disposed = false;

        public Session(Socket socket)
        {
            this.socket = socket;
            stream = new NetworkStream(socket);
        }

        ~Session()
        {
            this.Dispose(false);
        }

        public void Dispose()
        {
            this.Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (!disposed)
            {
                Console.WriteLine("disposing...");
                if (disposing)
                {
                    stream.Dispose();
                    socket.Disconnect(false);
                    socket.Close();
                    socket.Dispose();
                }

                disposed = true;
            }
        }

        public void Start(IPacketHandler handler)
        {
            this.handler = handler;

            if (Interlocked.Exchange(ref run, 1) == 1)
            {
                throw new Exception("cannot start session twice!!");
            }

            recvLoop();
        }

        async void recvLoop()
        {
            try
            {
                while (run != 0)
                {
                    if (!recvBuffer.writable)
                    {
                        recvBuffer.Unwind();
                        if (!recvBuffer.writable)
                        {
                            throw new Exception("recv buffer is not writable, after Unwind()!!");
                        }
                    }

                    var recvTask = stream.ReadAsync(recvBuffer.buffer, recvBuffer.writeOffset, recvBuffer.writableSpace);

                    while(true)
                    {
                        var timeoutTask = Task.Delay(600);
                        var done = await Task.WhenAny(recvTask, timeoutTask);

                        if (done == timeoutTask)
                        {
                            if (!socket.Connected)
                            {
                                // disconnected!
                                Console.WriteLine("timeout, disconnected");
                                return;
                            }
                            else
                            {
                                Console.WriteLine("timeout, but connected");
                            }
                        }
                        else
                        {
                            break;
                        }
                    }

                    int recvLen = await recvTask;
                    if (recvLen == 0)
                    {
                        Console.WriteLine("disconnected");
                        return;
                    }

                    recvBuffer.Write(recvLen);

                    while (true)
                    {
                        int processed = handler.Handle(recvBuffer.buffer, recvBuffer.readOffset, recvBuffer.readableSpace);
                        if (processed == 0)
                        {
                            break;
                        }

                        recvBuffer.Read(processed);
                    }
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine(ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
            finally
            {
                Console.WriteLine("session end");
                this.Dispose();
            }
        }
    }

    class Listener
    {
        public delegate void OnCreateSession(Session session);
        TcpListener listener;
        int run = 0;

        public async void Start(int port, OnCreateSession onCreate)
        {
            if (Interlocked.CompareExchange(ref run, 1, 0) != 0)
            {
                throw new Exception("Listener is already running");
            }

            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();

                await Task.Yield();

                while (run != 0)
                {
                    var socket = await listener.AcceptSocketAsync();
                    var session = new Session(socket);

                    onCreate(session);
                }
            }
            catch(Exception ex)
            {
                //Console.WriteLine(ex);
            }
            finally
            {
                Stop();
            }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref run, 0) == 1)
            {
                listener?.Stop();
            }
        }
    }

    class TestPacketHandler : IPacketHandler
    {
        public int Handle(byte[] data, int offset, int count)
        {
            if (count < sizeof(int))
                return 0;

            using (var ms = new MemoryStream(data, offset, count))
            using (var bw = new BinaryReader(ms))
            {
                int len = bw.ReadInt32();
                if (len > count)
                {
                    return 0;
                }

                var msg = bw.ReadString();
                Console.WriteLine($"got msg: {msg}");

                int processed = (int)ms.Position;
                return processed;
            }
        }
    }

    class MainClass
    {
        public static void Main(string[] args)
        {
            var packetHandler = new TestPacketHandler();

            Console.WriteLine("Hello World!");

            Listener listener = new Listener();
            listener.Start(17777, (session) =>
            {
                Console.WriteLine("session connected");

                session.Start(packetHandler);
            });

            for (;;)
            {
                var cmd = Console.ReadLine();
                if (cmd == "/q")
                {
                    listener.Stop();
                    break;
                }
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
    }
}
