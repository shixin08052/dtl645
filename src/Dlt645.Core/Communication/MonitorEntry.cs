namespace Dlt645.Core.Communication;

public enum MonitorKind
{
    /// <summary>发送</summary>
    Tx,
    /// <summary>接收（有效应答）</summary>
    Rx,
    /// <summary>红外回显（已过滤）</summary>
    Echo,
    /// <summary>一般信息</summary>
    Info,
    /// <summary>警告</summary>
    Warning,
    /// <summary>错误</summary>
    Error,
}

/// <summary>报文监视窗口中的一条记录。</summary>
public sealed record MonitorEntry(DateTime Time, MonitorKind Kind, byte[]? Bytes, string Text)
{
    public string KindText => Kind switch
    {
        MonitorKind.Tx => "发送",
        MonitorKind.Rx => "接收",
        MonitorKind.Echo => "回显",
        MonitorKind.Info => "信息",
        MonitorKind.Warning => "警告",
        _ => "错误",
    };

    public string TimeText => Time.ToString("HH:mm:ss.fff");

    public string HexText => Bytes is null ? string.Empty : Protocol.Hex.ToHex(Bytes);

    // 每条记录都是独立的一行：按引用比较，避免内容相同的两行在列表控件中被视为同一项
    public bool Equals(MonitorEntry? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    public override string ToString() =>
        Bytes is null
            ? $"{Time:yyyy-MM-dd HH:mm:ss.fff} [{KindText}] {Text}"
            : $"{Time:yyyy-MM-dd HH:mm:ss.fff} [{KindText}] {HexText}    // {Text}";
}

/// <summary>一次完整事务（含重试、后续帧）的结果，供状态栏显示。</summary>
public sealed record TransactionInfo(string Operation, bool Success, Errors.ErrorCode Code, string Message, TimeSpan Elapsed);
