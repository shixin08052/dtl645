using Dlt645.Core.Communication;
using Dlt645.Core.DataItems;
using Dlt645.Core.Errors;
using Dlt645.Core.Protocol;
using Dlt645.Core.Simulation;

namespace Dlt645.Core.Tests;

public class MeterClientTests
{
    private static readonly DataItemCatalog Catalog = DataItemCatalog.LoadBuiltIn();
    private static readonly MeterAddress Addr = MeterAddress.Parse("123456789012");

    private const string EnergyResponse = "FE FE 68 12 90 78 56 34 12 68 91 08 33 33 34 33 89 67 45 33 54 16";

    private static CommOptions FastOptions(int retries = 2) => new() { TimeoutMs = 300, Retries = retries, ErrorSettleMs = 100 };

    [Fact]
    public async Task ReadData_FiltersEcho_AndReturnsPayload()
    {
        var t = new ScriptedTransport(echo: true, Hex.Parse(EnergyResponse));
        var client = new MeterClient(t, FastOptions());
        var log = new List<MonitorEntry>();
        client.FrameLogged += log.Add;

        var r = await client.ReadDataAsync(Addr, 0x00010000);

        Assert.Equal("56 34 12 00", Hex.ToHex(r.Data));
        Assert.Equal(Addr, r.ResponseAddress);
        Assert.Empty(r.Warnings);
        Assert.Contains(log, e => e.Kind == MonitorKind.Echo);
        Assert.Contains(log, e => e.Kind == MonitorKind.Rx);
        Assert.Equal("FE FE FE FE 68 12 90 78 56 34 12 68 11 04 33 33 34 33 68 16", Hex.ToHex(t.Written[0]));
    }

    [Fact]
    public async Task ReadAddress_ParsesAddress()
    {
        var t = new ScriptedTransport(echo: true, Hex.Parse("FE 68 12 90 78 56 34 12 68 93 06 45 C3 AB 89 67 45 07 16"));
        var client = new MeterClient(t, FastOptions());
        var a = await client.ReadAddressAsync();
        Assert.Equal("123456789012", a.Text);
    }

    [Fact]
    public async Task ExceptionResponse_ThrowsWithErrorWord_NoRetry()
    {
        var t = new ScriptedTransport(echo: false, Hex.Parse("68 12 90 78 56 34 12 68 D1 01 35 8D 16"));
        var client = new MeterClient(t, FastOptions());
        var ex = await Assert.ThrowsAsync<MeterException>(() => client.ReadDataAsync(Addr, 0x00010000));
        Assert.Equal(ErrorCode.MeterErrorResponse, ex.Code);
        Assert.Equal((byte)0x02, ex.MeterErrorWord);
        Assert.Contains("无请求数据", ex.Message);
        Assert.Single(t.Written); // 异常应答不重试
    }

    [Fact]
    public async Task Timeout_RetriesConfiguredTimes()
    {
        var t = new ScriptedTransport(echo: true);
        var client = new MeterClient(t, FastOptions(retries: 2));
        var ex = await Assert.ThrowsAsync<MeterException>(() => client.ReadDataAsync(Addr, 0x00010000));
        Assert.Equal(ErrorCode.Timeout, ex.Code);
        Assert.Contains("回显", ex.Message); // 只收到回显
        Assert.Equal(3, t.Written.Count);
    }

    [Fact]
    public async Task ChecksumError_ThenSuccessOnRetry()
    {
        var bad = Hex.Parse("68 12 90 78 56 34 12 68 91 08 33 33 34 33 89 67 45 33 55 16");
        var t = new ScriptedTransport(echo: false, bad, Hex.Parse(EnergyResponse));
        var client = new MeterClient(t, FastOptions());
        var r = await client.ReadDataAsync(Addr, 0x00010000);
        Assert.Equal(2, t.Written.Count);
        Assert.Equal(4, r.Data.Length);
    }

    [Fact]
    public async Task ChecksumError_Persistent_ReportsChecksumCode()
    {
        var bad = Hex.Parse("68 12 90 78 56 34 12 68 91 08 33 33 34 33 89 67 45 33 55 16");
        var t = new ScriptedTransport(echo: false, bad, bad);
        var client = new MeterClient(t, FastOptions(retries: 1));
        var ex = await Assert.ThrowsAsync<MeterException>(() => client.ReadDataAsync(Addr, 0x00010000));
        Assert.Equal(ErrorCode.ChecksumError, ex.Code);
    }

