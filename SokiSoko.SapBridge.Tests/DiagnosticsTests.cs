using System.Net;
using System.Net.Sockets;
using SokiSoko.SapBridge.Ui;
using Xunit;

namespace SokiSoko.SapBridge.Tests;

public class DiagnosticsTests
{
    [Fact]
    public void B1_401_PointsAtCredentialsAndLicense()
    {
        var msg = Diagnostics.ExplainB1(new InvalidOperationException("B1 GET Login returned 401: unauthorized"));
        Assert.Contains("company database", msg);
        Assert.Contains("license", msg);
    }

    [Fact]
    public void B1_CertError_SuggestsUntrustedCertToggle()
    {
        var msg = Diagnostics.ExplainB1(new HttpRequestException("The remote certificate is invalid"));
        Assert.Contains("Untrusted cert", msg);
    }

    [Fact]
    public void B1_Unreachable_MentionsPortAndFirewall()
    {
        var msg = Diagnostics.ExplainB1(new HttpRequestException("Connection refused", new SocketException()));
        Assert.Contains("50000", msg);
        Assert.Contains("firewall", msg);
    }

    [Fact]
    public void Sokisoko_403_PointsAtScope()
    {
        var msg = Diagnostics.ExplainSokisoko(new InvalidOperationException("ingest returned 403: forbidden"));
        Assert.Contains("erp.manage", msg);
    }

    [Fact]
    public void Sokisoko_404_PointsAtConnectionId()
    {
        var msg = Diagnostics.ExplainSokisoko(new InvalidOperationException("ingest returned 404: not found"));
        Assert.Contains("connection ID", msg);
    }

    [Fact]
    public void Sokisoko_UnknownError_PassesThrough()
    {
        var msg = Diagnostics.ExplainSokisoko(new InvalidOperationException("something odd"));
        Assert.Equal("something odd", msg);
    }
}
