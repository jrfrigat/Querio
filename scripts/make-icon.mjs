// Draws icon.png, the package icon NuGet shows next to Querio on nuget.org.
//
// NuGet will not accept an SVG, so one raster file has to exist. Rather than commit a binary
// nobody can review and nobody can regenerate, the mark is drawn here from the same geometry as
// assets/logo.svg - no dependencies, no toolchain, `node scripts/make-icon.mjs` and it is current.
//
// Keep this in step with assets/logo.svg if the mark ever changes; the SVG stays the master for
// anything that renders vector (README, docs, favicon).
//
import { deflateSync } from 'node:zlib';
import { writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const SIZE = 256;      // final pixel size; NuGet renders at 128 but a 256 source stays crisp
const SS = 4;          // supersampling factor, so the curves do not come out jagged
const W = SIZE * SS;

const bg = [0x0e, 0x17, 0x29];
const bar = [0x1e, 0x3a, 0x5f];
const teal = [0x5e, 0xea, 0xd4];
const blue = [0x25, 0x63, 0xeb];

/** The ring's gradient runs corner to corner, the same as the SVG's linearGradient. */
const ringColour = (x, y) => {
    const t = Math.min(1, Math.max(0, (x / W + y / W) / 2));
    return [
        Math.round(teal[0] + (blue[0] - teal[0]) * t),
        Math.round(teal[1] + (blue[1] - teal[1]) * t),
        Math.round(teal[2] + (blue[2] - teal[2]) * t),
    ];
};

// -- shapes, all in the SVG's 256 coordinate space -----------------------------------------------

const s = v => v * SS;

const inRoundedRect = (x, y, rx, ry, w, h, r) => {
    const cx = Math.min(Math.max(x, rx + r), rx + w - r);
    const cy = Math.min(Math.max(y, ry + r), ry + h - r);
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r
        || (x >= rx && x <= rx + w && y >= ry + r && y <= ry + h - r)
        || (y >= ry && y <= ry + h && x >= rx + r && x <= rx + w - r);
};

const inRing = (x, y, cx, cy, radius, width) => {
    const d = Math.hypot(x - cx, y - cy);
    return d >= radius - width / 2 && d <= radius + width / 2;
};

const inDisc = (x, y, cx, cy, r) => (x - cx) ** 2 + (y - cy) ** 2 <= r * r;

/** A thick line with round caps: the distance from the point to the segment. */
const inCapsule = (x, y, x1, y1, x2, y2, width) => {
    const dx = x2 - x1, dy = y2 - y1;
    const len2 = dx * dx + dy * dy;
    const t = len2 === 0 ? 0 : Math.min(1, Math.max(0, ((x - x1) * dx + (y - y1) * dy) / len2));
    return Math.hypot(x - (x1 + t * dx), y - (y1 + t * dy)) <= width / 2;
};

// -- paint ---------------------------------------------------------------------------------------

const pixels = Buffer.alloc(W * W * 3);

for (let y = 0; y < W; y++) {
    for (let x = 0; x < W; x++) {
        let colour = bg;

        // The rounded card. Outside it the pixel stays background, which for a square PNG is the
        // same colour anyway - NuGet composites the icon on its own surface.
        if (!inRoundedRect(x, y, 0, 0, s(256), s(256), s(56))) colour = bg;

        // Rows of data under the lens.
        if (inRoundedRect(x, y, s(44), s(70), s(66), s(14), s(7))
            || inRoundedRect(x, y, s(44), s(118), s(52), s(14), s(7))
            || inRoundedRect(x, y, s(44), s(166), s(72), s(14), s(7))) {
            colour = bar;
        }

        // The Q: the ring, then the tail that doubles as the letter's stroke.
        if (inRing(x, y, s(132), s(120), s(60), s(18))
            || inCapsule(x, y, s(150), s(138), s(206), s(194), s(22))) {
            colour = ringColour(x, y);
        }

        // One query leaving for several targets.
        if (inCapsule(x, y, s(186), s(62), s(212), s(62), s(9))
            || inCapsule(x, y, s(186), s(62), s(212), s(36), s(9))
            || inCapsule(x, y, s(186), s(62), s(212), s(88), s(9))
            || inDisc(x, y, s(216), s(36), s(9))
            || inDisc(x, y, s(216), s(62), s(9))
            || inDisc(x, y, s(216), s(88), s(9))) {
            colour = teal;
        }

        const at = (y * W + x) * 3;
        pixels[at] = colour[0];
        pixels[at + 1] = colour[1];
        pixels[at + 2] = colour[2];
    }
}

// Average each SS x SS block down to one output pixel.
const out = Buffer.alloc(SIZE * SIZE * 3);
for (let y = 0; y < SIZE; y++) {
    for (let x = 0; x < SIZE; x++) {
        let r = 0, g = 0, b = 0;
        for (let sy = 0; sy < SS; sy++) {
            for (let sx = 0; sx < SS; sx++) {
                const at = ((y * SS + sy) * W + (x * SS + sx)) * 3;
                r += pixels[at]; g += pixels[at + 1]; b += pixels[at + 2];
            }
        }
        const n = SS * SS;
        const at = (y * SIZE + x) * 3;
        out[at] = Math.round(r / n);
        out[at + 1] = Math.round(g / n);
        out[at + 2] = Math.round(b / n);
    }
}

// -- encode ---------------------------------------------------------------------------------------

const crcTable = (() => {
    const table = new Int32Array(256);
    for (let n = 0; n < 256; n++) {
        let c = n;
        for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        table[n] = c;
    }
    return table;
})();

const crc32 = buffer => {
    let c = 0xffffffff;
    for (const byte of buffer) c = crcTable[(c ^ byte) & 0xff] ^ (c >>> 8);
    return (c ^ 0xffffffff) >>> 0;
};

const chunk = (type, data) => {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(body));
    return Buffer.concat([length, body, crc]);
};

const header = Buffer.alloc(13);
header.writeUInt32BE(SIZE, 0);
header.writeUInt32BE(SIZE, 4);
header[8] = 8;   // bit depth
header[9] = 2;   // colour type: truecolour
// 10..12 stay zero: deflate, adaptive filtering, no interlace.

// Each scanline is prefixed with its filter byte; 0 (none) keeps the encoder honest and simple,
// and the image is flat enough that deflate handles it well anyway.
const raw = Buffer.alloc(SIZE * (SIZE * 3 + 1));
for (let y = 0; y < SIZE; y++) {
    raw[y * (SIZE * 3 + 1)] = 0;
    out.copy(raw, y * (SIZE * 3 + 1) + 1, y * SIZE * 3, (y + 1) * SIZE * 3);
}

const png = Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
]);

const target = resolve(dirname(fileURLToPath(import.meta.url)), '..', 'icon.png');
writeFileSync(target, png);
console.log(`icon.png written: ${SIZE}x${SIZE}, ${png.length} bytes`);
