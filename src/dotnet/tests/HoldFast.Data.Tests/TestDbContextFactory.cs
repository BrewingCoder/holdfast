using HoldFast.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HoldFast.Data.Tests;

/// <summary>
/// Creates an in-memory SQLite-backed HoldFastDbContext for integration tests.
/// Each test gets an isolated database.
/// </summary>
public sealed class TestDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public HoldFastDbContext Create()
    {
        var options = new DbContextOptionsBuilder<HoldFastDbContext>()
            .UseSqlite(_connection)
            .Options;

        var db = new HoldFastDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}
