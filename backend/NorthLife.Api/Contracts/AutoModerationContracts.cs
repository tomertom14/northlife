using System.Text.Json;
using NorthLife.Api.Models;

namespace NorthLife.Api.Contracts;

/// <summary>The editable settings; <see cref="RunAt"/> is Israel local time as "HH:mm".</summary>
public sealed record AutoModerationSettingsRequest(
    AutoModerationMode Mode,
    string RunAt,
    int MinAccountAgeDays,
    int MinApprovedEvents,
    int RejectionLookbackDays,
    int MaxAutoApprovalsPerOwnerPerDay,
    int MaxDaysAhead,
    int MaxDurationDays,
    decimal MaxPrice,
    double DuplicateSimilarity,
    int DuplicateDistanceMeters,
    IReadOnlyList<string> BannedWords);

public sealed record AutoModerationRunResponse(
    Guid Id,
    AutoModerationTrigger Trigger,
    string? TriggeredByName,
    AutoModerationMode Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int Checked,
    int Approved,
    int WouldApprove,
    int Held,
    bool Failed);

public sealed record AutoModerationOverview(
    AutoModerationSettingsRequest Settings,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? NextRunAt,
    IReadOnlyList<AutoModerationRunResponse> Runs);

/// <summary>The service's latest verdict on the event's current revision.</summary>
public sealed record AutoReviewResponse(AutoModerationOutcome Outcome, JsonElement Reasons, DateTimeOffset DecidedAt);
