namespace Hollowdeck.Data;

// One line of between-fights advice, shown under the reward list.
//
// An id rather than a bare array of strings, even though nothing looks a tip
// up by id. Two reasons: every content row in data/ has one, and a failing
// assertion about a tip that is too long or carries a bad key hint has to be
// able to *name* the row - "tip 9 of 14" sends a reader counting lines.
//
// Text is plain ASCII and may carry one substitution: {hd_some_action} is
// replaced with the key currently bound to that action, resolved through
// ScreenKeyboardNav.KeyHint. That exists so a tip naming a key cannot drift
// from project.godot's [input] the way a hardcoded "Press D" would - the same
// reason KeyHint exists for the HUD's own badges.
public class TipDefinition
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}
