//! The one light — `docs/ART_SPEC.md` §10.
//!
//! `shapes.rs` exists so 192 icons share one *form* vocabulary. This file is
//! the other half: they share one *light* as well. The source is up and to the
//! left, everywhere, for every icon, forever.
//!
//! Why upper-left rather than a choice: `cards::strike` — the card the whole
//! Attack half of the set quotes — was already lit from the upper left, as
//! were `flask`, `droplet`, `raised_fist` and most of the hand-placed
//! highlights across the category modules. The direction was already there in
//! two thirds of the set; what was missing was anything holding the other
//! third to it.
//!
//! ## What this file is for
//!
//! A shape does not take "which side is lit" as a parameter. It computes it
//! from `LIGHT`. That is the whole point — the same argument the game makes for
//! deriving `IsPlayable` from `CardType` rather than authoring a sixth bool: a
//! wrong-side highlight should be *unrepresentable*, not discouraged.
//!
//! `blade` is the case that proves it. It used to paint its bright edge on the
//! side that was "leading" relative to the blade's own rotation, which is a
//! different screen side depending on where the blade points. Three of the
//! thirty-six authored blades came out lit on their shadow side, and one of
//! them — `annihilate`, a crossed pair — carried two opposed light sources
//! inside one 32x32 icon.
//!
//! Note that **colours are still the caller's**. Nothing here lightens or
//! darkens arithmetically; `shapes.rs`'s header explains why (a multiplied
//! colour lands off the §5 ramp and `artgen validate` would correctly reject
//! it). What is derived is *position*, never pigment.
//!
//! The tempting generalisation — a pass that walks a finished silhouette and
//! paints its up-left boundary one ramp step brighter — was considered and
//! declined. It needs a "next entry in this family" function `palette.rs` does
//! not have, it would move all 192 icons at once, and its failure mode is
//! silent: a family already at its top entry (`N8`, `G5`, `R5`, `V5`, `B5`,
//! `E4`, `P4`) would flatten to no rim at all with nothing to say so.
//!
//! ## Named exceptions
//!
//! These have no lit side, and none of them is a defect. Anything not listed
//! here is directional and takes its highlight position from this module:
//!
//! - `flame`, `orb`, `sparkle` — **emissive**. They *are* the light, locally.
//!   Giving one a lit side would say it is lit by something else.
//! - `eye` — the `N8` field is an aperture, not a face. Lighting one side of a
//!   hole is a mistake rather than a convention.
//! - `barb` — the tip is bright because it is thin. `misc.rs` mirrors barbs in
//!   pairs, and a derived tip would dull one thorn of each pair.
//! - `arrow`, `crack` — marks at 1–3px weight. No volume, so no faces.
//! - `skull`'s jaw band — **occlusion**, which is a different phenomenon from
//!   incidence and does not contradict a direction. (Its cranium crescent *is*
//!   directional; the two live in one function and are not the same thing.)
//! - `sword`'s guard and pommel — unlit by **budget** rather than by physics.
//!   At 1x a 2px crossguard has no room for two values.
//!
//! One entry runs the other way and is worth stating as an exception rather
//! than leaving to look like an oversight: **`raised_fist` keeps a highlight on
//! the knuckle the lamp cannot reach.** Dropping it to the body colour is what
//! §10 says, and it was tried — at 1x the fourth finger vanished into the hand
//! and the fist read as having three. §1's legibility budget outranks this
//! module when they disagree. `cards::clothesline` makes the opposite trade on
//! the same form, because its `N0` finger grooves already separate the fingers
//! and the knuckles are free to fall into shadow.
//!
//! **That list is data now, and it used to be three copies of prose.** It was
//! written here, restated in `shapes.rs`'s own module header — which had
//! already drifted, omitting `tower_shield` — and declared a third time in each
//! shape's doc comment. Nothing read any of them, so marking `flame`
//! directional in the wrong direction left every check in the repository green.
//!
//! `shapes::SHAPE_LIGHT` is the single copy; `LightClass` below is its
//! vocabulary. Three assertions hold it: every public shape must appear in the
//! table, the table must agree with the doc comment beside each shape, and
//! every `Directional` row must actually be measured by a light assertion. The
//! last is the one with teeth — the sweep drove a hand-written six, so a
//! seventh directional shape was unmeasured and green, which is how `sword` and
//! `tower_shield` turn out to have had no light assertion at all.
//!
//! What this does **not** close is `docs/ART_SPEC.md` §10's standing exemption:
//! the ~186 hand-placed highlights in the category modules are still held by
//! nothing, for the reason that section gives. A class list being wrong and a
//! hand-placed highlight being wrong are different failures, and only the first
//! of them is expressible in a table.

