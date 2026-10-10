namespace Dlt645.Core.Errors;

/// <summary>一条错误的通俗说明：标题、可能原因、排查建议。</summary>
public sealed record ErrorInfo(ErrorCode Code, string Title, IReadOnlyList<string> Causes, IReadOnlyList<string> Suggestions)
{
    public string CodeText => Code.ToCodeString();
}

/// <summary>错误码 → 中文提示的对照表。</summary>
public static class ErrorCatalog
{
    private static readonly Dictionary<ErrorCode, ErrorInfo> Map = new();

    static ErrorCatalog()
    {
        Add(ErrorCode.PortNotSelected, "未选择串口",
            new[] { "串口下拉框为空，或尚未选择红外通讯头对应的 COM 口" },
            new[] { "插好红外通讯头后点击“刷新”，选择标有【CH340】的串口", "不接硬件时可勾选“模拟电表”进行演示" });

        Add(ErrorCode.PortBusy, "串口被占用",
            new[] { "该串口已被其他程序打开（如其他抄表软件、串口调试助手）", "本软件已在另一个窗口中运行" },
            new[] { "关闭其他可能使用该串口的软件后重试", "仍不行时拔下红外头，等待 3 秒后重新插入，再点击“刷新”" });

        Add(ErrorCode.PortOpenFailed, "串口打开失败",
            new[] { "串口参数不被设备支持", "驱动异常或设备工作不正常" },
            new[] { "点击“恢复默认”后重试", "在设备管理器中检查该端口是否有黄色感叹号，必要时重新安装 CH340 驱动" });

        Add(ErrorCode.PortDisconnected, "串口异常断开",
            new[] { "红外通讯头 USB 被拔出或接触不良", "USB 延长线或扩展坞供电不足", "驱动异常导致端口消失" },
            new[] { "检查 USB 插头是否松动，重新插好后点击“刷新”并重新打开串口", "尽量直接插在电脑 USB 口上，不要经过无源 HUB" });

        Add(ErrorCode.PortWriteFailed, "串口发送失败",
            new[] { "串口发送超时，设备可能已断开或驱动无响应" },
            new[] { "关闭串口后重新打开；仍失败请重新插拔红外头" });

        Add(ErrorCode.Ch340NotFound, "未检测到 CH340 设备",
            new[] { "红外通讯头未插入电脑", "CH340 驱动未安装，设备在设备管理器中显示为“未知设备”或带黄色感叹号", "USB 线接触不良" },
            new[] { "确认红外通讯头已插入，指示灯亮", "打开左侧“驱动检测”页，查看设备和驱动状态，必要时点击“安装驱动”", "如使用其他芯片的通讯头，也可直接在列表中选择对应串口" });

        Add(ErrorCode.NotConnected, "串口未打开",
            new[] { "尚未打开串口，或串口已断开" },
            new[] { "先选择串口并点击“打开串口”", "不接硬件时可勾选“模拟电表”" });

        Add(ErrorCode.Ch340DriverMissing, "CH340 驱动未安装或异常",
            new[] { "电脑上没有安装 CH340/CH341 驱动（设备管理器中显示为“USB Serial”或带黄色感叹号）", "驱动版本过旧或已损坏", "设备在设备管理器中被禁用" },
            new[] { "打开左侧“驱动检测”页，点击“安装驱动”（需要管理员权限，仅首次安装需要）", "离线电脑：把沁恒官网的 CH341SER.EXE 放到本程序同一文件夹，再点“安装驱动”", "安装后拔下红外通讯头，等待 3 秒再插回，然后点“刷新”" });

        Add(ErrorCode.DriverInstallFailed, "驱动安装失败",
            new[] { "没有管理员权限，或在系统弹出的“是否允许更改”提示中选择了“否”", "驱动安装包不完整或与系统不匹配", "安全软件拦截了驱动安装" },
            new[] { "重新点击“安装驱动”，在系统提示中选择“是”", "从沁恒官网重新下载 CH341SER.EXE", "请电脑管理员协助安装驱动" });

        Add(ErrorCode.Timeout, "超时无应答",
            new[] { "红外头没有对准电表的红外通讯窗口", "距离不合适（建议贴近，一般 1~5 厘米以内）", "阳光或强灯光直射造成干扰", "表号不正确（电表只响应自己的表号或公共地址）", "波特率或校验位与电表不一致（电表红外口一般为 1200bps、8 数据位、偶校验、1 停止位）", "部分电表红外口需要先按键唤醒" },
            new[] { "重新对准红外窗口并保持稳定", "用手或遮光罩挡住强光后重试", "先用“读取表号”（公共地址）确认电表能应答", "点“恢复默认”确认参数为 1200 / 8 / 偶校验 / 1", "适当增加超时时间或重试次数" });

        Add(ErrorCode.ChecksumError, "校验错误",
            new[] { "通讯过程受到强光或电磁干扰，数据位出错", "红外头对准不稳定" },
            new[] { "重新对准后重试", "遮挡环境光，保持红外头静止" });

        Add(ErrorCode.IncompleteFrame, "帧不完整",
            new[] { "通讯被干扰中断，只收到部分数据", "超时时间过短，数据未收完" },
            new[] { "重新对准后重试", "适当增加超时时间（例如 2000ms）" });

        Add(ErrorCode.FrameFormatError, "帧格式错误",
            new[] { "收到的数据不符合 DL/T 645 帧格式（帧头、帧尾或长度异常）", "受到干扰或波特率不匹配" },
            new[] { "重新对准后重试", "确认波特率、校验位设置与电表一致" });

        Add(ErrorCode.AddressMismatch, "应答表号与请求不一致",
            new[] { "输入的表号与实际电表不符", "附近有其他电表同时应答" },
            new[] { "核对表号，或点击“读取表号”自动获取", "读取时只对准一块电表" });

        Add(ErrorCode.UnexpectedResponse, "应答类型不符",
            new[] { "收到的应答控制码与请求不对应（可能是上一次通讯残留的数据）" },
            new[] { "稍等片刻后重试" });

        Add(ErrorCode.DiMismatch, "应答数据标识不一致",
            new[] { "应答中的数据标识与请求不同，可能是残留数据或电表实现不规范" },
            new[] { "重试一次；若持续出现请导出诊断信息发给工程师" });

        Add(ErrorCode.FollowFrameFailed, "后续帧读取失败",
            new[] { "数据较长需要分多帧传输，后续帧通讯失败" },
            new[] { "保持红外头对准不动，重新读取" });

        Add(ErrorCode.MeterErrorResponse, "电表返回异常应答",
            new[] { "电表不支持该数据项（无请求数据）", "费率号或结算日超出电表配置", "该数据需要权限才能读取" },
            new[] { "查看下方错误字解析", "“无请求数据”通常表示该表不支持此项，可忽略", "本软件只读，不支持需要密码的操作" });

        Add(ErrorCode.InvalidAddressInput, "表号格式错误",
            new[] { "表号应为 12 位数字，或 AAAAAAAAAAAA 公共地址" },
            new[] { "不足 12 位会自动左补 0；如不知道表号，点击“重置为公共表号”再“读取表号”" });

        Add(ErrorCode.InvalidDiInput, "数据标识格式错误",
            new[] { "数据标识应为 8 位十六进制数，如 00010000" },
            new[] { "按 DI3 DI2 DI1 DI0 的顺序输入，可带空格" });

        Add(ErrorCode.DecodeFailed, "数据解析失败",
            new[] { "数据长度与配置表不一致，或不是有效的 BCD 码" },
            new[] { "查看“原始数据”列；如需支持该电表，请将诊断信息发给工程师调整配置表" });

        Add(ErrorCode.ExportFailed, "导出失败",
            new[] { "文件正被 Excel 等程序打开", "目标文件夹没有写入权限" },
            new[] { "关闭已打开的同名文件后重试", "换一个文件夹（如桌面）保存" });

        Add(ErrorCode.LogWriteFailed, "日志写入失败",
            new[] { "程序目录没有写入权限" },
            new[] { "把程序放到桌面或 D 盘等有权限的文件夹运行" });

        Add(ErrorCode.Cancelled, "已取消",
            new[] { "用户取消了读取" },
            Array.Empty<string>());

        Add(ErrorCode.Unknown, "未知错误",
            new[] { "程序内部异常" },
            new[] { "点击“导出诊断信息”，把生成的文件发给工程师" });
    }

    private static void Add(ErrorCode code, string title, string[] causes, string[] suggestions) =>
        Map[code] = new ErrorInfo(code, title, causes, suggestions);

    public static ErrorInfo Get(ErrorCode code) =>
        Map.TryGetValue(code, out var info) ? info : Map[ErrorCode.Unknown];
}
