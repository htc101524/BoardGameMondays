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
