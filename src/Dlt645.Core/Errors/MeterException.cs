namespace Dlt645.Core.Errors;

/// <summary>带错误码的业务异常。</summary>
public sealed class MeterException : Exception
{
    public MeterException(ErrorCode code, string? detail = null, Exception? inner = null)
        : base(BuildMessage(code, detail), inner)
    {
        Code = code;
        Detail = detail;
    }

    public ErrorCode Code { get; }

    /// <summary>补充说明（例如校验和计算值、错误字解析）。</summary>
    public string? Detail { get; }

    /// <summary>异常应答时的原始错误字。</summary>
    public byte? MeterErrorWord { get; init; }

    public ErrorInfo Info => ErrorCatalog.Get(Code);

    /// <summary>串口层面的故障：需要更新连接状态为“异常断开”。</summary>
    public bool IsPortFault => Code is ErrorCode.PortDisconnected or ErrorCode.PortWriteFailed;

    /// <summary>可以通过重发解决的通讯错误。</summary>
    public bool IsRetryable => Code is ErrorCode.Timeout or ErrorCode.ChecksumError or ErrorCode.IncompleteFrame
        or ErrorCode.FrameFormatError or ErrorCode.UnexpectedResponse or ErrorCode.DiMismatch;

    private static string BuildMessage(ErrorCode code, string? detail)
    {
        var title = ErrorCatalog.Get(code).Title;
        return string.IsNullOrEmpty(detail)
            ? $"[{code.ToCodeString()}] {title}"
            : $"[{code.ToCodeString()}] {title}：{detail}";
    }
}
