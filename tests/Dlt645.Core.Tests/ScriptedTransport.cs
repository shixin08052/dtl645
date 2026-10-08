using Dlt645.Core.Transport;

namespace Dlt645.Core.Tests;

/// <summary>按脚本应答的串口：每次 Write 后把下一组预设字节放入接收缓冲。</summary>
internal sealed class ScriptedTransport : ISerialTransport
{
    private readonly Queue<byte[]?> _responses;
    private readonly List<byte> _rx = new();

    public ScriptedTransport(bool echo, params byte[]?[] responses)
    {
        Echo = echo;
        _responses = new Queue<byte[]?>(responses);
    }

    public bool Echo { get; }
    public List<byte[]> Written { get; } = new();
    public string DisplayName => "脚本串口";
    public bool IsOpen { get; private set; } = true;

    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
    public void DiscardInput() => _rx.Clear();

    public void Write(byte[] data)
    {
        Written.Add(data);
        if (Echo) _rx.AddRange(data);
        if (_responses.Count > 0 && _responses.Dequeue() is { } resp) _rx.AddRange(resp);
    }

    public int Read(byte[] buffer, int timeoutMs)
    {
        if (_rx.Count == 0)
        {
            Thread.Sleep(Math.Min(timeoutMs, 5));
            return 0;
        }
        int n = Math.Min(Math.Min(buffer.Length, 5), _rx.Count);
        _rx.CopyTo(0, buffer, 0, n);
        _rx.RemoveRange(0, n);
        return n;
    }

    public void Dispose() { }
}
