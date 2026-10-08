namespace Dlt645.Core.Transport;

/// <summary>
/// 串口通讯层抽象。真实串口与模拟电表都实现该接口，协议层不关心底层是什么。
/// 所有方法在出错时抛出 <see cref="Errors.MeterException"/>。
/// </summary>
public interface ISerialTransport : IDisposable
{
    string DisplayName { get; }

    bool IsOpen { get; }

    void Open();

    void Close();

    /// <summary>清空接收缓冲区（发送新请求前调用，丢弃残留数据）。</summary>
    void DiscardInput();

    void Write(byte[] data);

    /// <summary>读取已到达的字节；在 <paramref name="timeoutMs"/> 内无数据返回 0。</summary>
    int Read(byte[] buffer, int timeoutMs);
}
