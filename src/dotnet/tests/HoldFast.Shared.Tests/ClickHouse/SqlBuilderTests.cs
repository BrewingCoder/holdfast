using HoldFast.Data.ClickHouse;

namespace HoldFast.Shared.Tests.ClickHouse;

public class SqlBuilderTests
{
    [Fact]
    public void Build_EmptyBuilder_ReturnsEmptySql()
    {
        var sb = new SqlBuilder();
        var (sql, parameters) = sb.Build();
        Assert.Equal("", sql);
        Assert.Empty(parameters);
    }

    [Fact]
    public void Build_SingleAppend()
    {
        var sb = new SqlBuilder();
        sb.Append("SELECT 1");
        var (sql, _) = sb.Build();
        Assert.Equal("SELECT 1", sql);
    }

    [Fact]
    public void Build_MultipleAppends_Concatenate()
    {
        var sb = new SqlBuilder();
        sb.Append("SELECT * ");
        sb.Append("FROM logs ");
        sb.Append("WHERE ProjectId = 1");
        var (sql, _) = sb.Build();
        Assert.Equal("SELECT * FROM logs WHERE ProjectId = 1", sql);
    }

    [Fact]
    public void AddParam_StoresParameters()
    {
        var sb = new SqlBuilder();
        sb.AddParam("projectId", 42);
        sb.AddParam("name", "test");
        var (_, parameters) = sb.Build();
        Assert.Equal(2, parameters.Count);
        Assert.Equal(42, parameters["projectId"]);
        Assert.Equal("test", parameters["name"]);
    }

    [Fact]
    public void AddParam_DuplicateKey_KeepsFirst()
    {
        var sb = new SqlBuilder();
        sb.AddParam("key", "first");
        sb.AddParam("key", "second");
        var (_, parameters) = sb.Build();
        Assert.Equal("first", parameters["key"]);
    }

    [Fact]
    public void Build_WithParamsAndSql()
    {
        var sb = new SqlBuilder();
        sb.Append("SELECT * FROM logs WHERE ProjectId = {projectId:Int32} AND ServiceName = {name:String}");
        sb.AddParam("projectId", 10);
        sb.AddParam("name", "api");
        var (sql, parameters) = sb.Build();
        Assert.Contains("{projectId:Int32}", sql);
        Assert.Equal(10, parameters["projectId"]);
        Assert.Equal("api", parameters["name"]);
    }

    [Fact]
    public void AddParam_DateTimeValue()
    {
        var sb = new SqlBuilder();
        var now = DateTime.UtcNow;
        sb.AddParam("ts", now);
        var (_, parameters) = sb.Build();
        Assert.Equal(now, parameters["ts"]);
    }

    [Fact]
    public void AddParam_NullValue()
    {
        var sb = new SqlBuilder();
        sb.AddParam("nullable", null!);
        var (_, parameters) = sb.Build();
        Assert.Null(parameters["nullable"]);
    }
}
