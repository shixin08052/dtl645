namespace Dlt645.Core.Errors;

/// <summary>
/// 错误码。1xx 串口，2xx 通讯/帧，3xx 电表应答，4xx 输入与解析，5xx 文件，9xx 其他。
/// 界面上显示为 “E201” 这样的形式，方便现场人员向工程师报告。
/// </summary>
public enum ErrorCode
{
    None = 0,

    PortNotSelected = 101,
    PortBusy = 102,
    PortOpenFailed = 103,
    PortDisconnected = 104,
    PortWriteFailed = 105,
    Ch340NotFound = 106,
    NotConnected = 107,

    Timeout = 201,
    ChecksumError = 202,
    IncompleteFrame = 203,
    FrameFormatError = 204,
    AddressMismatch = 205,
    UnexpectedResponse = 206,
    DiMismatch = 207,
    FollowFrameFailed = 208,

    MeterErrorResponse = 301,

    InvalidAddressInput = 401,
    InvalidDiInput = 402,
    DecodeFailed = 403,

    ExportFailed = 501,
    LogWriteFailed = 502,

    Cancelled = 900,
    Unknown = 999,
}

public static class ErrorCodeExtensions
{
    public static string ToCodeString(this ErrorCode code) => code == ErrorCode.None ? "" : $"E{(int)code:D3}";
}
