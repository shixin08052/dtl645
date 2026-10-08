namespace Dlt645.Core.Protocol;

/// <summary>一条完整的 DL/T 645 帧（68 A0..A5 68 C L DATA CS 16）。</summary>
public sealed class Dlt645Frame
{
    public Dlt645Frame(byte[] address, byte control, byte[] data, byte[] raw)
    {
        Address = address;
        Control = control;
        Data = data;
        Raw = raw;
    }

    /// <summary>帧内顺序 A0..A5。</summary>
    public byte[] Address { get; }

    public byte Control { get; }

    /// <summary>数据域（已减 33H）。</summary>
    public byte[] Data { get; }

    /// <summary>从第一个 68H 到 16H 的原始字节。</summary>
    public byte[] Raw { get; }

    public MeterAddress MeterAddress => MeterAddress.FromFrameBytes(Address);

    public bool IsResponse => ControlCode.IsResponse(Control);
}
