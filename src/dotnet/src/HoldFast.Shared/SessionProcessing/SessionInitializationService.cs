using System.Net.Mail;
using System.Text.Json;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HoldFast.Shared.SessionProcessing;

/// <summary>
/// Full session initialization logic ported from Go's InitializeSessionImpl
/// and IdentifySessionImpl.
/// </summary>
public class SessionInitializationService : ISessionInitializationService
{
    private readonly HoldFastDbContext _db;
    private readonly ILogger<SessionInitializationService> _logger;

    public SessionInitializationService(HoldFastDbContext db, ILogger<SessionInitializationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SessionInitResult> InitializeSessionAsync(
        string sessionSecureId,
        string? sessionKey,
        int projectId,
        string fingerprint,
        string clientId,
        string clientVersion,
        string firstloadVersion,
        string? clientConfig,
        string environment,
        string? appVersion,
        string? serviceName,
        bool enableStrictPrivacy,
        bool enableRecordingNetworkContents,
        string? privacySetting,
        string? userAgent,
        string? acceptLanguage,
        string? ipAddress,
        CancellationToken ct)
    {
        // Step 1: Check for duplicate (existing session)
        var existing = await _db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == sessionSecureId, ct);

        if (existing != null)
        {
            _logger.LogDebug("Session {SecureId} already exists, returning existing", sessionSecureId);
            return new SessionInitResult(existing, false, true);
        }

        // Step 2: Parse device details from User-Agent
        var device = DeviceParser.Parse(userAgent);

        // Step 3: Create session
        var session = new Session
        {
            SecureId = sessionSecureId,
            ProjectId = projectId,
            Fingerprint = fingerprint,
            ClientID = clientId,
            ClientVersion = clientVersion,
            FirstloadVersion = firstloadVersion,
            ClientConfig = clientConfig,
            Environment = environment,
            AppVersion = appVersion,
            ServiceName = serviceName,
            EnableStrictPrivacy = enableStrictPrivacy,
            EnableRecordingNetworkContents = enableRecordingNetworkContents,
            PrivacySetting = privacySetting,
            Language = acceptLanguage,
            IP = ipAddress,
            OSName = device.OSName,
            OSVersion = device.OSVersion,
            BrowserName = device.BrowserName,
            BrowserVersion = device.BrowserVersion,
            WithinBillingQuota = true, // Self-hosted: always within quota
            Processed = false,
            Excluded = false,
        };

        _db.Sessions.Add(session);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            // Race condition — another worker already created this session
            _logger.LogDebug("Duplicate key for session {SecureId}, fetching existing", sessionSecureId);
            var dup = await _db.Sessions.FirstOrDefaultAsync(s => s.SecureId == sessionSecureId, ct);
            if (dup != null)
                return new SessionInitResult(dup, false, true);
            throw;
        }

        // Step 4: Append device properties as Fields
        await AppendDeviceFieldsAsync(session, device, ct);

        // Step 5: Register service if applicable
        if (!string.IsNullOrEmpty(serviceName))
        {
            await UpsertServiceAsync(projectId, serviceName, ct);
        }

        // Step 6: Create SetupEvent if first session
        await TryCreateSetupEventAsync(projectId, "session", ct);

        _logger.LogInformation(
            "Session {SecureId} initialized: project={ProjectId}, os={OS}, browser={Browser}",
            sessionSecureId, projectId, device.OSName, device.BrowserName);

