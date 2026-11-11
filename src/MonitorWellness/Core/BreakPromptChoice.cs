namespace MonitorWellness.Core;

/// <summary>Which reminder the prompt is for.</summary>
public enum BreakPromptKind
{
    /// <summary>The 20-20-20 eye break.</summary>
    Eye,

    /// <summary>The longer stand-up-and-move break.</summary>
    StandUp,
}

/// <summary>What the person did with the break reminder prompt.</summary>
public enum BreakPromptChoice
{
    /// <summary>Start the 20-second focus break.</summary>
    StartBreak,

    /// <summary>Ask again in a few minutes.</summary>
    Snooze,

    /// <summary>"I already took one": the break was taken without using the prompt, so the count restarts and it is counted as taken.</summary>
    AlreadyTookBreak,

    /// <summary>Dismissed on purpose.</summary>
    Skip,

    /// <summary>Nobody answered; it went away by itself.</summary>
    TimedOut,
}
