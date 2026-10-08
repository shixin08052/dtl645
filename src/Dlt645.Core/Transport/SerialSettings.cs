using System.IO.Ports;

namespace Dlt645.Core.Transport;

/// <summary>串口参数。DL/T 645 红外口默认 2400bps、8 数据位、偶校验、1 停止位。</summary>
public sealed record SerialSettings
{
    public const int DefaultBaudRate = 2400;
    public const int DefaultDataBits = 8;
    public const Parity DefaultParity = Parity.Even;
    public const StopBits DefaultStopBits = StopBits.One;

    public static IReadOnlyList<int> BaudRates { get; } = new[] { 1200, 2400, 4800, 9600 };

    public string PortName { get; init; } = string.Empty;
    public int BaudRate { get; init; } = DefaultBaudRate;
    public int DataBits { get; init; } = DefaultDataBits;
    public Parity Parity { get; init; } = DefaultParity;
    public StopBits StopBits { get; init; } = DefaultStopBits;

    public static string ParityText(Parity p) => p switch
    {
        Parity.None => "N",
        Parity.Odd => "O",
        Parity.Even => "E",
        Parity.Mark => "M",
        Parity.Space => "S",
        _ => "?",
    };

    public static string StopBitsText(StopBits s) => s switch
    {
        StopBits.One => "1",
        StopBits.OnePointFive => "1.5",
        StopBits.Two => "2",
        _ => "0",
    };

    public override string ToString() =>
        $"{PortName} {BaudRate},{DataBits},{ParityText(Parity)},{StopBitsText(StopBits)}";
}
