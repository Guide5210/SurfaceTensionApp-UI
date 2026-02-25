using System.IO;
using System.IO.Ports;
using System.Text;

namespace SurfaceTensionApp.Services;

/// <summary>
/// Serial communication service using DataReceived event (no blocking read thread).
/// This avoids BaseStream locking issues between read and write.
/// </summary>
public class SerialService : IDisposable
{
    private SerialPort? _port;
    private readonly StringBuilder _lineBuffer = new(512);
    private readonly object _bufferLock = new();

    public bool IsConnected => _port is { IsOpen: true };
    public string PortName => _port?.PortName ?? "";

    public event Action<string>? LineReceived;
    public event Action<bool>? ConnectionChanged;

    public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

    public bool Connect(string portName, int baudRate = 115200)
    {
        Disconnect();
        try
        {
            _port = new SerialPort(portName, baudRate)
            {
                ReadTimeout = 500,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true,
                Encoding = Encoding.ASCII,
                NewLine = "\n",
                ReceivedBytesThreshold = 1,
            };

            _port.DataReceived += OnDataReceived;
            _port.Open();

            // Wait for Arduino to boot after DTR reset
            Thread.Sleep(2000);

            ConnectionChanged?.Invoke(true);
            return true;
        }
        catch
        {
            if (_port != null) _port.DataReceived -= OnDataReceived;
            _port?.Dispose();
            _port = null;
            ConnectionChanged?.Invoke(false);
            return false;
        }
    }

    public void Disconnect()
    {
        try
        {
            if (_port != null)
            {
                _port.DataReceived -= OnDataReceived;
                if (_port.IsOpen) _port.Close();
            }
        }
        catch { }
        _port?.Dispose();
        _port = null;
        ConnectionChanged?.Invoke(false);
    }

    public bool Send(string data)
    {
        try
        {
            if (_port is { IsOpen: true })
            {
                byte[] bytes = Encoding.ASCII.GetBytes(data + "\n");
                _port.Write(bytes, 0, bytes.Length);
                return true;
            }
        }
        catch { }
        return false;
    }

    public bool Send(char c) => Send(c.ToString());

    public void Flush()
    {
        try
        {
            if (_port is { IsOpen: true })
            {
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
            }
        }
        catch { }
    }

    /// <summary>
    /// Event-driven data receive — no blocking thread, no BaseStream conflict.
    /// </summary>
    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            if (_port == null || !_port.IsOpen) return;

            string incoming = _port.ReadExisting();
            if (string.IsNullOrEmpty(incoming)) return;

            lock (_bufferLock)
            {
                foreach (char c in incoming)
                {
                    if (c == '\n')
                    {
                        string line = _lineBuffer.ToString().TrimEnd('\r');
                        _lineBuffer.Clear();
                        if (line.Length > 0)
                            LineReceived?.Invoke(line);
                    }
                    else if (c != '\r')
                    {
                        _lineBuffer.Append(c);
                        if (_lineBuffer.Length > 4096)
                        {
                            string line = _lineBuffer.ToString();
                            _lineBuffer.Clear();
                            if (line.Length > 0)
                                LineReceived?.Invoke(line);
                        }
                    }
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
