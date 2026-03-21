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
        Assert.Contains("Host=localhost", cs);
        Assert.Contains("Port=8123", cs);
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

    // ── Additional edge cases ─────────────────────────────────────────

    [Fact]
    public void GetConnectionString_ReadOnlyFalse_UsesWriteCredentials()
    {
        var opts = new ClickHouseOptions
        {
            Username = "writer",
            Password = "write-pass",
            ReadonlyUsername = "reader",
            ReadonlyPassword = "read-pass",
        };

        var cs = opts.GetConnectionString(readOnly: false);
        Assert.Contains("Username=writer", cs);
        Assert.Contains("Password=write-pass", cs);
        Assert.DoesNotContain("reader", cs);
    }

    [Fact]
    public void GetConnectionString_ReadOnlyTrue_UsesReadCredentials()
    {
        var opts = new ClickHouseOptions
        {
            Username = "writer",
            Password = "write-pass",
            ReadonlyUsername = "reader",
            ReadonlyPassword = "read-pass",
        };

        var cs = opts.GetConnectionString(readOnly: true);
        Assert.Contains("Username=reader", cs);
        Assert.Contains("Password=read-pass", cs);
        Assert.DoesNotContain("writer", cs);
    }

    [Fact]
    public void GetConnectionString_SameReadWriteCredentials()
    {
        var opts = new ClickHouseOptions
        {
            Username = "admin",
            Password = "pass",
            ReadonlyUsername = "admin",
            ReadonlyPassword = "pass",
        };

        var rw = opts.GetConnectionString(readOnly: false);
        var ro = opts.GetConnectionString(readOnly: true);
        Assert.Equal(rw, ro);
    }

    [Fact]
    public void GetConnectionString_ContainsAllFourParts()
    {
        var opts = new ClickHouseOptions
        {
            Address = "myhost:8123",
            Database = "mydb",
            Username = "myuser",
            Password = "mypass",
        };
        var cs = opts.GetConnectionString();
        Assert.Contains("Host=myhost", cs);
        Assert.Contains("Port=8123", cs);
        Assert.Contains("Protocol=http", cs);
        Assert.Contains("Database=mydb", cs);
        Assert.Contains("Username=myuser", cs);
        Assert.Contains("Password=mypass", cs);
    }

    [Theory]
    [InlineData("host:80", "http")]
    [InlineData("host:443", "http")]
    [InlineData("host:8123", "http")]
    [InlineData("host:9000", "http")]
    [InlineData("host:9440", "https")]
    public void GetConnectionString_ProtocolSelection_ByPort(string address, string expectedProtocol)
    {
        var opts = new ClickHouseOptions { Address = address };
        var cs = opts.GetConnectionString();
        Assert.Contains($"Protocol={expectedProtocol}", cs);
    }

    [Fact]
    public void GetConnectionString_PasswordWithSpecialChars()
    {
        var opts = new ClickHouseOptions { Password = "p@ss=w;ord" };
        var cs = opts.GetConnectionString();
        Assert.Contains("Password=p@ss=w;ord", cs);
    }

    [Fact]
    public void GetConnectionString_IPv6Address()
    {
        var opts = new ClickHouseOptions { Address = "[::1]:8123" };
        var cs = opts.GetConnectionString();
        Assert.Contains("Host=[::1]", cs);
        Assert.Contains("Port=8123", cs);
        Assert.Contains("Protocol=http", cs);
    }

    [Fact]
    public void GetConnectionString_IPv4Address()
    {
        var opts = new ClickHouseOptions { Address = "10.0.0.1:8123" };
        var cs = opts.GetConnectionString();
        Assert.Contains("Host=10.0.0.1", cs);
        Assert.Contains("Port=8123", cs);
    }

    [Fact]
    public void MaxOpenConnections_DefaultIs100()
    {
        var opts = new ClickHouseOptions();
        Assert.Equal(100, opts.MaxOpenConnections);
    }

    [Fact]
    public void MaxOpenConnections_CanBeChanged()
    {
        var opts = new ClickHouseOptions { MaxOpenConnections = 50 };
        Assert.Equal(50, opts.MaxOpenConnections);
    }

    [Fact]
    public void AllProperties_CanBeSet()
    {
        var opts = new ClickHouseOptions
        {
            Address = "custom:9440",
            Database = "prod_db",
            Username = "writer",
            Password = "w-pass",
            ReadonlyUsername = "reader",
            ReadonlyPassword = "r-pass",
            MaxOpenConnections = 200,
        };

        Assert.Equal("custom:9440", opts.Address);
        Assert.Equal("prod_db", opts.Database);
        Assert.Equal("writer", opts.Username);
        Assert.Equal("w-pass", opts.Password);
        Assert.Equal("reader", opts.ReadonlyUsername);
        Assert.Equal("r-pass", opts.ReadonlyPassword);
        Assert.Equal(200, opts.MaxOpenConnections);
    }

    [Fact]
    public void GetConnectionString_EmptyDatabase()
    {
        var opts = new ClickHouseOptions { Database = "" };
        var cs = opts.GetConnectionString();
        Assert.Contains("Database=", cs);
    }

    [Fact]
    public void GetConnectionString_EmptyUsername()
    {
        var opts = new ClickHouseOptions { Username = "" };
        var cs = opts.GetConnectionString();
        Assert.Contains("Username=", cs);
    }

    [Fact]
    public void GetConnectionString_Port9440InMiddle_IsHttp()
    {
        // Only :9440 at the END triggers HTTPS
        var opts = new ClickHouseOptions { Address = "host-9440.example.com:8123" };
        var cs = opts.GetConnectionString();
        Assert.Contains("Protocol=http", cs);
    }
}