/// Which of §10's three classes a shape belongs to.
///
/// `Directional` is the only one that means work: it is a claim that the shape
/// derives a lit side from `LIGHT`, and therefore that something measures it.
/// The other two are claims that there is nothing to measure — an emissive
/// shape *is* the light locally, and a symmetric one has no face — which is why
/// they are declared rather than merely omitted. A shape that is simply missing
/// from the table is neither of those; it is a shape nobody has decided about.
///
/// Read only by assertions today, hence the `allow`. Left public and outside
/// `cfg(test)` deliberately: this is a declaration *about* the drawing code, in
/// the same file as the lamp it refers to, and a reader looking for "what class
/// is this shape" should not have to know it lives in a test build.
#[allow(dead_code)]
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum LightClass {
    /// Derives its lit side from `LIGHT`. Must carry a light assertion.
    Directional,
    /// A light source: lit from inside, so the lamp does not apply.
    Emissive,
    /// Concentric, single-colour, or a mark too thin to have faces.
    Symmetric,
}

/// Unit vector the light **travels** along: down and to the right, so the
/// source is up and to the left.
///
/// Both components are positive because `canvas.rs` puts y = 0 at the top. The
/// sign confusion this invites is exactly why every shape goes through
/// `is_lit` rather than writing its own comparison.
pub const LIGHT: (f32, f32) = (
    std::f32::consts::FRAC_1_SQRT_2,
    std::f32::consts::FRAC_1_SQRT_2,
);

/// How much of the lamp a face with outward normal `n` receives, as a signed
/// projection: **negative means lit**, because `LIGHT` is the direction the
/// light travels and a lit face turns back against it.
///
/// The one place that dot product is written. `is_lit` and `lit_offset` are
/// both defined on it rather than each computing their own, so the two cannot
/// answer differently about the same face.
pub fn incidence(n: (f32, f32)) -> f32 {
    n.0 * LIGHT.0 + n.1 * LIGHT.1
}

/// A face with outward normal `n` is lit iff it turns back into the light.
pub fn is_lit(n: (f32, f32)) -> bool {
    incidence(n) < 0.0
}

/// Which of a shape's two candidate faces is the lit one, as the signed
/// multiplier to apply to its `across` vector.
///
/// A shape with two opposed long faces — a blade, a plate, a stem — computes
/// one `across` and asks this which way to push the highlight. The shadow side
/// is the negation, so `-lit_offset(across)` is how a shape places a shade.
///
/// At a true tie — both faces exactly edge-on, within `GRAZING` — neither side
/// is lit and the answer is arbitrary. It breaks toward the **upper** face,
/// because a shape lit on top reads as lit and one lit underneath reads as
/// floating.
///
/// **`GRAZING` is a float-equality epsilon and must stay one.** It was `0.05`
/// for one commit, on the theory that a wider band buys stability against a
/// one-pixel change in a tip coordinate. It does the opposite, and the damage
/// was measurable: 0.05 is a ~3° wedge in which the tiebreak *overrides* a
/// real answer, and three authored blades sat inside it. `wild_swing`
/// (incidence +0.0499) and `map/elite`'s left sword (+0.0303) had their bright
/// edges moved onto the **shadow** face — the exact defect this module exists
/// to make unrepresentable — while `map/fight`'s matching sword at +0.0555 fell
/// outside the band and kept its lit edge, so the two crossed-sword map icons,
/// which are meant to be the same weapon in two colours, lit the same blade on
/// opposite faces. That is `annihilate`'s two suns rebuilt across a pair.
///
/// A one-pixel flip at a *genuine* tie is harmless, because there both choices
/// look the same. A band wide enough to contain real answers is not a tie.
pub fn lit_offset(across: (f32, f32)) -> f32 {
    let incidence = incidence(across);
    if incidence.abs() <= GRAZING {
        return if across.1 <= 0.0 { 1.0 } else { -1.0 };
    }
    if incidence < 0.0 {
        1.0
    } else {
        -1.0
    }
}

