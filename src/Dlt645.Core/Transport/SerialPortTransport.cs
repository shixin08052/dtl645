using System.IO.Ports;
using Dlt645.Core.Errors;

namespace Dlt645.Core.Transport;

/// <summary>基于 System.IO.Ports.SerialPort 的真实串口实现。</summary>
public sealed class SerialPortTransport : ISerialTransport
{
    private SerialPort? _port;

    public SerialPortTransport(SerialSettings settings) => Settings = settings;

    public SerialSettings Settings { get; }

    public string DisplayName => Settings.ToString();

    public bool IsOpen
    {
        get
        {
            try { return _port?.IsOpen == true; }
            catch { return false; }
        }
    }

    public void Open()
    {
        if (string.IsNullOrWhiteSpace(Settings.PortName))
            throw new MeterException(ErrorCode.PortNotSelected);

        Close();
        var port = new SerialPort(Settings.PortName, Settings.BaudRate, Settings.Parity, Settings.DataBits, Settings.StopBits)
        {
            Handshake = Handshake.None,
            ReadTimeout = 100,
            WriteTimeout = 2000,
            ReadBufferSize = 4096,
            WriteBufferSize = 1024,
        };
        try
        {
            port.Open();
        }
        catch (UnauthorizedAccessException ex)
        {
            port.Dispose();
            throw new MeterException(ErrorCode.PortBusy, $"{Settings.PortName} 已被其他程序占用", ex);
        }
        catch (FileNotFoundException ex)
        {
            port.Dispose();
            throw new MeterException(ErrorCode.PortDisconnected, $"{Settings.PortName} 不存在，设备可能已拔出", ex);
        }
        catch (IOException ex)
        {
            port.Dispose();
            bool exists = SerialPort.GetPortNames().Contains(Settings.PortName, StringComparer.OrdinalIgnoreCase);
            throw exists
                ? new MeterException(ErrorCode.PortOpenFailed, ex.Message, ex)
                : new MeterException(ErrorCode.PortDisconnected, $"{Settings.PortName} 不存在，设备可能已拔出", ex);
        }
        catch (ArgumentException ex)
        {
            port.Dispose();
            throw new MeterException(ErrorCode.PortOpenFailed, $"串口参数无效：{ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            port.Dispose();
            throw new MeterException(ErrorCode.PortOpenFailed, ex.Message, ex);
        }
        _port = port;
    }

    public void Close()
    {
        var p = _port;
        _port = null;
        if (p is null) return;
        try { if (p.IsOpen) p.Close(); } catch { /* 拔出后关闭可能抛异常，忽略 */ }
        try { p.Dispose(); } catch { }
    }

    public void DiscardInput()
    {
        var p = RequirePort();
        try { p.DiscardInBuffer(); }
        catch (Exception ex) when (IsPortGone(ex)) { throw Disconnected(ex); }
    }

    public void Write(byte[] data)
    {
        var p = RequirePort();
        try { p.Write(data, 0, data.Length); }
        catch (TimeoutException ex) { throw new MeterException(ErrorCode.PortWriteFailed, "发送超时", ex); }
        catch (Exception ex) when (IsPortGone(ex)) { throw Disconnected(ex); }
    }

    public int Read(byte[] buffer, int timeoutMs)
    {
        var p = RequirePort();
        try
        {
            p.ReadTimeout = Math.Max(1, timeoutMs);
            return p.Read(buffer, 0, buffer.Length);
        }
        catch (TimeoutException)
        {
            return 0;
        }
        catch (Exception ex) when (IsPortGone(ex))
        {
            throw Disconnected(ex);
        }
    }

    public void Dispose() => Close();

    private SerialPort RequirePort()
    {
        var p = _port;
        if (p is null) throw new MeterException(ErrorCode.NotConnected);
        bool open;
        try { open = p.IsOpen; } catch { open = false; }
        if (!open) throw Disconnected(null);
        return p;
    }

    private static bool IsPortGone(Exception ex) =>
        ex is IOException or InvalidOperationException or UnauthorizedAccessException or ObjectDisposedException;

    private MeterException Disconnected(Exception? ex) =>
        new(ErrorCode.PortDisconnected, $"{Settings.PortName} 通讯中断：{ex?.Message ?? "串口已关闭"}", ex);
}
