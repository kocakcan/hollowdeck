using Godot;
using Hollowdeck.Data;

namespace Hollowdeck.Effects;

// Puts a card into one of the player's piles at runtime. The primitive the
// whole of ROADMAP Phase 7 waited on: Curses, Status cards, self-replicating
// cards and every event downside that isn't HP or gold are all downstream of
// this one action existing.
//
// Deliberately resolves the piles through ctx.Combat.Player rather than
// casting ctx.Source the way DrawCardsEffect does. Two reasons: the player is
// the only pile owner in the game, so there is no other correct answer; and it
// lets an *enemy* move shuffle a card into the player's deck, which is a shape
// Phase 8 wants and which a source cast would have quietly refused. Scope is
// therefore meaningless here - the same shape gain_gold already has, pinned by
// EffectSmokeTest's gain_gold_ignores_targets.
public class AddCardEffect : IEffect
{
    public void Execute(EffectContext ctx, EffectSpec spec)
    {
        if (spec.CardId is not { Length: > 0 } id)
        {
            GD.PushError("AddCardEffect: spec has no cardId - the card/relic/potion "
                + "authoring it will silently do nothing.");
            return;
        }

        // Find rather than Get: a typo in cards.json must name itself here
        // rather than throw a KeyNotFoundException out of combat resolution.
        var definition = CardDatabase.Find(id);
        if (definition is null)
        {
            GD.PushError($"AddCardEffect: no card with id '{id}'.");
            return;
        }

        // No Math.Max(1, ...) clamp: an authored amount of 0 adds nothing, and
        // hiding that behind a clamp would turn an authoring bug into a card
        // that works for the wrong reason. EffectSmokeTest audits every
        // authored add_card spec for a count instead.
        ctx.Combat.Player.Piles.AddCard(definition, spec.Pile, ctx.AmountFor(spec));
    }
}