        return new SessionInitResult(session, true, false);
    }

    /// <inheritdoc />
    public async Task<Session> IdentifySessionAsync(
        string sessionSecureId,
        string userIdentifier,
        object? userObject,
        CancellationToken ct)
    {
        var session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.SecureId == sessionSecureId, ct)
            ?? throw new InvalidOperationException($"Session {sessionSecureId} not found");

        // Parse user properties from userObject
        var userProps = new Dictionary<string, string>();
        if (userObject != null)
        {
            try
            {
                var json = userObject is JsonElement je
                    ? je.ToString()
                    : JsonSerializer.Serialize(userObject);
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json ?? "{}");
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        userProps[kvp.Key] = kvp.Value.ToString();
                    }
                }
            }
            catch (JsonException)
            {
                // Invalid JSON — skip user properties
            }
        }

        // Auto-detect email
        string? email = null;
        try
        {
            var addr = new MailAddress(userIdentifier);
            email = addr.Address;
            userProps["email"] = email;
            userProps["identified_email"] = "true";
        }
        catch (FormatException)
        {
            // Not an email — that's fine
        }

        // Auto-detect domain from email
        if (email != null && email.Contains('@'))
        {
            var domain = email[(email.IndexOf('@') + 1)..];
            userProps["domain"] = domain;
        }

        // Detect first-time user
        var hasOtherSessions = await _db.Sessions
            .AnyAsync(s => s.ProjectId == session.ProjectId
                && s.Identifier == userIdentifier
                && s.Id != session.Id, ct);
        session.FirstTime = hasOtherSessions ? 0 : 1;

        // Update session
        session.Identifier = userIdentifier;

        // Append user properties as Fields
        foreach (var prop in userProps)
        {
            var existingField = await _db.Fields
                .FirstOrDefaultAsync(f => f.ProjectId == session.ProjectId
                    && f.Type == "user"
                    && f.Name == prop.Key
                    && f.Value == prop.Value, ct);

            if (existingField == null)
            {
                _db.Fields.Add(new Field
                {
                    ProjectId = session.ProjectId,
                    Type = "user",
                    Name = prop.Key,
                    Value = prop.Value,
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        // Backfill: find unidentified sessions with same ClientID
        if (!string.IsNullOrEmpty(session.ClientID))
        {
            var unidentified = await _db.Sessions
                .Where(s => s.ProjectId == session.ProjectId
                    && s.ClientID == session.ClientID
                    && s.Identifier == null
                    && s.Id != session.Id)
                .ToListAsync(ct);

            foreach (var s in unidentified)
            {
                s.Identifier = userIdentifier;
                s.FirstTime = 0; // Not first time since we're backfilling
            }

            if (unidentified.Count > 0)
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Backfilled {Count} sessions for client {ClientId}",
                    unidentified.Count, session.ClientID);
            }
        }

        return session;
    }

    private async Task AppendDeviceFieldsAsync(Session session, DeviceInfo device, CancellationToken ct)
    {
        var fields = new List<(string Name, string? Value)>
        {
            ("os_name", device.OSName),
            ("os_version", device.OSVersion),
            ("browser_name", device.BrowserName),
            ("browser_version", device.BrowserVersion),
            ("environment", session.Environment),
            ("device_id", session.Fingerprint),
        };

        if (!string.IsNullOrEmpty(session.ServiceName))
            fields.Add(("service_name", session.ServiceName));

        foreach (var (name, value) in fields)
        {
            if (string.IsNullOrEmpty(value)) continue;

            var exists = await _db.Fields
                .AnyAsync(f => f.ProjectId == session.ProjectId
                    && f.Type == "session"
                    && f.Name == name
                    && f.Value == value, ct);

            if (!exists)
            {
                _db.Fields.Add(new Field
                {
                    ProjectId = session.ProjectId,
                    Type = "session",
                    Name = name,
                    Value = value,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task UpsertServiceAsync(int projectId, string serviceName, CancellationToken ct)
    {
        var exists = await _db.Services
            .AnyAsync(s => s.ProjectId == projectId && s.Name == serviceName, ct);

        if (!exists)
        {
            _db.Services.Add(new Service
            {
                ProjectId = projectId,
                Name = serviceName,
            });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Duplicate — another worker beat us to it
            }
        }
    }

    private async Task TryCreateSetupEventAsync(int projectId, string type, CancellationToken ct)
    {
        var exists = await _db.SetupEvents
            .AnyAsync(e => e.ProjectId == projectId && e.Type == type, ct);

        if (!exists)
        {
            _db.SetupEvents.Add(new SetupEvent
            {
                ProjectId = projectId,
                Type = type,
                CreatedAt = DateTime.UtcNow,
            });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Duplicate — another worker beat us to it
            }
        }
    }

    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        var inner = ex.InnerException?.Message ?? "";
        return inner.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || inner.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
            || inner.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Parsed device information from User-Agent string.
/// </summary>
public record DeviceInfo(
    string? BrowserName,
    string? BrowserVersion,
    string? OSName,
    string? OSVersion);

/// <summary>
/// Simple User-Agent parser. Extracts browser and OS info.
/// Matches Go's mssola/useragent behavior for common browsers.
/// </summary>
internal static class DeviceParser
{
    public static DeviceInfo Parse(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return new DeviceInfo(null, null, null, null);

        var ua = userAgent;
        string? browserName = null, browserVersion = null;
        string? osName = null, osVersion = null;

        // OS detection — order matters: more specific before general
        if (ua.Contains("iPhone OS") || ua.Contains("iPad"))
        {
            osName = "iOS";
            osVersion = ExtractBetween(ua, "iPhone OS ", " ")?.Replace('_', '.')
                ?? ExtractBetween(ua, "CPU OS ", " ")?.Replace('_', '.');
        }
        else if (ua.Contains("Android"))
        {
            osName = "Android";
            osVersion = ExtractBetween(ua, "Android ", ";");
        }
        else if (ua.Contains("Windows NT"))
        {
            osName = "Windows";
            osVersion = ExtractBetween(ua, "Windows NT ", ";") ?? ExtractBetween(ua, "Windows NT ", ")");
        }
        else if (ua.Contains("Mac OS X"))
        {
            osName = "Mac OS X";
            osVersion = ExtractBetween(ua, "Mac OS X ", ";")?.Replace('_', '.')
                ?? ExtractBetween(ua, "Mac OS X ", ")")?.Replace('_', '.');
        }
        else if (ua.Contains("Linux"))
        {
            osName = "Linux";
        }

        // Browser detection (order matters — more specific first)
        if (ua.Contains("Edg/"))
        {
            browserName = "Edge";
            browserVersion = ExtractAfter(ua, "Edg/");
        }
        else if (ua.Contains("OPR/"))
        {
            browserName = "Opera";
            browserVersion = ExtractAfter(ua, "OPR/");
        }
        else if (ua.Contains("Chrome/") && !ua.Contains("Chromium"))
        {
            browserName = "Chrome";
            browserVersion = ExtractAfter(ua, "Chrome/");
        }
        else if (ua.Contains("Firefox/"))
        {
            browserName = "Firefox";
            browserVersion = ExtractAfter(ua, "Firefox/");
        }
        else if (ua.Contains("Safari/") && ua.Contains("Version/"))
        {
            browserName = "Safari";
            browserVersion = ExtractAfter(ua, "Version/");
        }

        return new DeviceInfo(browserName, browserVersion, osName, osVersion);
    }

    private static string? ExtractBetween(string input, string start, string end)
    {
        var startIdx = input.IndexOf(start, StringComparison.Ordinal);
        if (startIdx < 0) return null;
        startIdx += start.Length;
        var endIdx = input.IndexOf(end, startIdx, StringComparison.Ordinal);
        if (endIdx < 0) return null;
        return input[startIdx..endIdx].Trim();
    }

    private static string? ExtractAfter(string input, string marker)
    {
        var idx = input.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        idx += marker.Length;
        var end = input.IndexOfAny([' ', ';', ')'], idx);
        return end < 0 ? input[idx..] : input[idx..end];
    }
}
