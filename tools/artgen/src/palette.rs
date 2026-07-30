//! The shared 43-colour ramp — the Rust mirror of `PixelSpec.Ramp`.
//!
//! These two lists MUST agree: `PixelSpec.Clamp` clamps at runtime, this
//! clamps offline, and a colour that is on-ramp for one and off-ramp for the
//! other means `artgen validate` rejects assets the game happily draws (or,
//! worse, passes assets it shouldn't). `PixelSpecSmokeTest` parses the
//! `pub const` lines below and asserts they match `PixelSpec.Ramp.All`
//! exactly, so the declarations have to stay in this literal shape:
//!
//! ```text
//! pub const N0: Rgb = Rgb(0x07, 0x06, 0x0a);
//! ```
//!
//! Ordering is irrelevant to `nearest` but kept family-grouped so a validation
//! failure names a family a human can reason about.

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub struct Rgb(pub u8, pub u8, pub u8);

impl Rgb {
    pub const fn a(self, alpha: u8) -> Rgba {
        Rgba(self.0, self.1, self.2, alpha)
    }

    pub const fn opaque(self) -> Rgba {
        self.a(255)
    }
}

/// Straight RGBA8. Alpha is never a palette decision — see `clamp`.
#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub struct Rgba(pub u8, pub u8, pub u8, pub u8);

pub const TRANSPARENT: Rgba = Rgba(0, 0, 0, 0);

impl Rgba {
    pub fn rgb(self) -> Rgb {
        Rgb(self.0, self.1, self.2)
    }

    pub fn is_opaque(self) -> bool {
        self.3 > 0
    }
}

// Neutral — stone, bone, parchment
pub const N0: Rgb = Rgb(0x07, 0x06, 0x0a);
pub const N1: Rgb = Rgb(0x12, 0x10, 0x0f);
pub const N2: Rgb = Rgb(0x1e, 0x1a, 0x18);
pub const N3: Rgb = Rgb(0x2e, 0x27, 0x24);
pub const N4: Rgb = Rgb(0x45, 0x3b, 0x35);
pub const N5: Rgb = Rgb(0x66, 0x59, 0x50);
pub const N6: Rgb = Rgb(0x8c, 0x7d, 0x70);
pub const N7: Rgb = Rgb(0xc4, 0xb5, 0xa4);
pub const N8: Rgb = Rgb(0xed, 0xe4, 0xd4);

// Bronze / gold — chrome, rare, currency
pub const G0: Rgb = Rgb(0x3d, 0x2a, 0x12);
pub const G1: Rgb = Rgb(0x6b, 0x4a, 0x1f);
pub const G2: Rgb = Rgb(0x9c, 0x6f, 0x2e);
pub const G3: Rgb = Rgb(0xc7, 0x9e, 0x47);
pub const G4: Rgb = Rgb(0xeb, 0xc7, 0x66);
pub const G5: Rgb = Rgb(0xfb, 0xea, 0xa8);

// Oxblood / red — attack, damage, danger
pub const R0: Rgb = Rgb(0x24, 0x0d, 0x10);
pub const R1: Rgb = Rgb(0x3f, 0x14, 0x18);
pub const R2: Rgb = Rgb(0x52, 0x21, 0x21);
pub const R3: Rgb = Rgb(0x8c, 0x2b, 0x2b);
pub const R4: Rgb = Rgb(0xc9, 0x3f, 0x3f);
pub const R5: Rgb = Rgb(0xff, 0x59, 0x59);

// Verdigris / green — skill, heal, upgrade
pub const V0: Rgb = Rgb(0x0d, 0x1f, 0x1a);
pub const V1: Rgb = Rgb(0x1f, 0x42, 0x38);
pub const V2: Rgb = Rgb(0x2f, 0x6b, 0x52);
pub const V3: Rgb = Rgb(0x4a, 0x9c, 0x68);
pub const V4: Rgb = Rgb(0x73, 0xd9, 0x73);
pub const V5: Rgb = Rgb(0xa8, 0xf0, 0xa0);

