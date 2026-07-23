namespace Hollowdeck.Run;

public enum RunEndOutcome { Win, Lose }

// Same data-mailbox pattern as Combat.CombatContext - survives the scene
// change into RunEndScreen without needing a dedicated autoload.
public static class RunEndContext
{
    public static RunEndOutcome Outcome { get; set; }
}
