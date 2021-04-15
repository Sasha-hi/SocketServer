using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using log4net;

namespace SocketServer
{
    public sealed partial class SocketServer
    {
        private readonly ConcurrentDictionary<string, Client> _clients;
        private readonly ILog _log;
        private readonly TcpListener _tcpListener;
        private bool _started;

        public SocketServer(IPEndPoint ipEndPoint)
        {
            if (ipEndPoint is null) throw new ArgumentNullException(nameof(ipEndPoint));

            _tcpListener = new TcpListener(ipEndPoint);
            _clients = new ConcurrentDictionary<string, Client>();
            _log = LogManager.GetLogger(GetType());
        }

        public void Start()
        {
            if (_started)
                return;

            _tcpListener.Start();

            Task.Run(async () => await AcceptClientsAsync());
            _started = true;
        }

        private async Task AcceptClientsAsync()
        {
            try
            {
                _log.Info("Waiting for connections...");

                while (true)
                {
                    var tcpClient = await _tcpListener.AcceptTcpClientAsync();
                    var client = new Client(tcpClient);
                    _log.Debug($"Client {client.Ip} accepted");
                    client.Disconnected += ClientOnDisconnected;
                    client.ListCommandRequested += ClientOnListCommandRequested;
                    _clients.AddOrUpdate(client.Ip, client, (key, value) => value);
                    client.Start();
                }
            }
            catch (ObjectDisposedException)
            {
                _log.Info("Waiting for connections ended");
            }
            catch (Exception ex)
            {
                _log.Error(ex.Message, ex);
            }
            finally
            {
                Stop();
            }
        }

        private void ClientOnListCommandRequested(object sender, Client.ListEventArgs e)
        {
            foreach (var (key, value) in _clients)
            {
                e.List[key] = value.Sum;
            }
        }

        private void ClientOnDisconnected(object sender, EventArgs e)
        {
            if (!(sender is Client client))
                return;

            client.Disconnected -= ClientOnDisconnected;
            client.ListCommandRequested -= ClientOnDisconnected;
            client.Dispose();
            _clients.TryRemove(client.Ip, out _);
            _log.Debug($"Client {client.Ip} disconnected");
        }

        public void Stop()
        {
            _tcpListener.Stop();
            foreach (var (_, client) in _clients)
            {
                client.Close();
            }

            _started = false;
        }
    }
}