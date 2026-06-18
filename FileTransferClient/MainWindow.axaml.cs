using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using System.Linq;
using System.IO;
using System.Threading;

namespace FileTransferClient;

public partial class MainWindow : Window
{
    private TcpClient? client;
    private NetworkStream? stream;
    private string? selectedFilePath;
private byte[]? receivedFileData;
private string? receivedFileName;
private string? receivedSenderName;
    public MainWindow()
    {
        InitializeComponent();
        btnConnect.Click += BtnConnect_Click;
        btnBrowse.Click += BtnBrowse_Click;
            btnSend.Click += BtnSend_Click;
                btnSave.Click += BtnSave_Click;


    }

    private void BtnSend_Click(object? sender, RoutedEventArgs e)
{
    if (selectedFilePath == null)
    {
        txtStatus.Text = "Please select a file first";
        return;
    }

    if (lstUsers.SelectedItem == null)
    {
        txtStatus.Text = "Please select receiver";
        return;
    }

    string receiver = lstUsers.SelectedItem.ToString()!;
    string fileName = Path.GetFileName(selectedFilePath);

    byte[] fileData = File.ReadAllBytes(selectedFilePath);
    long fileSize = fileData.Length;

    string header = $"FILE|{receiver}|{fileName}";
byte[] headerBytes = Encoding.UTF8.GetBytes(header);

// Gửi 4 byte độ dài header
byte[] headerLengthBytes = BitConverter.GetBytes(headerBytes.Length);
stream!.Write(headerLengthBytes, 0, headerLengthBytes.Length);

// Gửi header
stream.Write(headerBytes, 0, headerBytes.Length);

// Gửi 8 byte kích thước file
byte[] fileSizeBytes = BitConverter.GetBytes(fileSize);
stream.Write(fileSizeBytes, 0, fileSizeBytes.Length);

// Gửi nội dung file
stream.Write(fileData, 0, fileData.Length);

    txtStatus.Text = "File sent to server";
}
    private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
{
    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
    {
        Title = "Chọn file cần gửi",
        AllowMultiple = false
    });

    if (files.Count > 0)
    {
        selectedFilePath = files[0].Path.LocalPath;
        txtFileName.Text = selectedFilePath;
    }
}
private void BtnConnect_Click(object? sender, RoutedEventArgs e)
{
    try
    {
        string ip = txtIP.Text!;
        int port = int.Parse(txtPort.Text!);

        client = new TcpClient();
        client.Connect(ip, port);

        stream = client.GetStream();

        txtStatus.Text = "Connected";

        string username = txtUsername.Text!;
        byte[] data = Encoding.UTF8.GetBytes(username);

        stream.Write(data, 0, data.Length);

        Task.Run(() => ListenServer());
    }
    catch (Exception)
    {
        txtStatus.Text = "Connection failed";
    }
}
private void ListenServer()
{
    try
    {
        while (true)
        {
            byte[] headerLengthBytes = ReadExact(stream!, 4);
            int headerLength = BitConverter.ToInt32(headerLengthBytes, 0);

            byte[] headerBytes = ReadExact(stream!, headerLength);
            string header = Encoding.UTF8.GetString(headerBytes);

            if (header.StartsWith("USERS|"))
            {
                string usersText = header.Substring(6);
                string[] users = usersText.Split(',');

                Dispatcher.UIThread.Post(() =>
                {
                    lstUsers.Items.Clear();

                    foreach (string user in users)
                    {
                        if (!string.IsNullOrWhiteSpace(user) && user != txtUsername.Text)
                        {
                            lstUsers.Items.Add(user);
                        }
                    }
                });
            }
            else if (header.StartsWith("FILE|"))
            {
                string[] parts = header.Split('|');

                string senderName = parts[1];
                string fileName = parts[2];

                byte[] fileSizeBytes = ReadExact(stream!, 8);
                long fileSize = BitConverter.ToInt64(fileSizeBytes, 0);

                byte[] fileData = ReadExact(stream!, fileSize);
receivedFileData = fileData;
receivedFileName = fileName;
receivedSenderName = senderName;
                string saveFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "ReceivedFiles"
                );

                Directory.CreateDirectory(saveFolder);

                string savePath = Path.Combine(saveFolder, fileName);

                File.WriteAllBytes(savePath, fileData);

                Dispatcher.UIThread.Post(() =>
                {
                    txtStatus.Text = $"Received file from {senderName}";
                    txtFileName.Text = $"Saved: {savePath}";
                });
            }
        }
    }
    catch
    {
        Dispatcher.UIThread.Post(() =>
        {
            txtStatus.Text = "Disconnected from server";
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
    
private async void BtnSave_Click(object? sender, RoutedEventArgs e)
{
    if (receivedFileData == null || receivedFileName == null)
    {
        txtStatus.Text = "No received file to save";
        return;
    }

    var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
    {
        Title = "Save received file",
        SuggestedFileName = receivedFileName
    });

    if (file == null)
    {
        txtStatus.Text = "Save cancelled";
        return;
    }

    string savePath = file.Path.LocalPath;

    File.WriteAllBytes(savePath, receivedFileData);

    txtStatus.Text = $"File saved: {savePath}";
}
}