/// Float-equality epsilon for "both faces are exactly edge-on to the lamp".
/// See `lit_offset` for why this is not a taste knob.
///
/// `pub(crate)` so the test that pins the tiebreak reads *this* value instead
/// of copying it. A copy hid art drift: with the number written out in the
/// test, moving it to 0.0495 silently re-rendered `wild_swing` and moving it to
/// 0.20 re-rendered `feint` and `map/fight`, with every assertion green.
pub(crate) const GRAZING: f32 = 1e-3;

/// How far a face insets from its outline: `lit` where the light lands,
/// `shade` where it does not.
///
/// Which of the two is the larger is the *caller's*, and deliberately so —
/// `shield` reveals a rim brighter than its face, so its wide band goes on the
/// lit side, while `gem` reveals a body darker than its face and wants the
/// wide band on the shadow side. Same call, opposite arguments. Folding that
/// choice in here would make one of the two shapes wrong.
pub fn by_face(normal: (f32, f32), lit: i32, shade: i32) -> i32 {
    if is_lit(normal) {
        lit
    } else {
        shade
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_light_is_a_unit_vector() {
        let length = (LIGHT.0 * LIGHT.0 + LIGHT.1 * LIGHT.1).sqrt();
        assert!(
            (length - 1.0).abs() < 1e-5,
            "LIGHT must be normalised; is_lit's dot product is a projection, not a comparison \
             against a magnitude, and lit_offset callers scale by it: length {length}"
        );
    }

    #[test]
    fn the_source_is_up_and_to_the_left() {
        // A face pointing up-left is lit; one pointing down-right is not. If
        // this ever inverts, every directional shape in the set flips at once
        // and nothing else would say so.
        assert!(is_lit((-1.0, -1.0)), "an up-left face must be lit");
        assert!(!is_lit((1.0, 1.0)), "a down-right face must be shadowed");
        assert!(is_lit((-1.0, 0.0)), "a left-facing face must be lit");
        assert!(is_lit((0.0, -1.0)), "an up-facing face must be lit");
    }

    /// `GRAZING` is a float epsilon, not a design knob. A band wide enough to
    /// contain a real answer lets the tiebreak override it — which is exactly
    /// what happened at 0.05, and it put two shipped blades' bright edges on
    /// their shadow face.
    #[test]
    fn the_grazing_band_is_a_float_epsilon() {
        assert!(
            GRAZING < 0.01,
            "GRAZING is {GRAZING}; anything this wide is a wedge of real angles, \
             not a tie. The smallest authored blade incidence is 0.0303."
        );
    }

    #[test]
    fn opposed_faces_never_agree() {
        // The property every shape depends on: given one `across`, exactly one
        // of the two sides comes back lit. A shape that got `true` for both
        // would paint its highlight over its own shadow.
        //
        // The two terminator angles are excluded rather than asserted. There
        // both faces are edge-on, the dot product is zero to within float
        // error, and the documented tie-break sends both to the same side.
        // That is a real hole and it is a harmless one: a face exactly edge-on
        // to the light has no lit side to get wrong.
        for step in 0..64 {
            let angle = step as f32 * std::f32::consts::TAU / 64.0;
            let n = (angle.cos(), angle.sin());
            if incidence(n).abs() <= GRAZING {
                continue;
            }
            let opposite = (-n.0, -n.1);
            assert_ne!(
                lit_offset(n),
                lit_offset(opposite),
                "faces at {angle} rad and its opposite both resolved the same way"
            );
        }
    }
}
