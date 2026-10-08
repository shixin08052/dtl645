using Dlt645.Core.Transport;

namespace Dlt645.Core.Tests;

public class DriverInspectorTests
{
    private static UsbSerialDevice Device(uint problem, string? port = "COM3", string? version = "3.8.2023.2") =>
        new(@"USB\VID_1A86&PID_7523\5&1A2B", "USB-SERIAL CH340 (COM3)", @"USB\VID_1A86&PID_7523&REV_0264", port, problem,
            problem == 0 ? "CH341SER_A64" : null, problem == 0 ? version : null, null, null);

    [Fact]
    public void DeviceWithoutDriver_IsDeviceProblem()
    {
        var r = DriverInspector.BuildReport(new[] { Device(28, port: null) }, null, null, Array.Empty<string>());
        Assert.Equal(DriverHealth.DeviceProblem, r.Health);
        Assert.Contains("驱动程序未安装", r.Summary);
        Assert.Contains("CH340", r.Summary);
        Assert.NotNull(r.ProblemDevice);
        Assert.False(r.DriverInstalled);
    }

    [Fact]
    public void WorkingDevice_IsOkWithPortAndVersion()
    {
        var r = DriverInspector.BuildReport(new[] { Device(0) }, @"C:\Windows\System32\drivers\CH341S64.SYS", "3.8", new[] { "ch341ser.inf_amd64_x" });
        Assert.Equal(DriverHealth.Ok, r.Health);
        Assert.Contains("COM3", r.Summary);
        Assert.Contains("3.8.2023.2", r.Summary);
        Assert.True(r.DriverInstalled);
    }

    [Fact]
    public void NoDevice_DistinguishesDriverInstalledOrNot()
    {
        var with = DriverInspector.BuildReport(Array.Empty<UsbSerialDevice>(), null, null, new[] { "ch341ser.inf_amd64_x" });
        Assert.Equal(DriverHealth.NoDevice, with.Health);
        Assert.Contains("驱动已安装", with.Summary);

        var without = DriverInspector.BuildReport(Array.Empty<UsbSerialDevice>(), null, null, Array.Empty<string>());
        Assert.Equal(DriverHealth.NoDevice, without.Health);
        Assert.Contains("未发现 CH340 驱动", without.Summary);
    }

    [Theory]
    [InlineData(@"USB\VID_1A86&PID_7523&REV_0264", "CH340")]
    [InlineData(@"USB\VID_1A86&PID_5523", "CH341")]
    [InlineData(@"USB\VID_1A86&PID_7522", "CH340K")]
    public void ChipName_ByPid(string hwId, string chip) => Assert.Equal(chip, DriverInspector.ChipName(hwId));

    [Theory]
    [InlineData(28u, "未安装")]
    [InlineData(22u, "禁用")]
    [InlineData(10u, "无法启动")]
    [InlineData(999u, "999")]
    public void ProblemCodes_AreChinese(uint code, string expected) => Assert.Contains(expected, DriverInspector.DescribeProblem(code));

    [Fact]
    public void Inspect_DoesNotThrow()
    {
        var r = DriverInspector.Inspect();
        Assert.False(string.IsNullOrEmpty(r.Summary));
    }
}
