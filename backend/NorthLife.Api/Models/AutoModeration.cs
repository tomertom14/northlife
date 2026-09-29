namespace NorthLife.Api.Models;

/// <summary>What the automatic event approval service may do.</summary>
public enum AutoModerationMode
{
    /// <summary>The service does not run.</summary>
    Off,

    /// <summary>The service writes a note on each pending event but approves nothing.</summary>
    NotesOnly,

    /// <summary>The service approves events that pass every term and writes notes on the rest.</summary>
    Approve,
}

public enum AutoModerationTrigger
{
    Scheduled,
    Manual,
}

public enum AutoModerationOutcome
{
    Approved,
    WouldApprove,
    Held,
}

/// <summary>The single settings row of the automatic event approval service, edited by administrators.</summary>
public sealed class AutoModerationSettings
{
    public static readonly Guid SingletonId = Guid.Parse("0199a0f7-0017-7000-8000-a07011a70017");

    public Guid Id { get; set; } = SingletonId;
    public AutoModerationMode Mode { get; set; } = AutoModerationMode.NotesOnly;

    /// <summary>Israel local time of the daily run.</summary>
    public TimeOnly RunAtLocal { get; set; } = new(7, 0);

    public int MinAccountAgeDays { get; set; } = 7;
    public int MinApprovedEvents { get; set; } = 3;
    public int RejectionLookbackDays { get; set; } = 90;
    public int MaxAutoApprovalsPerOwnerPerDay { get; set; } = 5;
    public int MaxDaysAhead { get; set; } = 180;
    public int MaxDurationDays { get; set; } = 14;
    public decimal MaxPrice { get; set; } = 1000;
    public double DuplicateSimilarity { get; set; } = 0.85;
    public int DuplicateDistanceMeters { get; set; } = 1000;
    public string[] BannedWords { get; set; } = ["קזינו", "הימורים", "הלוואות"];

    /// <summary>Israel date of the last scheduled run that was claimed; guarantees one scheduled run a day.</summary>
    public DateOnly? LastScheduledRunDate { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>One run of the service: when, why, in which mode, and what it decided.</summary>
public sealed class AutoModerationRun
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public AutoModerationTrigger Trigger { get; set; }

    /// <summary>The administrator who pressed "run now"; null for the daily run.</summary>
    public Guid? TriggeredById { get; set; }
    public AutoModerationMode Mode { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public int Checked { get; set; }
    public int Approved { get; set; }
    public int WouldApprove { get; set; }
    public int Held { get; set; }
    public string? Error { get; set; }
}

/// <summary>The service's verdict on one revision of one event.</summary>
public sealed class AutoModerationDecision
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RunId { get; set; }
    public Guid EventId { get; set; }

    /// <summary>
    /// The revision the verdict applies to. For an approval this is the revision after approving, so
    /// "the note matches the event's current revision" works for every outcome.
    /// </summary>
    public int EventRevision { get; set; }
    public AutoModerationOutcome Outcome { get; set; }

    /// <summary>JSON array of hold reasons: <c>[{"code": "...", "values": {...}}]</c>; empty when nothing failed.</summary>
    public string Reasons { get; set; } = "[]";
    public DateTimeOffset DecidedAtUtc { get; set; }
}
