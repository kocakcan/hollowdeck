//! An RGBA8 pixel buffer plus the drawing primitives the icon authoring uses,
//! and PNG load/save.
//!
//! Everything here is deliberately hard-edged: no anti-aliasing, no alpha
//! blending, no sub-pixel coordinates. `set` overwrites. That is not a
//! simplification, it is the spec — `docs/ART_SPEC.md` §3/§4 rule out soft
//! edges, and a primitive that could produce one would let them back in
//! through the tool that exists to keep them out.
//!
//! Coordinates are `i32` and out-of-bounds writes are silently dropped, so an
//! icon can draw a shape that runs off the grid without arithmetic guards at
//! every call site.

use std::fs::File;
use std::io::BufWriter;
use std::path::Path;

use crate::palette::{Rgb, Rgba, TRANSPARENT};

pub struct Canvas {
    pub width: u32,
    pub height: u32,
    pixels: Vec<Rgba>,
}

impl Canvas {
    pub fn new(width: u32, height: u32) -> Self {
        Canvas {
            width,
            height,
            pixels: vec![TRANSPARENT; (width * height) as usize],
        }
    }

    pub fn load(path: &Path) -> Result<Self, String> {
        let file = File::open(path).map_err(|e| format!("{}: {e}", path.display()))?;
        let mut decoder = png::Decoder::new(file);
        // Assets in the repo genuinely vary — some of the CC0 tiles are 4-bit
        // indexed, some are 8-bit RGBA. EXPAND unpacks palettes, sub-8-bit
        // greyscale and tRNS chunks so everything below sees 8-bit samples.
        decoder.set_transformations(png::Transformations::EXPAND);
        let mut reader = decoder
            .read_info()
            .map_err(|e| format!("{}: {e}", path.display()))?;
        let mut buffer = vec![0u8; reader.output_buffer_size()];
        let info = reader
            .next_frame(&mut buffer)
            .map_err(|e| format!("{}: {e}", path.display()))?;

        let channels = info.color_type.samples();
        let depth = info.bit_depth as usize;
        if depth != 8 {
            return Err(format!(
                "{}: {depth}-bit PNG; artgen only reads 8-bit",
                path.display()
            ));
        }
        let pixels = buffer[..(info.width * info.height) as usize * channels]
            .chunks_exact(channels)
            .map(|p| match channels {
                1 => Rgba(p[0], p[0], p[0], 255),
                2 => Rgba(p[0], p[0], p[0], p[1]),
                3 => Rgba(p[0], p[1], p[2], 255),
                _ => Rgba(p[0], p[1], p[2], p[3]),
            })
            .collect();

        Ok(Canvas {
            width: info.width,
            height: info.height,
            pixels,
        })
    }

