window.bgm = window.bgm || {};

window.bgm.getCookie = (name) => {
    if (!name) return null;
    const encoded = encodeURIComponent(name) + "=";
    const parts = document.cookie.split(";");
    for (const part of parts) {
        const trimmed = part.trim();
        if (trimmed.startsWith(encoded)) {
            return decodeURIComponent(trimmed.substring(encoded.length));
        }
    }
    return null;
};

window.bgm.setViewAsNonAdmin = (enabled) => {
    const name = "bgm_viewAsNonAdmin";
    const base = `${encodeURIComponent(name)}=${enabled ? "1" : ""}; path=/; samesite=lax`;
    const secure = window.location && window.location.protocol === "https:" ? "; secure" : "";

    if (enabled) {
        // 1 year
        document.cookie = base + "; max-age=" + (60 * 60 * 24 * 365) + secure;
    } else {
        document.cookie = base + "; max-age=0" + secure;
    }
};

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

window.bgm.getBackgroundHex = (element) => {
    const toHex2 = (n) => {
        const h = Number(n).toString(16);
        return h.length === 1 ? "0" + h : h;
    };

    const rgbStringToHex = (rgb) => {
        if (!rgb) return null;
        const m = rgb.match(/rgba?\((\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*([0-9.]+))?\)/i);
        if (!m) return null;
        const r = Math.max(0, Math.min(255, Number(m[1]) || 0));
        const g = Math.max(0, Math.min(255, Number(m[2]) || 0));
        const b = Math.max(0, Math.min(255, Number(m[3]) || 0));
        return "#" + toHex2(r) + toHex2(g) + toHex2(b);
    };

    // If background is transparent, walk up until we find something non-transparent.
    let el = element;
    while (el) {
        const bg = window.getComputedStyle(el).backgroundColor;
        if (bg && bg !== "transparent" && !bg.startsWith("rgba(0, 0, 0, 0")) {
            return rgbStringToHex(bg);
        }
        el = el.parentElement;
    }

    return null;
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

    const rgbToHsl = (r, g, b) => {
        const rn = r / 255;
        const gn = g / 255;
        const bn = b / 255;

        const max = Math.max(rn, gn, bn);
        const min = Math.min(rn, gn, bn);
        const d = max - min;

        let h = 0;
        if (d !== 0) {
            switch (max) {
                case rn:
                    h = ((gn - bn) / d) % 6;
                    break;
                case gn:
                    h = (bn - rn) / d + 2;
                    break;
                default:
                    h = (rn - gn) / d + 4;
                    break;
            }

            h *= 60;
            if (h < 0) h += 360;
        }

        const l = (max + min) / 2;
        const s = d === 0 ? 0 : d / (1 - Math.abs(2 * l - 1));
        return { h, s, l };
    };

    const hueDistance = (a, b) => {
        const d = Math.abs(a.h - b.h);
        return Math.min(d, 360 - d);
    };

    const pickPalette = (buckets) => {
        const candidates = buckets
            .map(b => {
                const hsl = rgbToHsl(b.r, b.g, b.b);
                const satBoost = Math.pow(0.35 + hsl.s, 1.35);
                const lightPenalty = 1 - Math.min(0.55, Math.abs(hsl.l - 0.55));
                const score = b.weight * satBoost * lightPenalty;
                return { ...b, ...hsl, score };
            })
            .sort((a, b) => b.score - a.score);

        const filter = (minSat, minL, maxL) =>
            candidates.filter(c => c.s >= minSat && c.l >= minL && c.l <= maxL);

        let pool = filter(0.22, 0.16, 0.88);
        if (pool.length < 3) pool = filter(0.14, 0.12, 0.92);
        if (pool.length < 3) pool = candidates;

        const chosen = [];
        chosen.push(pool[0] || { r: 0, g: 0, b: 0, h: 0, s: 0, l: 0, score: 0 });

        const pickNext = () => {
            let best = null;
            let bestScore = -Infinity;

            for (const c of pool) {
                if (chosen.includes(c)) continue;

                let minHue = Infinity;
                let minRgb = Infinity;
                for (const p of chosen) {
                    minHue = Math.min(minHue, hueDistance(c, p));
                    minRgb = Math.min(minRgb, Math.sqrt(distSq(c, p)));
                }

                // Prefer hue separation strongly; then RGB separation; then intrinsic candidate score.
                const sepScore = (minHue * 2.4) + (minRgb * 0.9) + (c.score * 0.02);
                if (sepScore > bestScore) {
                    bestScore = sepScore;
                    best = c;
                }
            }

            return best;
        };

        chosen.push(pickNext() || chosen[0]);
        chosen.push(pickNext() || chosen[1]);

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

        const chosen = pickPalette(buckets);
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

        try {
            const palette = await computePalette(imageUrl);
            if (!palette) {
                el.dataset.bgmPalette = "0";
                return false;
            }

            el.style.setProperty("--bgm-c1-rgb", palette.c1);
            el.style.setProperty("--bgm-c2-rgb", palette.c2);
            el.style.setProperty("--bgm-c3-rgb", palette.c3);

            // Compute contrast-aware fallback variants for c1 and c2.
            const parseRgb = (s) => s.split(',').map(x => Number(x.trim()) || 0);
            const toRgbString = (r, g, b) => `${Math.round(r)}, ${Math.round(g)}, ${Math.round(b)}`;

            const srgbToLinear = (v) => {
                v = v / 255;
                return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
            };
            const luminanceFromRgb = (rgb) => {
                return 0.2126 * srgbToLinear(rgb[0]) + 0.7152 * srgbToLinear(rgb[1]) + 0.0722 * srgbToLinear(rgb[2]);
            };
            const contrastRatio = (L1, L2) => {
                const lighter = Math.max(L1, L2);
                const darker = Math.min(L1, L2);
                return (lighter + 0.05) / (darker + 0.05);
            };

            // Get computed background colour for the element (fall back to white if transparent)
            const bgCss = window.getComputedStyle(el).backgroundColor;
            const m = (bgCss || '').match(/rgba?\((\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*([0-9.]+))?\)/i);
            const bgRgb = m ? [Number(m[1]), Number(m[2]), Number(m[3])] : [255, 255, 255];
            const bgL = luminanceFromRgb(bgRgb);

            try {
                for (let i = 0; i < 2; i++) {
                    const key = i === 0 ? 'c1' : 'c2';
                    const raw = parseRgb(palette[key]);

                    // Candidate palette color
                    const cand = raw;
                    const candL = luminanceFromRgb(cand);
                    let best = { rgb: cand, ratio: contrastRatio(candL, bgL) };

                    // Candidate fallback mixes to try
                    const ensureCandidates = [
                        [cand[0] * 0.66, cand[1] * 0.66, cand[2] * 0.66], // moderate darken
                        [cand[0] * 0.45, cand[1] * 0.45, cand[2] * 0.45], // stronger darken
                        [0, 0, 0], // black
                        [255, 255, 255] // white
                    ];

                    for (const c of ensureCandidates) {
                        const Lc = luminanceFromRgb(c);
                        const r = contrastRatio(Lc, bgL);
                        if (r > best.ratio) {
                            best = { rgb: c, ratio: r };
                        }
                    }

                    // Set the contrast var to the best candidate (prefer >= 4.5 but fall back to best available)
                    el.style.setProperty("--bgm-" + key + "-contrast-rgb", toRgbString(best.rgb[0], best.rgb[1], best.rgb[2]));
                }
            }
            catch (e) {
                // ignore parsing failures
            }
            el.dataset.bgmPalette = "1";
            return true;
        } catch {
            el.dataset.bgmPalette = "0";
            return false;
        }
    };

    window.bgm.clearPalette = (elementId) => {
        if (!elementId) return;
        const el = document.getElementById(elementId);
        if (!el) return;
        el.style.removeProperty("--bgm-c1-rgb");
        el.style.removeProperty("--bgm-c2-rgb");
        el.style.removeProperty("--bgm-c3-rgb");
        el.dataset.bgmPalette = "0";
    };
})();

