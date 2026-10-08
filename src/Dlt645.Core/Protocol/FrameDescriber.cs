namespace Dlt645.Core.Protocol;

/// <summary>把帧翻译成一句中文说明，供报文监视窗口使用。</summary>
public static class FrameDescriber
{
    public static string Describe(Dlt645Frame f, Func<uint, string?>? diNamer = null)
    {
        string addr = f.MeterAddress.Text;
        string Di(int offset)
        {
            if (f.Data.Length < offset + 4) return "DI=?";
            uint di = DataId.FromWireBytes(f.Data.AsSpan(offset, 4));
            var name = diNamer?.Invoke(di);
            return name is null ? $"DI={DataId.Format(di)}" : $"DI={DataId.Format(di)}（{name}）";
        }
        string Payload(int from, int trim = 0)
        {
            int n = f.Data.Length - from - trim;
            return n <= 0 ? "无数据" : $"数据[{n}字节]={Hex.ToHex(f.Data.AsSpan(from, n))}";
        }

        switch (f.Control)
        {
            case ControlCode.ReadAddress:
                return $"读通信地址 地址={addr}";
            case ControlCode.ReadAddressOk:
                return f.Data.Length >= 6
                    ? $"应答通信地址：{MeterAddress.FromFrameBytes(f.Data)}"
                    : $"应答通信地址（数据长度异常 {f.Data.Length} 字节）";
            case ControlCode.ReadData:
                return $"读数据 地址={addr} {Di(0)}";
            case ControlCode.ReadDataOk:
            case ControlCode.ReadDataOkMore:
                return $"正常应答{(ControlCode.HasFollow(f.Control) ? "（有后续帧）" : "")} 地址={addr} {Di(0)} {Payload(4)}";
            case ControlCode.ReadFollow:
                return $"读后续数据 地址={addr} {Di(0)} 帧序号={(f.Data.Length >= 5 ? f.Data[4] : 0)}";
            case ControlCode.ReadFollowOk:
            case ControlCode.ReadFollowOkMore:
                return $"后续帧应答{(ControlCode.HasFollow(f.Control) ? "（仍有后续）" : "（最后一帧）")} 地址={addr} {Di(0)} " +
                       $"{Payload(4, 1)} 帧序号={(f.Data.Length >= 5 ? f.Data[^1] : 0)}";
            case ControlCode.ReadDataError:
            case ControlCode.ReadFollowError:
                return $"异常应答 地址={addr} " + (f.Data.Length > 0 ? ErrorWord.Describe(f.Data[0]) : "（无错误字）");
        }

        string dir = f.IsResponse ? "应答" : "请求";
        string abn = ControlCode.IsAbnormal(f.Control) ? "异常" : "";
        return $"{abn}{dir}·{ControlCode.FunctionName(f.Control)}（控制码 {f.Control:X2}H） 地址={addr} 数据长度={f.Data.Length}";
    }
}
