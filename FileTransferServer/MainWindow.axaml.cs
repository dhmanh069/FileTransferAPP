using System;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Generic;
using System.IO;
namespace FileTransferServer;

public partial class MainWindow : Window
{
        private TcpListener? server;
        private Dictionary<string, TcpClient> clients = new Dictionary<string, TcpClient>();
    private object lockObj = new object();

    public MainWindow()
    {
        InitializeComponent();
        btnStart.Click += BtnStart_Click;
    }
    private void BtnStart_Click(object? sender, RoutedEventArgs e)
{
    int port = int.Parse(txtPort.Text!);

    server = new TcpListener(IPAddress.Any, port);

    server.Start();

    txtStatus.Text = "Server started";

    lstHistory.Items.Add($"Server started on port {port}");

    Task.Run(() =>
{
    while (true)
    {
        TcpClient client = server.AcceptTcpClient();

        Task.Run(() =>
        {
            RegisterClient(client);
        });
    }
});
}
private void RegisterClient(TcpClient client)
{
    try
    {
        NetworkStream stream = client.GetStream();

        byte[] buffer = new byte[1024];

        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        string username = Encoding.UTF8.GetString(buffer, 0, bytesRead);

        clients[username] = client;

        Dispatcher.UIThread.Post(() =>
        {
            lstClients.Items.Add(username);
            lstHistory.Items.Add($"{username} connected");
        });

        BroadcastOnlineUsers();

        // Sau khi đăng ký xong, bắt đầu lắng nghe file từ client này
        Task.Run(() => HandleClient(username, client));
    }
    catch
    {
    }
}
private void BroadcastOnlineUsers()
{
    string users = string.Join(",", clients.Keys);
    string header = "USERS|" + users;

    byte[] headerBytes = Encoding.UTF8.GetBytes(header);
    byte[] headerLengthBytes = BitConverter.GetBytes(headerBytes.Length);

    lock (lockObj)
    {
        foreach (TcpClient client in clients.Values)
        {
            try
            {
                NetworkStream stream = client.GetStream();

                // Gửi 4 byte độ dài header
                stream.Write(headerLengthBytes, 0, headerLengthBytes.Length);

                // Gửi nội dung header USERS
                stream.Write(headerBytes, 0, headerBytes.Length);
            }
            catch
            {
            }
        }
    }
}

private void HandleClient(string username, TcpClient client)
{
    try
    {
        NetworkStream stream = client.GetStream();

        while (true)
        {
            // 1. Đọc 4 byte độ dài header
            byte[] headerLengthBytes = ReadExact(stream, 4);
            int headerLength = BitConverter.ToInt32(headerLengthBytes, 0);

            // 2. Đọc header
            byte[] headerBytes = ReadExact(stream, headerLength);
            string header = Encoding.UTF8.GetString(headerBytes);

            if (header.StartsWith("FILE|"))
            {
                string[] parts = header.Split('|');

                string receiver = parts[1];
                string fileName = parts[2];

                // 3. Đọc 8 byte kích thước file
                byte[] fileSizeBytes = ReadExact(stream, 8);
                long fileSize = BitConverter.ToInt64(fileSizeBytes, 0);

                // 4. Đọc đúng dữ liệu file
                byte[] fileData = ReadExact(stream, fileSize);
if (clients.ContainsKey(receiver))
{
    TcpClient receiverClient = clients[receiver];

    NetworkStream receiverStream = receiverClient.GetStream();

    // Header gửi cho Client nhận
    string sendHeader = $"FILE|{username}|{fileName}";
    byte[] sendHeaderBytes = Encoding.UTF8.GetBytes(sendHeader);

    // Gửi độ dài Header
    byte[] sendHeaderLength = BitConverter.GetBytes(sendHeaderBytes.Length);
    receiverStream.Write(sendHeaderLength, 0, sendHeaderLength.Length);

    // Gửi Header
    receiverStream.Write(sendHeaderBytes, 0, sendHeaderBytes.Length);

    // Gửi kích thước file
    byte[] sendFileSize = BitConverter.GetBytes(fileSize);
    receiverStream.Write(sendFileSize, 0, sendFileSize.Length);

    // Gửi dữ liệu file
    receiverStream.Write(fileData, 0, fileData.Length);

    Dispatcher.UIThread.Post(() =>
    {
        lstHistory.Items.Add($"Forwarded {fileName}");
        lstHistory.Items.Add($"{username} -> {receiver}");
    });
}
else
{
    Dispatcher.UIThread.Post(() =>
    {
        lstHistory.Items.Add($"Receiver {receiver} not online");
    });
}
                Dispatcher.UIThread.Post(() =>
                {
                    lstHistory.Items.Add($"Received file from {username}");
                    lstHistory.Items.Add($"To: {receiver}");
                    lstHistory.Items.Add($"File: {fileName}");
                    lstHistory.Items.Add($"Size: {fileSize} bytes");
                });
            }
        }
    }
    catch
    {
        Dispatcher.UIThread.Post(() =>
        {
            lstHistory.Items.Add($"{username} disconnected");
        });
    }
}
private byte[] ReadExact(NetworkStream stream, long size)
{
    byte[] buffer = new byte[size];

    int totalRead = 0;

    while (totalRead < size)
    {
        int read = stream.Read(buffer, totalRead, (int)(size - totalRead));

        if (read <= 0)
            throw new Exception("Connection closed");

        totalRead += read;
    }

    return buffer;
}
}