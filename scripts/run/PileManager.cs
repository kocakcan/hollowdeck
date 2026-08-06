using System.Collections.Generic;
using System.Linq;
using Hollowdeck.Data;

namespace Hollowdeck.Run;

public class PileManager
{
    public List<CardInstance> DrawPile = new();
    public List<CardInstance> Hand = new();
    public List<CardInstance> Discard = new();
    public List<CardInstance> Exhaust = new();

    // Powers played this fight. Its own pile rather than reusing Exhaust,
    // which would be functionally identical - both are "out of the fight" -
    // but says the wrong thing: Exhaust is a cost, and the combat HUD renders
    // it as one (the ember-tinted exit tween, the exhaust badge, its own
    // counter cell). A Power leaving play is the card working, not the card
    // being burned.
    //
    // Combat-only state like the other four: PileManager is rebuilt fresh from
    // RunState.Deck at the start of every fight, so nothing here is serialized
    // and this needs no save version bump.
    public List<CardInstance> Powers = new();

    public PileManager(IEnumerable<CardDefinition> startingDeck)
    {
        DrawPile = startingDeck.Select(d => new CardInstance(d)).ToList();
        Shuffle(DrawPile);
    }

    public void Shuffle(List<CardInstance> pile)
    {
        var rng = RngStreams.Combat;
        for (int i = pile.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pile[i], pile[j]) = (pile[j], pile[i]);
        }
    }

    public void DrawHand(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (DrawPile.Count == 0)
            {
                if (Discard.Count == 0) return;
                (DrawPile, Discard) = (Discard, DrawPile);
                Shuffle(DrawPile);
            }
            var top = DrawPile[^1];
            DrawPile.RemoveAt(DrawPile.Count - 1);
            Hand.Add(top);
        }
    }

    // The three pile keywords all resolve here rather than at the call site in
    // CombatManager.TryEndTurn, so there is one place to look and one place a
    // fourth keyword would join.
    //
    // Ethereal beats Retain, stated rather than left to fall out of the branch
    // order: nothing authors both today, and picking a winner explicitly is
    // cheaper than discovering the answer from a bug report. A card that says
    // "exhaust if you don't play it" must not be kept alive by a keyword that
    // says "keep it".
    //
    // Iterated backwards because it removes as it goes.
    public void DiscardHand()
    {
        for (int i = Hand.Count - 1; i >= 0; i--)
        {
            var card = Hand[i];
            if (card.Definition.Ethereal)
            {
                Hand.RemoveAt(i);
                Exhaust.Add(card);
            }
            else if (card.Definition.Retain)
            {
                // Stays. BeginPlayerTurn then draws a full hand on top of it,
                // so a retained card is a six-card hand - see CardDefinition.
            }
            else
            {
                Hand.RemoveAt(i);
                Discard.Add(card);
            }
        }
    }

    // Moves every Innate card to where DrawHand will take it first. Called
    // once from CombatManager.StartCombat, immediately after the opening
    // shuffle and before the opening draw.
    //
    // DrawHand pops from the *end* of DrawPile, so "drawn first" means "last
    // in the list" - which is the kind of inversion that reads as a bug six
    // months later, hence the name and this comment rather than a bare
    // AddRange at the call site.
    //
    // More Innate cards than the hand size needs no special case: the excess
    // stays on top and arrives next turn. That is asserted rather than assumed
    // (CardKeywordSmokeTest).
    public void PromoteInnate()
    {
        var innate = DrawPile.Where(c => c.Definition.Innate).ToList();
        if (innate.Count == 0) return;
        DrawPile.RemoveAll(c => c.Definition.Innate);
        DrawPile.AddRange(innate);
    }

    // The primitive everything in ROADMAP Phase 7 waited on: until this
    // existed, nothing could put a card into a pile at runtime, so Curses,
    // Status cards and every non-HP/non-gold event downside were unauthorable.
    //
    // Draw inserts at a random index rather than on top, because "shuffle it
    // into your draw pile" is what the genre means and what makes a Curse a
    // cost rather than a single bad turn. RngStreams.Combat is the right
    // stream: a card resolving is combat, and risk 2 asks for a new stream per
    // new *system*, not per new call site.
    public void AddCard(CardDefinition definition, CardPile pile, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var card = new CardInstance(definition);
            switch (pile)
            {
                case CardPile.Hand:
                    Hand.Add(card);
                    break;
                case CardPile.Draw:
                    DrawPile.Insert(RngStreams.Combat.Next(DrawPile.Count + 1), card);
                    break;
                default:
                    Discard.Add(card);
                    break;
            }
        }
    }

    public void ExhaustCard(CardInstance card)
    {
        Hand.Remove(card);
        Exhaust.Add(card);
    }
}
