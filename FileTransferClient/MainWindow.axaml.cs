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
using System.Collections.Generic;

namespace FileTransferClient;

public partial class MainWindow : Window
{
    private TcpClient? client;
    private NetworkStream? stream;
private List<string> selectedFilePaths = new List<string>();    private byte[]? receivedFileData;
    private string? receivedFileName;
    private string? receivedSenderName;
    private List<ReceivedFileItem> receivedFiles = new List<ReceivedFileItem>();

    public MainWindow()
    {
        InitializeComponent();
        btnConnect.Click += BtnConnect_Click;
        btnBrowse.Click += BtnBrowse_Click;
        btnSend.Click += BtnSend_Click;
        btnSave.Click += BtnSave_Click;
     btnLogout.Click += BtnLogout_Click;
     btnSendMessage.Click += BtnSendMessage_Click;
    }

   private async void BtnSend_Click(object? sender, RoutedEventArgs e)
{
    if (selectedFilePaths.Count == 0)
    {
        txtStatus.Text = "Please select file(s) first";
        return;
    }

    if (lstUsers.SelectedItem == null)
    {
        txtStatus.Text = "Please select receiver";
        return;
    }

    string receiver = lstUsers.SelectedItem.ToString()!;

    try
    {
        foreach (string selectedFilePath in selectedFilePaths)
        {
            string fileName = Path.GetFileName(selectedFilePath);

            FileInfo fileInfo = new FileInfo(selectedFilePath);
            long fileSize = fileInfo.Length;

            string header = $"FILE|{receiver}|{fileName}";
            byte[] headerBytes = Encoding.UTF8.GetBytes(header);

            byte[] headerLengthBytes = BitConverter.GetBytes(headerBytes.Length);
            stream!.Write(headerLengthBytes, 0, headerLengthBytes.Length);

            stream.Write(headerBytes, 0, headerBytes.Length);

            byte[] fileSizeBytes = BitConverter.GetBytes(fileSize);
            stream.Write(fileSizeBytes, 0, fileSizeBytes.Length);

            progressBar.Value = 0;

            using (FileStream fs = new FileStream(selectedFilePath, FileMode.Open, FileAccess.Read))
            {
                byte[] buffer = new byte[64 * 1024];
                int bytesRead;
                long totalSent = 0;

                while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await stream.WriteAsync(buffer, 0, bytesRead);

                    totalSent += bytesRead;

                    int percent = (int)((totalSent * 100.0) / fileSize);

                    Dispatcher.UIThread.Post(() =>
                    {
                        progressBar.Value = percent;
                        txtStatus.Text = $"Sending {fileName}... {percent}%";
                    });
                }
            }
            string timeText = DateTime.Now.ToString("HH:mm:ss");

Dispatcher.UIThread.Post(() =>
{
    lstChat.Items.Add($"[{timeText}] Me sent file: {fileName}");
});
        }

        txtStatus.Text = "All files sent";
    }
    catch (Exception ex)
    {
        txtStatus.Text = $"Send error: {ex.Message}";
    }
}

    private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Chọn file cần gửi",
           AllowMultiple = true
        });

        if (files.Count > 0)
{
    selectedFilePaths.Clear();

    foreach (var file in files)
    {
        selectedFilePaths.Add(file.Path.LocalPath);
    }

    txtFileName.Text = $"Selected {selectedFilePaths.Count} file(s)";
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

                    Dispatcher.UIThread.Post(() =>
                    
                    {
                        
                        progressBar.Value = 0;
                        txtStatus.Text = $"Receiving file from {senderName}...";
                    });
                    

                    byte[] fileData = ReadExact(stream!, fileSize, true);
                    
                 var receivedFile = new ReceivedFileItem
{
    FileName = fileName,
    FileData = fileData,
    SenderName = senderName,
    ReceivedAt = DateTime.Now
};

receivedFiles.Add(receivedFile);
                    string timeText = DateTime.Now.ToString("HH:mm:ss");

Dispatcher.UIThread.Post(() =>
{
    lstChat.Items.Add($"[{timeText}] {senderName} sent file: {fileName}");
});
                    
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
                        txtFileName.Text = fileName;
                        lstChat.Items.Add(receivedFile);
                    });
                }
                else if (header.StartsWith("CHAT|"))
{
    string[] parts = header.Split('|', 4);

    string sender = parts[1];
    int deleteAfterMinutes = int.Parse(parts[2]);
    string message = parts[3];

    string timeText = DateTime.Now.ToString("HH:mm");

    string displayText = deleteAfterMinutes > 0
        ? $"[{timeText}] {sender}: {message} (deleted after {deleteAfterMinutes} minutes)"
        : $"[{timeText}] {sender}: {message}";

    Dispatcher.UIThread.Post(() =>
    {
        lstChat.Items.Add(displayText);

        if (deleteAfterMinutes > 0)
        {
            _ = DeleteMessageAfterDelay(displayText, deleteAfterMinutes);
        }
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

    private byte[] ReadExact(NetworkStream stream, long size, bool isFileData = false)
    {
        byte[] buffer = new byte[size];
        int totalRead = 0;

        while (totalRead < size)
        {
            int read = stream.Read(buffer, totalRead, (int)(size - totalRead));

            if (read <= 0)
                throw new Exception("Connection closed");

            totalRead += read;

            if (isFileData && size > 0)
            {
                int percent = (int)((totalRead * 100.0) / size);
                Dispatcher.UIThread.Post(() =>
                {
                    progressBar.Value = percent;
                    txtStatus.Text = $"Receiving... {percent}%";
                });
            }
        }

        return buffer;
    }

    private async void BtnSave_Click(object? sender, RoutedEventArgs e)
{
    if (lstChat.SelectedItem is not ReceivedFileItem selectedFile)
    {
        txtStatus.Text = "Please select a received file in chat";
        return;
    }

    var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
    {
        Title = "Save received file",
        SuggestedFileName = selectedFile.FileName
    });

    if (file == null)
    {
        txtStatus.Text = "Save cancelled";
        return;
    }

    string savePath = file.Path.LocalPath;

    File.WriteAllBytes(savePath, selectedFile.FileData);

    txtStatus.Text = $"File saved: {Path.GetFileName(savePath)}";
}

    private void BtnLogout_Click(object? sender, RoutedEventArgs e)
    {
        if (stream == null || client == null || !client.Connected)
        {
            txtStatus.Text = "Not connected";
            return;
        }

        try
        {
            string header = $"LOGOUT|{txtUsername.Text}";
            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
            byte[] headerLengthBytes = BitConverter.GetBytes(headerBytes.Length);

            stream.Write(headerLengthBytes, 0, headerLengthBytes.Length);
            stream.Write(headerBytes, 0, headerBytes.Length);

            stream.Close();
            client.Close();
            
            lstUsers.Items.Clear();
            txtStatus.Text = "Disconnected (Logged out)";
            txtUsername.Text = ""; 
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Logout error: {ex.Message}";
        }
    }
private void BtnSendMessage_Click(object? sender, RoutedEventArgs e)
{
    if (lstUsers.SelectedItem == null)
    {
        txtStatus.Text = "Please select receiver";
        return;
    }

    if (string.IsNullOrWhiteSpace(txtMessage.Text))
    {
        txtStatus.Text = "Please enter message";
        return;
    }

    string receiver = lstUsers.SelectedItem.ToString()!;
var parsed = ParseChatMessage(txtMessage.Text!);

string message = parsed.Message;
int deleteAfterMinutes = parsed.DeleteAfterMinutes;
string header = $"CHAT|{receiver}|{deleteAfterMinutes}|{message}";    byte[] headerBytes = Encoding.UTF8.GetBytes(header);
    byte[] headerLengthBytes = BitConverter.GetBytes(headerBytes.Length);

    stream!.Write(headerLengthBytes, 0, headerLengthBytes.Length);
    stream.Write(headerBytes, 0, headerBytes.Length);

string timeText = DateTime.Now.ToString("HH:mm");

string displayText = deleteAfterMinutes > 0
    ? $"[{timeText}] Me: {message} (deleted after {deleteAfterMinutes} minutes)"
    : $"[{timeText}] Me: {message}";

lstChat.Items.Add(displayText);    txtMessage.Text = "";
if (deleteAfterMinutes > 0)
{
    _ = DeleteMessageAfterDelay(displayText, deleteAfterMinutes);
}
}
private (string Message, int DeleteAfterMinutes) ParseChatMessage(string input)
{
    int deleteAfterMinutes = 0;
    string message = input;

    if (input.StartsWith("/del "))
    {
        string[] parts = input.Split(' ', 3);

        if (parts.Length == 3 && int.TryParse(parts[1], out deleteAfterMinutes))
        {
            message = parts[2];
        }
    }

    return (message, deleteAfterMinutes);
}
private async Task DeleteMessageAfterDelay(string text, int minutes)
{
    await Task.Delay(TimeSpan.FromMinutes(minutes));

    Dispatcher.UIThread.Post(() =>
    {
        lstChat.Items.Remove(text);
    });
}
public class ReceivedFileItem
{
    public string FileName { get; set; } = "";
    public byte[] FileData { get; set; } = Array.Empty<byte>();
    public string SenderName { get; set; } = "";
    public DateTime ReceivedAt { get; set; }

    public override string ToString()
    {
        return $"[{ReceivedAt:HH:mm:ss}] {SenderName} sent file: {FileName}";
    }
}
}