    [Fact]
    public async Task IncompleteFrame_Reported()
    {
        var partial = Hex.Parse("68 12 90 78 56 34 12 68 91 08 33 33");
        var t = new ScriptedTransport(echo: false, partial);
        var client = new MeterClient(t, FastOptions(retries: 0));
        var ex = await Assert.ThrowsAsync<MeterException>(() => client.ReadDataAsync(Addr, 0x00010000));
        Assert.Equal(ErrorCode.IncompleteFrame, ex.Code);
    }

    [Fact]
    public async Task AddressMismatch_IsWarning()
    {
        var t = new ScriptedTransport(echo: false, Hex.Parse(EnergyResponse));
        var client = new MeterClient(t, FastOptions());
        var r = await client.ReadDataAsync(MeterAddress.Parse("000000000001"), 0x00010000);
        Assert.Single(r.Warnings);
        Assert.Equal(ErrorCode.AddressMismatch, r.Warnings[0].Code);
    }

    [Fact]
    public async Task Cancellation_StopsQuickly()
    {
        var t = new ScriptedTransport(echo: false);
        var client = new MeterClient(t, new CommOptions { TimeoutMs = 5000, Retries = 5 });
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadDataAsync(Addr, 0x00010000, cts.Token));
    }

    // ---------------------------------------------------------------- 与模拟电表联调

    private static (MeterClient Client, SimulatedTransport Transport) Simulated()
    {
        var meter = new SimulatedMeter(Catalog, MeterAddress.Parse("000012345678"));
        var t = new SimulatedTransport(meter) { ResponseDelayMs = 5 };
        t.Open();
        return (new MeterClient(t, FastOptions()) { DiNamer = Catalog.DescribeDi }, t);
    }

    [Fact]
    public async Task Simulator_ReadAddressThenEnergy()
    {
        var (client, _) = Simulated();
        var addr = await client.ReadAddressAsync();
        Assert.Equal("000012345678", addr.Text);

        var r = await client.ReadDataAsync(addr, 0x00010000);
        var item = Catalog.Find(0x00010000)!.Item;
        var fields = DataItemDecoder.Decode(item, r.Data);
        Assert.True(fields[0].Valid);
        Assert.True(fields[0].Number > 0);
    }

    [Fact]
    public async Task Simulator_FollowFrames_AreConcatenated()
    {
        var (client, _) = Simulated();
        // 开表盖记录 60 字节 > 模拟器单帧 48 字节，会产生后续帧
        var r = await client.ReadDataAsync(MeterAddress.Wildcard, 0x03300D01);
        Assert.Equal(2, r.FrameCount);
        Assert.Equal(60, r.Data.Length);
        var fields = DataItemDecoder.Decode(Catalog.Find(0x03300D01)!.Item, r.Data);
        Assert.All(fields, f => Assert.True(f.Valid, f.Name + " " + f.Text));
    }

    [Fact]
    public async Task Simulator_UnknownDi_ReturnsNoRequestedData()
    {
        var (client, _) = Simulated();
        var ex = await Assert.ThrowsAsync<MeterException>(() => client.ReadDataAsync(MeterAddress.Wildcard, 0x12345678));
        Assert.Equal(ErrorCode.MeterErrorResponse, ex.Code);
        Assert.Equal((byte)0x02, ex.MeterErrorWord);
    }

    [Fact]
    public async Task Simulator_WrongAddress_Timeout()
    {
        var (client, _) = Simulated();
        client.Options.Retries = 0;
        var ex = await Assert.ThrowsAsync<MeterException>(() => client.ReadDataAsync(MeterAddress.Parse("999999999999"), 0x00010000));
        Assert.Equal(ErrorCode.Timeout, ex.Code);
    }

    [Fact]
    public async Task Simulator_CorruptedResponse_RecoveredByRetry()
    {
        var (client, t) = Simulated();
        t.CorruptNextResponses = 1;
        var r = await client.ReadDataAsync(MeterAddress.Wildcard, 0x02010100);
        Assert.Equal(2, r.Data.Length);
    }

    [Fact]
    public async Task Simulator_AllCatalogItems_DecodeValid()
    {
        var (client, _) = Simulated();
        var reqs = ReadRequest.Expand(Catalog.Items, new[] { 0, 4 }, new[] { 0, 12 }, 1).ToList();
        foreach (var req in reqs)
        {
            var r = await client.ReadDataAsync(MeterAddress.Wildcard, req.Di);
            var fields = DataItemDecoder.Decode(req.Item, r.Data);
            foreach (var f in fields)
                Assert.True(f.Valid, $"{req.Describe()} {f.Name}: {f.Text}");
        }
    }

    [Fact]
    public void ResultRows_FromSuccessAndError()
    {
        var item = Catalog.Find(0x01010000)!.Item;
        var req = new ReadRequest(item, 0, 1);
        var res = new DataResult(req.Di, Hex.Parse("45 23 01 45 13 20 05 24"), Addr, TimeSpan.FromMilliseconds(120), 1, Array.Empty<MeterException>());
        var rows = ReadResultRow.FromSuccess(req, res, DataItemDecoder.Decode(item, res.Data)).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("上1结算日", rows[0].Period);
        Assert.Equal("总", rows[0].Tariff);
        Assert.Equal("01010001", rows[0].Di);

        var err = ReadResultRow.FromError(req, new MeterException(ErrorCode.Timeout));
        Assert.True(err.IsError);
        Assert.StartsWith("E201", err.Status);
    }
}

