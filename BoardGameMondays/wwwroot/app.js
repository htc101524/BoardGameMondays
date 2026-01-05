window.bgm = window.bgm || {};

window.bgm.scrollToTopNextFrame = (behavior) => {
    const resolvedBehavior = behavior || "smooth";

    window.requestAnimationFrame(() => {
        window.scrollTo({ top: 0, left: 0, behavior: resolvedBehavior });
    });
};

window.bgm.scrollToIdNextFrame = (id, behavior) => {
    const resolvedBehavior = behavior || "smooth";

    window.requestAnimationFrame(() => {
        if (!id) {
            return;
        }

        const target = document.getElementById(id);
        if (!target) {
            return;
        }

        const headerHeightRaw = getComputedStyle(document.documentElement)
            .getPropertyValue("--bgm-header-height")
            .trim();
        const headerHeight = Number.parseFloat(headerHeightRaw || "0") || 0;

        const rect = target.getBoundingClientRect();
        const targetTop = rect.top + window.scrollY - headerHeight;
        window.scrollTo({ top: Math.max(0, targetTop), left: 0, behavior: resolvedBehavior });
    });
};

// Extract a lightweight 3-color palette from an image and apply it to an element as CSS vars.
// Vars set: --bgm-c1-rgb, --bgm-c2-rgb, --bgm-c3-rgb (comma-separated RGB, e.g. "123, 45, 67")
(() => {
    const paletteCache = new Map();

    const clamp = (n, min, max) => Math.max(min, Math.min(max, n));

    const toRgbString = (r, g, b) => `${r}, ${g}, ${b}`;

    const distSq = (a, b) => {
        const dr = a.r - b.r;
        const dg = a.g - b.g;
        const db = a.b - b.b;
        return dr * dr + dg * dg + db * db;
    };

    const pickDistinct = (candidates, count) => {
        const chosen = [];
        const minDist = 60 * 60; // squared distance

        for (const c of candidates) {
            if (chosen.length === 0) {
                chosen.push(c);
                if (chosen.length === count) break;
                continue;
            }

            let ok = true;
            for (const picked of chosen) {
                if (distSq(c, picked) < minDist) {
                    ok = false;
                    break;
                }
            }

            if (ok) {
                chosen.push(c);
                if (chosen.length === count) break;
            }
        }

        while (chosen.length < count) {
            chosen.push(chosen[chosen.length - 1] || { r: 0, g: 0, b: 0 });
        }

        return chosen;
    };

    const computePalette = async (imageUrl) => {
        if (!imageUrl) {
            return null;
        }

        if (paletteCache.has(imageUrl)) {
            return paletteCache.get(imageUrl);
        }

        const img = new Image();
        img.decoding = "async";
        // Best-effort: this only works if the image server allows CORS.
        img.crossOrigin = "anonymous";
        img.src = imageUrl;

        await new Promise((resolve, reject) => {
            img.onload = resolve;
            img.onerror = reject;
        });

        const canvas = document.createElement("canvas");
        const target = 72;
        const scale = Math.min(1, target / Math.max(img.width || 1, img.height || 1));
        canvas.width = Math.max(1, Math.floor((img.width || target) * scale));
        canvas.height = Math.max(1, Math.floor((img.height || target) * scale));

        const ctx = canvas.getContext("2d", { willReadFrequently: true });
        if (!ctx) {
            return null;
        }

        ctx.drawImage(img, 0, 0, canvas.width, canvas.height);

        let data;
        try {
            data = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
        } catch {
            // Tainted canvas (CORS) or other failure.
            return null;
        }

        // Quantize to 4 bits/channel => 4096 buckets
        const counts = new Map();
        const step = 4; // skip pixels for speed

        for (let i = 0; i < data.length; i += 4 * step) {
            const r = data[i];
            const g = data[i + 1];
            const b = data[i + 2];
            const a = data[i + 3];
            if (a < 180) continue;

            // luminance + "colorfulness" filters
            const lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            if (lum < 18 || lum > 245) continue;

            const max = Math.max(r, g, b);
            const min = Math.min(r, g, b);
            if (max - min < 16) continue; // too gray

            const rq = r >> 4;
            const gq = g >> 4;
            const bq = b >> 4;
            const key = (rq << 8) | (gq << 4) | bq;
            counts.set(key, (counts.get(key) || 0) + 1);
        }

        const buckets = Array.from(counts.entries())
            .sort((a, b) => b[1] - a[1])
            .slice(0, 24)
            .map(([key, weight]) => {
                const rq = (key >> 8) & 0xf;
                const gq = (key >> 4) & 0xf;
                const bq = key & 0xf;
                // convert back to a representative color (center of bucket)
                const r = clamp(rq * 16 + 8, 0, 255);
                const g = clamp(gq * 16 + 8, 0, 255);
                const b = clamp(bq * 16 + 8, 0, 255);
                return { r, g, b, weight };
            });

        if (buckets.length === 0) {
            return null;
        }

        const chosen = pickDistinct(buckets, 3);
        const palette = {
            c1: toRgbString(chosen[0].r, chosen[0].g, chosen[0].b),
            c2: toRgbString(chosen[1].r, chosen[1].g, chosen[1].b),
            c3: toRgbString(chosen[2].r, chosen[2].g, chosen[2].b)
        };

        paletteCache.set(imageUrl, palette);
        return palette;
    };

    window.bgm.applyPaletteFromImage = async (elementId, imageUrl) => {
        if (!elementId) return false;
        const el = document.getElementById(elementId);
        if (!el) return false;
        if (!imageUrl) return false;

        const palette = await computePalette(imageUrl);
        if (!palette) return false;

        el.style.setProperty("--bgm-c1-rgb", palette.c1);
        el.style.setProperty("--bgm-c2-rgb", palette.c2);
        el.style.setProperty("--bgm-c3-rgb", palette.c3);
        return true;
    };

    window.bgm.clearPalette = (elementId) => {
        if (!elementId) return;
        const el = document.getElementById(elementId);
        if (!el) return;
        el.style.removeProperty("--bgm-c1-rgb");
        el.style.removeProperty("--bgm-c2-rgb");
        el.style.removeProperty("--bgm-c3-rgb");
    };
})();
