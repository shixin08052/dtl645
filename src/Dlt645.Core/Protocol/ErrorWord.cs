namespace Dlt645.Core.Protocol;

/// <summary>异常应答（D1H/D2H）中的错误信息字 ERR 解析。</summary>
public static class ErrorWord
{
    private static readonly string[] BitNames =
    {
        "其他错误",
        "无请求数据",
        "密码错或未授权",
        "通信速率不能更改",
        "年时区数超",
        "日时段数超",
        "费率数超",
        "保留位",
    };

    /// <summary>逐位列出置 1 的错误项。</summary>
    public static IReadOnlyList<string> Decode(byte err)
    {
        var list = new List<string>();
        for (int bit = 0; bit < 8; bit++)
        {
            if ((err & (1 << bit)) != 0) list.Add($"Bit{bit} {BitNames[bit]}");
        }
        if (list.Count == 0) list.Add("错误字为 0（未指明原因）");
        return list;
    }

    public static string Describe(byte err) => $"错误字 {err:X2}H：" + string.Join("；", Decode(err));
}