public class SimulatorFaultTests
{
    private static readonly DataItemCatalog Catalog = DataItemCatalog.LoadBuiltIn();

    private static (MeterClient Client, SimulatedTransport Transport) Create()
    {
        var t = new SimulatedTransport(new SimulatedMeter(Catalog)) { ResponseDelayMs = 5 };
        t.Open();
        return (new MeterClient(t, new CommOptions { TimeoutMs = 300, Retries = 0, ErrorSettleMs = 100 }), t);
    }

    [Fact]
    public async Task ForcedErrorWord_GivesMeterErrorResponse()
    {
        var (client, t) = Create();
        t.Meter.ForcedErrorWord = 0x04;
        var ex = await Assert.ThrowsAsync<MeterException>(() => client.ReadDataAsync(MeterAddress.Wildcard, 0x00010000));
        Assert.Equal(ErrorCode.MeterErrorResponse, ex.Code);
        Assert.Contains("密码错或未授权", ex.Message);
    }

    [Fact]
    public async Task Truncated_GivesIncompleteFrame()
    {
        var (client, t) = Create();
        t.TruncateNextResponses = 1;
        var ex = await Assert.ThrowsAsync<MeterException>(() => client.ReadDataAsync(MeterAddress.Wildcard, 0x00010000));
        Assert.Equal(ErrorCode.IncompleteFrame, ex.Code);
    }

    [Fact]
    public async Task NoResponse_GivesTimeoutWithEchoHint()
    {
        var (client, t) = Create();
        t.Respond = false;
        var ex = await Assert.ThrowsAsync<MeterException>(() => client.ReadDataAsync(MeterAddress.Wildcard, 0x00010000));
        Assert.Equal(ErrorCode.Timeout, ex.Code);
        Assert.Contains("回显", ex.Message);
    }

    [Fact]
    public async Task CustomData_IsReturnedAndDecoded()
    {
        var (client, t) = Create();
        t.Meter.SetData(0x00010000, Hex.Parse("34 12 00 00"));
        var r = await client.ReadDataAsync(MeterAddress.Wildcard, 0x00010000);
        var f = DataItemDecoder.Decode(Catalog.Find(0x00010000)!.Item, r.Data);
        Assert.Equal("12.34", f[0].Text);
        t.Meter.ClearData(0x00010000);
        r = await client.ReadDataAsync(MeterAddress.Wildcard, 0x00010000);
        Assert.NotEqual("12.34", DataItemDecoder.Decode(Catalog.Find(0x00010000)!.Item, r.Data)[0].Text);
    }
}
