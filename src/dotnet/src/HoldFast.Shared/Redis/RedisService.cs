using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Expiration = StackExchange.Redis.Expiration;

namespace HoldFast.Shared.Redis;

/// <summary>
/// Configuration for Redis connections. Uses StackExchange.Redis connection string format.
/// </summary>
public class RedisOptions
{
    public string Configuration { get; set; } = "localhost:6379";
}

/// <summary>
/// Redis service for caching and distributed state.
/// Replaces go-redis usage in the Go backend.
/// </summary>
public class RedisService : IDisposable
{
    private readonly ConnectionMultiplexer _connection;
    private readonly IDatabase _db;

    public RedisService(IOptions<RedisOptions> options)
    {
        _connection = ConnectionMultiplexer.Connect(options.Value.Configuration);
        _db = _connection.GetDatabase();
    }

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        var value = await _db.StringGetAsync(key);
        if (value.IsNullOrEmpty) return null;
        return JsonSerializer.Deserialize<T>((string)value!);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class
    {
        var json = JsonSerializer.Serialize(value);
        if (expiry.HasValue)
            await _db.StringSetAsync(key, json, new Expiration(expiry.Value));
        else
            await _db.StringSetAsync(key, json);
    }

    public async Task<bool> DeleteAsync(string key)
    {
        return await _db.KeyDeleteAsync(key);
    }

    public async Task<long> IncrementAsync(string key)
    {
        return await _db.StringIncrementAsync(key);
    }

    public async Task<bool> SetAddAsync(string key, string value)
    {
        return await _db.SetAddAsync(key, value);
    }

    public async Task<bool> SetContainsAsync(string key, string value)
    {
        return await _db.SetContainsAsync(key, value);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
