using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using PowerArgs;

namespace SocketServer
{
    public sealed class SocketServer
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

        private sealed class Client : IDisposable
        {
            private const string ListCommandMessage = "list";
            private const string DisconnectMessage = "exit";
            private const int MaxDataLength = 21;
            private readonly CancellationTokenSource _cancellationTokenSource;
            private readonly ILog _log;
            private readonly TcpClient _tcpClient;

            public Client(TcpClient tcpClient)
            {
                _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
                Ip = tcpClient.Client.RemoteEndPoint.ToString();
                _cancellationTokenSource = new CancellationTokenSource();
                _log = LogManager.GetLogger(GetType());
            }

            public string Ip { get; }

            public long Sum { get; private set; }

            public void Dispose()
            {
                _tcpClient.Dispose();
                _cancellationTokenSource.Dispose();
            }

            public void Close()
            {
                _cancellationTokenSource.Cancel();
                _tcpClient.Close();
            }

            public void Start()
            {
                Task.Run(async () => await ProcessAsync(_cancellationTokenSource.Token));
            }

            private async Task ProcessAsync(CancellationToken cancellationToken)
            {
                try
                {
                    await using var stream = _tcpClient.GetStream();
                    while (true)
                    {
                        var message = await ReceiveMessageAsync(stream, cancellationToken);
                        if (string.Equals(message, DisconnectMessage, StringComparison.Ordinal))
                            break;

                        await HandleMessageAsync(message, stream, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (IOException ex)
                {
                    _log.Warn($"Client {Ip} disconnected. {ex.Message}", ex);
                }
                catch (Exception ex)
                {
                    _log.Error($"Client {Ip}. {ex.Message}", ex);
                }
                finally
                {
                    Close();
                    OnDisconnected();
                }
            }

            private async Task HandleMessageAsync(string message, Stream stream, CancellationToken cancellationToken)
            {
                string response;

                if (long.TryParse(message, out var number))
                {
                    Sum += number;
                    response = $"Sum={Sum}{Environment.NewLine}";
                }
                else if (message.Equals(ListCommandMessage, StringComparison.Ordinal))
                {
                    var arg = new ListEventArgs();
                    OnListCommandRequested(arg);

                    var sb = new StringBuilder();
                    sb.AppendLine("Clients:");
                    arg.List.ForEach(pair => sb.AppendLine($"{pair.Key} {pair.Value}"));

                    response = sb.ToString();
                }
                else
                {
                    response =
                        $"Invalid command. Please use numbers or command ({ListCommandMessage}, {DisconnectMessage}){Environment.NewLine}";
                }

                var data = Encoding.ASCII.GetBytes(response);
                await stream.WriteAsync(data, 0, data.Length, cancellationToken);
            }

            private async Task<string> ReceiveMessageAsync(Stream stream, CancellationToken cancellationToken)
            {
                var dataBuffer = new StringBuilder(MaxDataLength);
                var bytes = new byte[1];
                while (true)
                {
                    var dataLength = await stream.ReadAsync(bytes, 0, bytes.Length, cancellationToken);

                    if (dataLength <= 0)
                        return DisconnectMessage;

                    var data = Encoding.ASCII.GetString(bytes);

                    if (data == "\n")
                    {
                        var message = dataBuffer.ToString();
                        _log.Debug($"Client {Ip}. Message: {message}");
                        return message;
                    }

                    dataBuffer.Append(data);

                    if (dataBuffer.Length > MaxDataLength)
                        dataBuffer.Clear();
                }
            }

            public event EventHandler<EventArgs> Disconnected;
            public event EventHandler<ListEventArgs> ListCommandRequested;

            private void OnDisconnected()
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            }

            private void OnListCommandRequested(ListEventArgs e)
            {
                ListCommandRequested?.Invoke(this, e);
            }

            internal class ListEventArgs : EventArgs
            {
                public ListEventArgs()
                {
                    List = new Dictionary<string, long>();
                }

                public IDictionary<string, long> List { get; }
            }
        }
    }
}