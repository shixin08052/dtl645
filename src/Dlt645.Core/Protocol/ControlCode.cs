namespace Dlt645.Core.Protocol;

/// <summary>
/// DL/T 645-2007 控制码。
/// D7：传送方向（0 主站发出，1 从站应答）；D6：应答标志（0 正常，1 异常）；
/// D5：后续帧标志（1 有后续数据）；D4~D0：功能码。
/// </summary>
public static class ControlCode
{
    public const byte ReadData = 0x11;
    public const byte ReadFollow = 0x12;
    public const byte ReadAddress = 0x13;

    public const byte ReadDataOk = 0x91;
    public const byte ReadDataOkMore = 0xB1;
    public const byte ReadDataError = 0xD1;

    public const byte ReadFollowOk = 0x92;
    public const byte ReadFollowOkMore = 0xB2;
    public const byte ReadFollowError = 0xD2;

    public const byte ReadAddressOk = 0x93;

    public static bool IsResponse(byte c) => (c & 0x80) != 0;
    public static bool IsAbnormal(byte c) => (c & 0x40) != 0;
    public static bool HasFollow(byte c) => (c & 0x20) != 0;
    public static byte Function(byte c) => (byte)(c & 0x1F);

    public static string FunctionName(byte c) => Function(c) switch
    {
        0x08 => "广播校时",
        0x11 => "读数据",
        0x12 => "读后续数据",
        0x13 => "读通信地址",
        0x14 => "写数据",
        0x15 => "写通信地址",
        0x16 => "冻结命令",
        0x17 => "更改通信速率",
        0x18 => "修改密码",
        0x19 => "最大需量清零",
        0x1A => "电表清零",
        0x1B => "事件清零",
        _ => $"功能码{Function(c):X2}H",
    };
}
