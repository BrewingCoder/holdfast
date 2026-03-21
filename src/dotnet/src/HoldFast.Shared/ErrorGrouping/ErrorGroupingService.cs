using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HoldFast.Data;
using HoldFast.Domain.Entities;
using HoldFast.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HoldFast.Shared.ErrorGrouping;

/// <summary>
/// Classic fingerprint-based error grouping, ported from Go's errorgroups/fingerprint.go
/// and resolver.go HandleErrorAndGroup / GetTopErrorGroupMatch.
/// </summary>
public class ErrorGroupingService : IErrorGroupingService
{
    private const int MaxEventLength = 10_000;
    private const int MaxStackFrames = 50;

    private readonly HoldFastDbContext _db;
    private readonly ILogger<ErrorGroupingService> _logger;

    public ErrorGroupingService(HoldFastDbContext db, ILogger<ErrorGroupingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public List<ErrorFingerprintEntry> GetFingerprints(string? stackTraceJson)
    {
        var fingerprints = new List<ErrorFingerprintEntry>();
        if (string.IsNullOrWhiteSpace(stackTraceJson)) return fingerprints;

        List<StackFrame>? frames;
        try
        {
            frames = JsonSerializer.Deserialize<List<StackFrame>>(stackTraceJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return fingerprints;
        }

        if (frames == null) return fingerprints;

        var limit = Math.Min(frames.Count, MaxStackFrames);
        for (var i = 0; i < limit; i++)
        {
            var frame = frames[i];
            if (frame == null) continue;

            // CODE fingerprint: line content (before + current + after)
            var codeParts = new List<string>();
            if (!string.IsNullOrEmpty(frame.LinesBefore)) codeParts.Add(frame.LinesBefore);
            if (!string.IsNullOrEmpty(frame.LineContent)) codeParts.Add(frame.LineContent);
            if (!string.IsNullOrEmpty(frame.LinesAfter)) codeParts.Add(frame.LinesAfter);

            if (codeParts.Count > 0)
            {
                fingerprints.Add(new ErrorFingerprintEntry("CODE", string.Join(";", codeParts), i));
            }

            // META fingerprint: file + function + line + column
            var metaParts = new List<string>();
            if (!string.IsNullOrEmpty(frame.FileName)) metaParts.Add(frame.FileName);
            if (!string.IsNullOrEmpty(frame.FunctionName)) metaParts.Add(frame.FunctionName);
            if (frame.LineNumber.HasValue) metaParts.Add(frame.LineNumber.Value.ToString());
            if (frame.ColumnNumber.HasValue) metaParts.Add(frame.ColumnNumber.Value.ToString());

            if (metaParts.Count > 0)
            {
                fingerprints.Add(new ErrorFingerprintEntry("META", string.Join(";", metaParts), i));
            }
        }

        return fingerprints;
    }

    /// <inheritdoc />
    public async Task<ErrorGroup?> FindMatchingGroupAsync(
        int projectId,
        string errorEvent,
        List<ErrorFingerprintEntry> fingerprints,
        CancellationToken ct)
    {
        // Separate fingerprints into scoring categories (matching Go logic)
        var firstCode = fingerprints.FirstOrDefault(f => f.Type == "CODE" && f.Index == 0);
        var firstMeta = fingerprints.FirstOrDefault(f => f.Type == "META" && f.Index == 0);
        var restCode = fingerprints.Where(f => f.Type == "CODE" && f.Index is > 0 and <= 4).ToList();
        var restMeta = fingerprints.Where(f => f.Type == "META" && f.Index is > 0 and <= 4).ToList();

        // Minimum score threshold: first frame match + all rest frames
        var minScore = 10 + Math.Max(restCode.Count, restMeta.Count) - 1;
        if (minScore < 10) minScore = 10; // At least need first-frame match

        // Build scored candidates using EF Core instead of raw SQL
        // Step 1: Get all candidate group IDs with event match (+100)
        var eventMatchIds = await _db.ErrorGroups
            .Where(g => g.ProjectId == projectId && g.Event == errorEvent)
            .Select(g => g.Id)
            .ToListAsync(ct);

        if (eventMatchIds.Count == 0 && fingerprints.Count == 0)
            return null;

        // Step 2: Score fingerprint matches
        var scores = new Dictionary<int, int>();

        // Event match: +100 per group
        foreach (var id in eventMatchIds)
            scores[id] = 100;

        // First frame match: +10
        if (firstCode != null || firstMeta != null)
        {
            var firstValues = new List<string>();
            if (firstCode != null) firstValues.Add(firstCode.Value);
            if (firstMeta != null) firstValues.Add(firstMeta.Value);

            var firstTypes = new List<string>();
            if (firstCode != null) firstTypes.Add("CODE");
            if (firstMeta != null) firstTypes.Add("META");

            var firstMatches = await _db.ErrorFingerprints
                .Where(f => f.ProjectId == projectId
                    && f.Index == 0
                    && firstTypes.Contains(f.Type)
                    && firstValues.Contains(f.Value)
                    && f.ErrorGroupId != 0)
                .Select(f => f.ErrorGroupId)
                .Distinct()
                .ToListAsync(ct);

            foreach (var groupId in firstMatches)
            {
                scores.TryAdd(groupId, 0);
                scores[groupId] += 10;
            }
        }

        // Rest frames match: +1 each (index 1-4)
        var restValues = restCode.Select(f => f.Value)
            .Concat(restMeta.Select(f => f.Value))
            .Distinct()
            .ToList();

        if (restValues.Count > 0)
        {
            var restMatches = await _db.ErrorFingerprints
                .Where(f => f.ProjectId == projectId
                    && f.Index > 0 && f.Index <= 4
                    && restValues.Contains(f.Value)
                    && f.ErrorGroupId != 0)
                .Select(f => new { f.ErrorGroupId, f.Index })
                .Distinct()
                .ToListAsync(ct);

            foreach (var match in restMatches)
            {
                scores.TryAdd(match.ErrorGroupId, 0);
                scores[match.ErrorGroupId] += 1;
            }
        }

        // Find best match above threshold
        var best = scores
            .Where(s => s.Value > minScore)
            .OrderByDescending(s => s.Value)
            .ThenByDescending(s => s.Key) // Prefer newer groups on tie
            .Select(s => (int?)s.Key)
            .FirstOrDefault();

        if (best == null) return null;

        return await _db.ErrorGroups.FindAsync([best.Value], ct);
    }

    /// <inheritdoc />
    public async Task<ErrorGroupingResult> GroupErrorAsync(
        int projectId,
        string errorEvent,
        string errorType,
        string? stackTrace,
        DateTime timestamp,
        string? url,
        string? source,
        string? payload,
        string? environment,
        string? serviceName,
        string? serviceVersion,
        int? sessionId,
        string? traceExternalId,
        string? spanId,
        CancellationToken ct)
    {
        // Truncate event
        if (errorEvent.Length > MaxEventLength)
            errorEvent = errorEvent[..MaxEventLength];

        // Generate fingerprints
        var fingerprints = GetFingerprints(stackTrace);

        // Try to find matching group
        var existingGroup = await FindMatchingGroupAsync(projectId, errorEvent, fingerprints, ct);
        bool isNewGroup;
        ErrorGroup errorGroup;

        if (existingGroup != null)
        {
            errorGroup = existingGroup;
            isNewGroup = false;

            // Reopen resolved errors (Go behavior)
            if (errorGroup.State == ErrorGroupState.Resolved)
            {
                errorGroup.State = ErrorGroupState.Open;
                _db.ErrorGroups.Update(errorGroup);
            }
        }
        else
        {
            // Create new error group
            errorGroup = new ErrorGroup
            {
                ProjectId = projectId,
                Event = errorEvent,
                Type = errorType,
                StackTrace = stackTrace,
                State = ErrorGroupState.Open,
                ServiceName = serviceName,
                SecureId = GenerateSecureId(),
            };
            _db.ErrorGroups.Add(errorGroup);
            await _db.SaveChangesAsync(ct);
            isNewGroup = true;
        }

        // Create error object
        var errorObject = new ErrorObject
        {
            ProjectId = projectId,
            ErrorGroupId = errorGroup.Id,
            Event = errorEvent,
            Type = errorType,
            StackTrace = stackTrace,
            Timestamp = timestamp,
            Url = url,
            Source = source,
            Payload = payload,
            Environment = environment,
            ServiceName = serviceName,
            ServiceVersion = serviceVersion,
            SessionId = sessionId,
            TraceExternalId = traceExternalId,
            SpanId = spanId,
        };
        _db.ErrorObjects.Add(errorObject);

        // Store fingerprints associated with the group
        if (fingerprints.Count > 0)
        {
            // Remove old fingerprints for this group, replace with new
            var existingFps = await _db.ErrorFingerprints
                .Where(f => f.ErrorGroupId == errorGroup.Id)
                .ToListAsync(ct);
            _db.ErrorFingerprints.RemoveRange(existingFps);

            foreach (var fp in fingerprints)
            {
                _db.ErrorFingerprints.Add(new ErrorFingerprint
                {
                    ProjectId = projectId,
                    ErrorGroupId = errorGroup.Id,
                    Type = fp.Type,
                    Value = fp.Value,
                    Index = fp.Index,
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        return new ErrorGroupingResult(errorGroup, errorObject, isNewGroup);
    }

    private static string GenerateSecureId()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Stack frame structure matching the JSON format from SDKs.
    /// </summary>
    private class StackFrame
    {
        public string? FileName { get; set; }
        public string? FunctionName { get; set; }
        public int? LineNumber { get; set; }
        public int? ColumnNumber { get; set; }
        public string? LineContent { get; set; }
        public string? LinesBefore { get; set; }
        public string? LinesAfter { get; set; }
        public string? Source { get; set; }
    }
}
