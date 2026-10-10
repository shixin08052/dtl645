using Dlt645.Core.Communication;
using Dlt645.Core.Errors;
using Dlt645.Core.Protocol;
using Dlt645.Core.Transport;

// 用法：Harness <串口路径> <电表是否接受公共地址读数据 0/1>
// 用软件真实的串口层与假电表通讯，任何不符合预期的结果都返回非 0 退出码。
var port = args[0];
bool wildcardAccepted = args[1] == "1";
var expectedAddress = "000012345678";
int failures = 0;

var transport = new SerialPortTransport(new SerialSettings { PortName = port, BaudRate = 1200 });
transport.Open();
var client = new MeterClient(transport, new CommOptions { TimeoutMs = 1500, Retries = 0 });
client.FrameLogged += e => Console.WriteLine($"   [{e.KindText}] {e.HexText}  {e.Text}");

async Task Check(string title, Func<Task<string>> action, string? expect, bool expectFailure = false)
{
    Console.WriteLine($"== {title}");
    try
    {
        var result = await action();
        bool ok = !expectFailure && (expect is null || result == expect);
        Console.WriteLine($"   {(ok ? "通过" : "不符合预期")}：{result}");
        if (!ok) failures++;
    }
    catch (MeterException ex)
    {
        Console.WriteLine($"   {(expectFailure ? "通过（预期失败）" : "不符合预期")}：{ex.Message}");
        if (!expectFailure) failures++;
    }
}

await Check("读取表号（13H，公共地址）", async () => (await client.ReadAddressAsync()).Text, expectedAddress);
await Check("用实际表号读正向有功总电能", async () =>
    Hex.ToHex((await client.ReadDataAsync(MeterAddress.Parse(expectedAddress), 0x00010000)).Data), "56 34 12 00");
await Check("直接用公共地址读数据", async () =>
    Hex.ToHex((await client.ReadDataAsync(MeterAddress.Wildcard, 0x00000000)).Data), "56 34 12 00", expectFailure: !wildcardAccepted);
await Check("公共地址：先解析实际表号再读数据（软件实际流程）", async () =>
{
    var actual = await client.ResolveAddressAsync(MeterAddress.Wildcard);
    return Hex.ToHex((await client.ReadDataAsync(actual, 0x00010000)).Data);
}, "56 34 12 00");

transport.Close();
Console.WriteLine(failures == 0 ? "全部通过" : $"失败 {failures} 项");
return failures == 0 ? 0 : 1;