// Bind a lightweight scroll progress value (0..1) to the Thoughts section as CSS variable.
// Used for scroll-driven styling (title fill + score highlight).
window.bgm.bindThoughtsScrollProgress = (sectionId) => {
    const clamp = (n, min, max) => Math.max(min, Math.min(max, n));

    let disposed = false;
    let raf = 0;

    const update = () => {
        if (disposed) {
            return;
        }

        const root = sectionId ? document.getElementById(sectionId) : null;
        if (!root) {
            return;
        }

        const title = root.querySelector(".bgm-thoughtsHero__title") || root;
        const rect = title.getBoundingClientRect();

        // Progress 0 when the title is near the bottom of the viewport; 1 when it's near the top.
        const start = window.innerHeight * 0.85;
        const end = window.innerHeight * 0.25;
        const denom = (start - end) || 1;
        const progress = clamp((start - rect.top) / denom, 0, 1);

        root.style.setProperty("--bgm-thoughts-progress", progress.toFixed(4));
        // Transient value: 1 when not filled, 0 when fully filled. Use to fade transient glows.
        const transient = Math.max(0, 1 - progress);
        root.style.setProperty("--bgm-thoughts-transient", transient.toFixed(4));
    };

    const schedule = () => {
        if (disposed) {
            return;
        }

        if (raf) {
            return;
        }

        raf = window.requestAnimationFrame(() => {
            raf = 0;
            update();
        });
    };

    window.addEventListener("scroll", schedule, { passive: true });
    window.addEventListener("resize", schedule, { passive: true });

    // Initial value
    update();

    // Always return a disposable object so Blazor can safely dispose it.
    return {
        dispose: () => {
            if (disposed) {
                return;
            }

            disposed = true;
            window.removeEventListener("scroll", schedule);
            window.removeEventListener("resize", schedule);
            if (raf) {
                window.cancelAnimationFrame(raf);
                raf = 0;
            }
        }
    };
};
