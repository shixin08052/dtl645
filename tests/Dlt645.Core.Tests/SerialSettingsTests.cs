using System.IO.Ports;
using Dlt645.Core.Transport;

namespace Dlt645.Core.Tests;

public class SerialSettingsTests
{
    [Fact]
    public void Defaults_AreMeterInfraredStandard_1200_8E1()
    {
        var s = new SerialSettings();
        Assert.Equal(1200, s.BaudRate);
        Assert.Equal(8, s.DataBits);
        Assert.Equal(Parity.Even, s.Parity);
        Assert.Equal(StopBits.One, s.StopBits);
        Assert.Contains(1200, SerialSettings.BaudRates);
    }
}
