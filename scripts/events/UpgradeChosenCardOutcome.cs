using System.Collections.Generic;
using Hollowdeck.Data;
using Hollowdeck.Run;

namespace Hollowdeck.Events;

// Upgrades one card the player picks - a rest site's Smith, offered as an
// event outcome. Selectable() and the actual upgrade both go through the same
// helpers UpgradeRandomCardOutcome uses, so the chosen and random forms can
// never disagree about what counts as upgradable.
public class UpgradeChosenCardOutcome : ICardPickerOutcome
{
    public string Prompt => "Choose a card to sharpen.";

    public IEnumerable<int> Selectable() => UpgradeRandomCardOutcome.Upgradable();

    public string Apply(int deckIndex)
    {
        RunState.Deck[deckIndex] = CardUpgrade.Apply(RunState.Deck[deckIndex]);
        return $"{RunState.Deck[deckIndex].Name} is keener than it was.";
    }

    public string? Execute(EventOutcomeSpec spec) =>
        "Every card you carry is already as sharp as it will get.";
}
