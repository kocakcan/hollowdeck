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

    public void DiscardHand()
    {
        Discard.AddRange(Hand);
        Hand.Clear();
    }

    public void ExhaustCard(CardInstance card)
    {
        Hand.Remove(card);
        Exhaust.Add(card);
    }
}