// Steel / blue — block, uncommon
pub const B0: Rgb = Rgb(0x0f, 0x16, 0x26);
pub const B1: Rgb = Rgb(0x1e, 0x2c, 0x47);
pub const B2: Rgb = Rgb(0x35, 0x49, 0x6e);
pub const B3: Rgb = Rgb(0x59, 0x8c, 0xd9);
pub const B4: Rgb = Rgb(0x8c, 0xbf, 0xff);
pub const B5: Rgb = Rgb(0xc4, 0xdf, 0xff);

// Ember / orange — exhaust, act II
pub const E0: Rgb = Rgb(0x3d, 0x1a, 0x08);
pub const E1: Rgb = Rgb(0x6b, 0x2f, 0x0f);
pub const E2: Rgb = Rgb(0xa8, 0x54, 0x20);
pub const E3: Rgb = Rgb(0xf2, 0xa6, 0x4d);
pub const E4: Rgb = Rgb(0xff, 0xd9, 0xa0);

// Void / violet — act III, arcane, the Power card type when it lands
pub const P0: Rgb = Rgb(0x18, 0x0d, 0x24);
pub const P1: Rgb = Rgb(0x2e, 0x1a, 0x42);
pub const P2: Rgb = Rgb(0x4d, 0x2e, 0x6b);
pub const P3: Rgb = Rgb(0x7a, 0x52, 0xa3);
pub const P4: Rgb = Rgb(0xb0, 0x8c, 0xd9);

pub const RAMP: [Rgb; 43] = [
    N0, N1, N2, N3, N4, N5, N6, N7, N8, //
    G0, G1, G2, G3, G4, G5, //
    R0, R1, R2, R3, R4, R5, //
    V0, V1, V2, V3, V4, V5, //
    B0, B1, B2, B3, B4, B5, //
    E0, E1, E2, E3, E4, //
    P0, P1, P2, P3, P4,
];

/// Human-readable name for a ramp entry, for validator output. Falls back to
/// hex for anything off-ramp, which is exactly the case being reported.
pub fn name_of(colour: Rgb) -> String {
    const NAMES: [&str; 43] = [
        "N0", "N1", "N2", "N3", "N4", "N5", "N6", "N7", "N8", //
        "G0", "G1", "G2", "G3", "G4", "G5", //
        "R0", "R1", "R2", "R3", "R4", "R5", //
        "V0", "V1", "V2", "V3", "V4", "V5", //
        "B0", "B1", "B2", "B3", "B4", "B5", //
        "E0", "E1", "E2", "E3", "E4", //
        "P0", "P1", "P2", "P3", "P4",
    ];
    match RAMP.iter().position(|&c| c == colour) {
        Some(i) => NAMES[i].to_string(),
        None => hex(colour),
    }
}

pub fn hex(colour: Rgb) -> String {
    format!("#{:02x}{:02x}{:02x}", colour.0, colour.1, colour.2)
}

pub fn is_on_ramp(colour: Rgb) -> bool {
    RAMP.contains(&colour)
}

/// Nearest ramp entry by squared RGB distance.
///
/// Plain squared RGB rather than anything perceptual (Lab, redmean) on
/// purpose: `PixelSpec.Clamp` has to produce byte-identical results in C#, and
/// a perceptual metric is exactly the kind of thing that drifts between two
/// implementations. See the note at the top of this file.
pub fn nearest(colour: Rgb) -> Rgb {
    let mut best = RAMP[0];
    let mut best_distance = i32::MAX;
    for &candidate in RAMP.iter() {
        let dr = candidate.0 as i32 - colour.0 as i32;
        let dg = candidate.1 as i32 - colour.1 as i32;
        let db = candidate.2 as i32 - colour.2 as i32;
        let distance = dr * dr + dg * dg + db * db;
        if distance < best_distance {
            best_distance = distance;
            best = candidate;
        }
    }
    best
}

/// Clamps a pixel onto the ramp, carrying alpha through untouched — a
/// sprite's transparency is not a palette decision, and rounding it would eat
/// soft mask edges. Mirrors `PixelSpec.Clamp`.
pub fn clamp(pixel: Rgba) -> Rgba {
    if !pixel.is_opaque() {
        return TRANSPARENT;
    }
    nearest(pixel.rgb()).a(pixel.3)
}
