using System.Globalization;
using NorthLife.Api.Contracts;
using NorthLife.Api.Models;

namespace NorthLife.Api.Moderation;

public static class AutoModerationSettingsValidator
{
    public const int MaximumBannedWords = 200;
    public const int MaximumBannedWordLength = 50;

    /// <summary>Checks every field against its allowed range and returns the normalised settings.</summary>
    public static AutoModerationSettings Validate(AutoModerationSettingsRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        void Range<T>(string field, T value, T minimum, T maximum) where T : IComparable<T>
        {
            if (value.CompareTo(minimum) < 0 || value.CompareTo(maximum) > 0)
            {
                errors[field] = [$"Must be between {minimum} and {maximum}."];
            }
        }

        if (!Enum.IsDefined(request.Mode)) errors["mode"] = ["Unknown mode."];
        if (!TimeOnly.TryParseExact(request.RunAt ?? string.Empty, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var runAt))
        {
            errors["runAt"] = ["Use a time of day as HH:mm."];
        }

        Range("minAccountAgeDays", request.MinAccountAgeDays, 0, 365);
        Range("minApprovedEvents", request.MinApprovedEvents, 0, 100);
        Range("rejectionLookbackDays", request.RejectionLookbackDays, 0, 730);
        Range("maxAutoApprovalsPerOwnerPerDay", request.MaxAutoApprovalsPerOwnerPerDay, 1, 100);
        Range("maxDaysAhead", request.MaxDaysAhead, 1, 730);
        Range("maxDurationDays", request.MaxDurationDays, 1, 60);
        Range("maxPrice", request.MaxPrice, 0m, 100_000m);
        Range("duplicateSimilarity", request.DuplicateSimilarity, 0.5, 1.0);
        Range("duplicateDistanceMeters", request.DuplicateDistanceMeters, 0, 10_000);

        var bannedWords = (request.BannedWords ?? [])
            .Select(word => word?.Trim() ?? string.Empty)
            .Where(word => word.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (bannedWords.Length > MaximumBannedWords || bannedWords.Any(word => word.Length > MaximumBannedWordLength))
        {
            errors["bannedWords"] = [$"Use at most {MaximumBannedWords} entries of up to {MaximumBannedWordLength} characters each."];
        }

        if (errors.Count > 0) throw new AutoModerationSettingsException(errors);

        return new AutoModerationSettings
        {
            Mode = request.Mode,
            RunAtLocal = runAt,
            MinAccountAgeDays = request.MinAccountAgeDays,
            MinApprovedEvents = request.MinApprovedEvents,
            RejectionLookbackDays = request.RejectionLookbackDays,
            MaxAutoApprovalsPerOwnerPerDay = request.MaxAutoApprovalsPerOwnerPerDay,
            MaxDaysAhead = request.MaxDaysAhead,
            MaxDurationDays = request.MaxDurationDays,
            MaxPrice = request.MaxPrice,
            DuplicateSimilarity = request.DuplicateSimilarity,
            DuplicateDistanceMeters = request.DuplicateDistanceMeters,
            BannedWords = bannedWords,
        };
    }
}

public sealed class AutoModerationSettingsException(IReadOnlyDictionary<string, string[]> errors) : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
