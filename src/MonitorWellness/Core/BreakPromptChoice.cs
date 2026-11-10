namespace MonitorWellness.Core;

/// <summary>What the person did with the break reminder prompt.</summary>
public enum BreakPromptChoice
{
    /// <summary>Start the 20-second focus break.</summary>
    StartBreak,

    /// <summary>Ask again in a few minutes.</summary>
    Snooze,

    /// <summary>Dismissed on purpose.</summary>
    Skip,

    /// <summary>Nobody answered; it went away by itself.</summary>
    TimedOut,
}
