using NorthLife.Api.Models;

namespace NorthLife.Api.Moderation;

/// <summary>A pending event as the rules see it.</summary>
public sealed record ModerationCandidate(
    Guid EventId,
    int Revision,
    Guid OwnerId,
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    string VenueName,
    string OrganizerName,
    double Latitude,
    double Longitude,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc,
    decimal Price);

/// <summary>What the owner's history says about them.</summary>
/// <param name="PublishedEvents">The owner's events that are published now (not deleted).</param>
/// <param name="LastRejectionAtUtc">The latest rejection of any of the owner's events, from the audit log.</param>
/// <param name="AutoApprovalsToday">Approvals by the service for this owner since Israel midnight.</param>
public sealed record OwnerRecord(
    DateTimeOffset CreatedAtUtc,
    bool EmailConfirmed,
    bool Suspended,
    int PublishedEvents,
    DateTimeOffset? LastRejectionAtUtc,
    int AutoApprovalsToday);

/// <summary>Why an event was held, with the numbers behind it; the admin page turns it into a Hebrew sentence.</summary>
public sealed record ModerationReason(string Code, IReadOnlyDictionary<string, object>? Values = null);

/// <summary>
/// The four terms of automatic approval. Every term is checked and every failure is reported, so the
/// note on a held event lists everything the admin should look at, not just the first problem.
/// An event is approved only when the list is empty. Pure and deterministic.
/// </summary>
public static class ModerationRules
{
    public static IReadOnlyList<ModerationReason> Evaluate(
        ModerationCandidate candidate,
        OwnerRecord owner,
        DuplicateMatch? duplicate,
        AutoModerationSettings settings,
        DateTimeOffset now,
        int passedEarlierThisRun)
    {
        var reasons = new List<ModerationReason>();
        void Hold(string code, object? values = null) =>
            reasons.Add(new ModerationReason(code, values is null ? null : ToDictionary(values)));

        // 1. Owner track record.
        if (owner.Suspended) Hold("owner_suspended");
        if (!owner.EmailConfirmed) Hold("owner_email_unconfirmed");
        var accountAge = now - owner.CreatedAtUtc;
        if (accountAge < TimeSpan.FromDays(settings.MinAccountAgeDays))
        {
            Hold("owner_new_account", new { days = (int)Math.Floor(Math.Max(0, accountAge.TotalDays)), need = settings.MinAccountAgeDays });
        }

        if (owner.PublishedEvents < settings.MinApprovedEvents)
        {
            Hold("owner_few_approvals", new { have = owner.PublishedEvents, need = settings.MinApprovedEvents });
        }

        if (owner.LastRejectionAtUtc is { } rejectedAt && now - rejectedAt < TimeSpan.FromDays(settings.RejectionLookbackDays))
        {
            Hold("owner_recent_rejection", new { daysAgo = (int)Math.Floor((now - rejectedAt).TotalDays) });
        }

        if (owner.AutoApprovalsToday + passedEarlierThisRun >= settings.MaxAutoApprovalsPerOwnerPerDay)
        {
            Hold("owner_daily_cap", new { cap = settings.MaxAutoApprovalsPerOwnerPerDay });
        }

        // 2. Place and time.
        if (candidate.EndAtUtc <= now) Hold("ended");
        if (!NorthRegion.Contains(candidate.Latitude, candidate.Longitude)) Hold("outside_region");
        if (candidate.StartAtUtc > now + TimeSpan.FromDays(settings.MaxDaysAhead))
        {
            Hold("too_far_ahead", new { days = (int)Math.Ceiling((candidate.StartAtUtc - now).TotalDays), max = settings.MaxDaysAhead });
        }

        var duration = candidate.EndAtUtc - candidate.StartAtUtc;
        if (duration > TimeSpan.FromDays(settings.MaxDurationDays))
        {
            Hold("too_long", new { days = (int)Math.Ceiling(duration.TotalDays), max = settings.MaxDurationDays });
        }

        if (candidate.Price > settings.MaxPrice)
        {
            Hold("price_too_high", new { price = candidate.Price, max = settings.MaxPrice });
        }

        // 3. Text.
        string[] texts = [candidate.Title, candidate.Description, candidate.VenueName, candidate.OrganizerName, .. candidate.Tags];
        if (ContentScanner.FindBannedWord(texts, settings.BannedWords) is { } word) Hold("banned_word", new { word });
        foreach (var kind in ContentScanner.FindContactDetails(texts)) Hold("contact_details", new { kind });

        // 4. Duplicates.
        if (duplicate is not null)
        {
            Hold("possible_duplicate", new { eventId = duplicate.EventId, title = duplicate.Title, similarity = Math.Round(duplicate.Similarity, 2) });
        }

        return reasons;
    }

    private static Dictionary<string, object> ToDictionary(object values) =>
        values.GetType().GetProperties().ToDictionary(property => property.Name, property => property.GetValue(values)!);
}
