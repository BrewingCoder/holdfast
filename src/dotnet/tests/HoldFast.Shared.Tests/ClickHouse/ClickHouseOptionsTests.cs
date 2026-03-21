using HoldFast.Data.ClickHouse;

namespace HoldFast.Shared.Tests.ClickHouse;

public class ClickHouseOptionsTests
{
    [Fact]
    public void Defaults_AreReasonable()
    {
        var opts = new ClickHouseOptions();
        Assert.Equal("localhost:8123", opts.Address);
        Assert.Equal("default", opts.Database);
        Assert.Equal("default", opts.Username);
        Assert.Equal(string.Empty, opts.Password);
        Assert.Equal("default", opts.ReadonlyUsername);
        Assert.Equal(string.Empty, opts.ReadonlyPassword);
        Assert.Equal(100, opts.MaxOpenConnections);
    }

    [Fact]
    public void GetConnectionString_Http_Default()
    {
        var opts = new ClickHouseOptions();
        var cs = opts.GetConnectionString();
        Assert.Contains("Protocol=http", cs);
        Assert.Contains("Host=localhost:8123", cs);
        Assert.Contains("Database=default", cs);
        Assert.Contains("Username=default", cs);
    }

    [Fact]
    public void GetConnectionString_Https_Port9440()
    {
        var opts = new ClickHouseOptions { Address = "ch.example.com:9440" };
        var cs = opts.GetConnectionString();
        Assert.Contains("Protocol=https", cs);
    }

    [Fact]
    public void GetConnectionString_ReadOnly_UsesDifferentCredentials()
    {
        var opts = new ClickHouseOptions
        {
            Username = "admin",
            Password = "admin-pass",
            ReadonlyUsername = "reader",
            ReadonlyPassword = "reader-pass",
        };

        var rwCs = opts.GetConnectionString(readOnly: false);
        var roCs = opts.GetConnectionString(readOnly: true);

        Assert.Contains("Username=admin", rwCs);
        Assert.Contains("Password=admin-pass", rwCs);
        Assert.Contains("Username=reader", roCs);
        Assert.Contains("Password=reader-pass", roCs);
    }

    [Fact]
    public void GetConnectionString_CustomDatabase()
    {
        var opts = new ClickHouseOptions { Database = "holdfast_prod" };
        var cs = opts.GetConnectionString();
        Assert.Contains("Database=holdfast_prod", cs);
    }

    [Fact]
    public void GetConnectionString_CustomPort_NotTls()
    {
        var opts = new ClickHouseOptions { Address = "192.168.1.100:9000" };
        var cs = opts.GetConnectionString();
        Assert.Contains("Protocol=http", cs);
    }

    [Fact]
    public void GetConnectionString_EmptyPassword_StillIncluded()
    {
        var opts = new ClickHouseOptions();
        var cs = opts.GetConnectionString();
        Assert.Contains("Password=", cs);
    }
}
