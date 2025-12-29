window.bgm = window.bgm || {};

window.bgm.scrollToTopNextFrame = (behavior) => {
    const resolvedBehavior = behavior || "smooth";

    window.requestAnimationFrame(() => {
        window.scrollTo({ top: 0, left: 0, behavior: resolvedBehavior });
    });
};