    pub fn save(&self, path: &Path) -> Result<(), String> {
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent).map_err(|e| format!("{}: {e}", parent.display()))?;
        }
        let file = File::create(path).map_err(|e| format!("{}: {e}", path.display()))?;
        let mut encoder = png::Encoder::new(BufWriter::new(file), self.width, self.height);
        encoder.set_color(png::ColorType::Rgba);
        encoder.set_depth(png::BitDepth::Eight);
        let mut writer = encoder
            .write_header()
            .map_err(|e| format!("{}: {e}", path.display()))?;
        let bytes: Vec<u8> = self
            .pixels
            .iter()
            .flat_map(|p| [p.0, p.1, p.2, p.3])
            .collect();
        writer
            .write_image_data(&bytes)
            .map_err(|e| format!("{}: {e}", path.display()))
    }

    pub fn pixels(&self) -> &[Rgba] {
        &self.pixels
    }

    fn index(&self, x: i32, y: i32) -> Option<usize> {
        if x < 0 || y < 0 || x >= self.width as i32 || y >= self.height as i32 {
            return None;
        }
        Some(y as usize * self.width as usize + x as usize)
    }

    pub fn get(&self, x: i32, y: i32) -> Rgba {
        match self.index(x, y) {
            Some(i) => self.pixels[i],
            None => TRANSPARENT,
        }
    }

    pub fn set(&mut self, x: i32, y: i32, colour: Rgb) {
        if let Some(i) = self.index(x, y) {
            self.pixels[i] = colour.opaque();
        }
    }

    pub fn set_raw(&mut self, x: i32, y: i32, colour: Rgba) {
        if let Some(i) = self.index(x, y) {
            self.pixels[i] = colour;
        }
    }

    pub fn map<F: FnMut(Rgba) -> Rgba>(&mut self, mut f: F) {
        for pixel in self.pixels.iter_mut() {
            *pixel = f(*pixel);
        }
    }

    // -- shapes -----------------------------------------------------------

    pub fn rect(&mut self, x: i32, y: i32, w: i32, h: i32, colour: Rgb) {
        for dy in 0..h {
            for dx in 0..w {
                self.set(x + dx, y + dy, colour);
            }
        }
    }

    pub fn hline(&mut self, x: i32, y: i32, len: i32, colour: Rgb) {
        self.rect(x, y, len, 1, colour);
    }

    pub fn vline(&mut self, x: i32, y: i32, len: i32, colour: Rgb) {
        self.rect(x, y, 1, len, colour);
    }

    /// Bresenham, 1px wide. Diagonals come out as a staircase with no
    /// corner-touching gaps, which is what reads as a hand-drawn pixel line.
    pub fn line(&mut self, x0: i32, y0: i32, x1: i32, y1: i32, colour: Rgb) {
        let (dx, dy) = ((x1 - x0).abs(), -(y1 - y0).abs());
        let (sx, sy) = (if x0 < x1 { 1 } else { -1 }, if y0 < y1 { 1 } else { -1 });
        let (mut x, mut y) = (x0, y0);
        let mut error = dx + dy;
        loop {
            self.set(x, y, colour);
            if x == x1 && y == y1 {
                break;
            }
            let e2 = 2 * error;
            if e2 >= dy {
                error += dy;
                x += sx;
            }
            if e2 <= dx {
                error += dx;
                y += sy;
            }
        }
    }

    /// A line thickened by stamping a `weight`x`weight` block at each step.
    /// Cheaper to reason about than a proper stroke and, at 32px, identical.
    pub fn thick_line(&mut self, x0: i32, y0: i32, x1: i32, y1: i32, weight: i32, colour: Rgb) {
        let mut stamps = Canvas::new(self.width, self.height);
        stamps.line(x0, y0, x1, y1, colour);
        for y in 0..self.height as i32 {
            for x in 0..self.width as i32 {
                if stamps.get(x, y).is_opaque() {
                    self.rect(x, y, weight, weight, colour);
                }
            }
        }
    }

    /// Filled circle. `radius` is measured to the outer edge of the ring, and
    /// the `+ 0.25` fattens the test just enough that small radii come out as
    /// recognisable discs rather than plus signs.
    pub fn disc(&mut self, cx: i32, cy: i32, radius: i32, colour: Rgb) {
        let limit = (radius as f32 + 0.25) * (radius as f32 + 0.25);
        for dy in -radius..=radius {
            for dx in -radius..=radius {
                if (dx * dx + dy * dy) as f32 <= limit {
                    self.set(cx + dx, cy + dy, colour);
                }
            }
        }
    }

    pub fn ring(&mut self, cx: i32, cy: i32, radius: i32, thickness: i32, colour: Rgb) {
        let outer = (radius as f32 + 0.25) * (radius as f32 + 0.25);
        let inner_radius = (radius - thickness) as f32 + 0.25;
        let inner = inner_radius * inner_radius;
        for dy in -radius..=radius {
            for dx in -radius..=radius {
                let d = (dx * dx + dy * dy) as f32;
                if d <= outer && d > inner {
                    self.set(cx + dx, cy + dy, colour);
                }
            }
        }
    }

    /// Filled polygon by even-odd scanline at pixel centres. Handles concave
    /// outlines, which most of the weapon silhouettes are.
    pub fn poly(&mut self, points: &[(i32, i32)], colour: Rgb) {
        if points.len() < 3 {
            return;
        }
        let min_y = points.iter().map(|p| p.1).min().unwrap();
        let max_y = points.iter().map(|p| p.1).max().unwrap();
        for y in min_y..=max_y {
            let scan = y as f32 + 0.5;
            let mut crossings: Vec<f32> = Vec::new();
            for i in 0..points.len() {
                let (x0, y0) = points[i];
                let (x1, y1) = points[(i + 1) % points.len()];
                let (fy0, fy1) = (y0 as f32, y1 as f32);
                if (fy0 <= scan) == (fy1 <= scan) {
                    continue;
                }
                let t = (scan - fy0) / (fy1 - fy0);
                crossings.push(x0 as f32 + t * (x1 - x0) as f32);
            }
            crossings.sort_by(|a, b| a.partial_cmp(b).unwrap());
            for pair in crossings.chunks_exact(2) {
                let (from, to) = (pair[0].round() as i32, pair[1].round() as i32);
                for x in from..to {
                    self.set(x, y, colour);
                }
            }
            // A polygon one pixel wide at some scanline can round both
            // crossings to the same x and vanish. Icons rely on thin tapers
            // (blade tips, flame points), so keep at least one pixel.
            if crossings.len() == 2 && crossings[0].round() as i32 == crossings[1].round() as i32 {
                self.set(crossings[0].round() as i32, y, colour);
            }
        }
    }

    // -- passes -----------------------------------------------------------

    /// Surrounds every opaque pixel with `colour` on its four empty
    /// neighbours. This is what makes an icon separate from a dark backdrop —
    /// the reason `ROADMAP.md` Phase 3 lists it as a batch job — and it is why
    /// icons are authored inset one pixel from the grid edge.
    pub fn outline(&mut self, colour: Rgb) {
        self.outline_with(colour, false);
    }

    /// `diagonal` also fills the four corner neighbours, giving a heavier edge
    /// that closes staircase gaps. Better on chunky shapes, too fat on thin
    /// ones, so it's the caller's choice.
    pub fn outline_with(&mut self, colour: Rgb, diagonal: bool) {
        let source = Canvas {
            width: self.width,
            height: self.height,
            pixels: self.pixels.clone(),
        };
        let neighbours: &[(i32, i32)] = if diagonal {
            &[
                (-1, 0),
                (1, 0),
                (0, -1),
                (0, 1),
                (-1, -1),
                (1, -1),
                (-1, 1),
                (1, 1),
            ]
        } else {
            &[(-1, 0), (1, 0), (0, -1), (0, 1)]
        };
        for y in 0..self.height as i32 {
            for x in 0..self.width as i32 {
                if source.get(x, y).is_opaque() {
                    continue;
                }
                if neighbours
                    .iter()
                    .any(|(dx, dy)| source.get(x + dx, y + dy).is_opaque())
                {
                    self.set(x, y, colour);
                }
            }
        }
    }

    /// Clears every pixel where `mask` is transparent — a cookie cutter.
    /// This is how a pattern gets confined to a silhouette (Fortify's bricks
    /// inside its shield) without re-deriving the silhouette's edge equation.
    pub fn intersect(&mut self, mask: &Canvas) {
        for y in 0..self.height as i32 {
            for x in 0..self.width as i32 {
                if !mask.get(x, y).is_opaque() {
                    self.set_raw(x, y, TRANSPARENT);
                }
            }
        }
    }

    /// Cuts a hole. `set` overwrites rather than blends, so there is no way to
    /// subtract by drawing — a shape that removes material (Last Stand's
    /// broken blade, Whirlwind's gap) needs this instead.
    pub fn erase_poly(&mut self, points: &[(i32, i32)]) {
        let mut cutter = Canvas::new(self.width, self.height);
        cutter.poly(points, crate::palette::N8);
        for y in 0..self.height as i32 {
            for x in 0..self.width as i32 {
                if cutter.get(x, y).is_opaque() {
                    self.set_raw(x, y, TRANSPARENT);
                }
            }
        }
    }

    /// Cuts a circular hole — how a crescent is made (`cleave`'s axe head).
    pub fn erase_disc(&mut self, cx: i32, cy: i32, radius: i32) {
        let limit = (radius as f32 + 0.25) * (radius as f32 + 0.25);
        for dy in -radius..=radius {
            for dx in -radius..=radius {
                if (dx * dx + dy * dy) as f32 <= limit {
                    self.set_raw(cx + dx, cy + dy, TRANSPARENT);
                }
            }
        }
    }

    pub fn erase_rect(&mut self, x: i32, y: i32, w: i32, h: i32) {
        for dy in 0..h {
            for dx in 0..w {
                self.set_raw(x + dx, y + dy, TRANSPARENT);
            }
        }
    }

    /// Stamps `other` at an offset, skipping its transparent pixels.
    pub fn blit(&mut self, other: &Canvas, x: i32, y: i32) {
        for sy in 0..other.height as i32 {
            for sx in 0..other.width as i32 {
                let pixel = other.get(sx, sy);
                if pixel.is_opaque() {
                    self.set_raw(x + sx, y + sy, pixel);
                }
            }
        }
    }
}
