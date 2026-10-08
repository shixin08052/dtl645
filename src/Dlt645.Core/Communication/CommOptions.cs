namespace Dlt645.Core.Communication;

/// <summary>通讯参数。</summary>
public sealed class CommOptions
{
    public const int DefaultTimeoutMs = 1500;
    public const int DefaultRetries = 2;

    /// <summary>每次发送后等待应答的超时时间。</summary>
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    /// <summary>失败后的重试次数（不含第一次）。</summary>
    public int Retries { get; set; } = DefaultRetries;

    /// <summary>发送前是否加 4 个 FE 前导字节。</summary>
    public bool SendPreamble { get; set; } = true;

    /// <summary>
    /// 已收到损坏帧（校验/帧尾错误）后，若这段时间内没有新字节，提前结束等待以便尽快重试。
    /// </summary>
    public int ErrorSettleMs { get; set; } = 400;

    /// <summary>后续帧最多读取的帧数，防止异常电表无限返回后续帧。</summary>
    public int MaxFollowFrames { get; set; } = 64;
}
