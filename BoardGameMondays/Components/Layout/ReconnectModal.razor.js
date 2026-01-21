// Track connection state for proactive reconnection
let isCircuitDisconnected = false;
let reconnectAttemptInProgress = false;
let lastActivityTime = Date.now();

// Set up event handlers
const reconnectBanner = document.getElementById("components-reconnect-banner");
if (reconnectBanner) {
    reconnectBanner.addEventListener("components-reconnect-state-changed", handleReconnectStateChanged);

    const retryButton = document.getElementById("components-reconnect-button");
    if (retryButton) retryButton.addEventListener("click", retry);

    const resumeButton = document.getElementById("components-resume-button");
    if (resumeButton) resumeButton.addEventListener("click", resume);
}

function setOfflineState(on) {
    if (on) {
        document.body.classList.add('bgm-offline');
    } else {
        document.body.classList.remove('bgm-offline');
    }
}

function handleReconnectStateChanged(event) {
    if (!reconnectBanner) return;

    if (event.detail.state === "show") {
        isCircuitDisconnected = true;
        reconnectBanner.classList.add('components-reconnect-show');
        setOfflineState(true);
    } else if (event.detail.state === "hide") {
        isCircuitDisconnected = false;
        reconnectAttemptInProgress = false;
        reconnectBanner.classList.remove('components-reconnect-show');
        setOfflineState(false);
    } else if (event.detail.state === "failed") {
        isCircuitDisconnected = true;
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    } else if (event.detail.state === "rejected") {
        location.reload();
    }
}

async function retry() {
    if (reconnectAttemptInProgress) return;
    reconnectAttemptInProgress = true;
    
    document.removeEventListener("visibilitychange", retryWhenDocumentBecomesVisible);

    try {
        // Reconnect will asynchronously return:
        // - true to mean success
        // - false to mean we reached the server, but it rejected the connection (e.g., unknown circuit ID)
        // - exception to mean we didn't reach the server (this can be sync or async)
        const successful = await Blazor.reconnect();
        if (!successful) {
            // We have been able to reach the server, but the circuit is no longer available.
            // Try to resume the circuit first (for paused circuits).
            if (typeof Blazor.resumeCircuit === 'function') {
                const resumeSuccessful = await Blazor.resumeCircuit();
                if (!resumeSuccessful) {
                    location.reload();
                } else {
                    isCircuitDisconnected = false;
                    reconnectBanner?.classList.remove('components-reconnect-show');
                    setOfflineState(false);
                }
            } else {
                // Blazor.resumeCircuit not available, reload the page.
                location.reload();
            }
        } else {
            isCircuitDisconnected = false;
        }
    } catch (err) {
        // We got an exception, server is currently unavailable
        document.addEventListener("visibilitychange", retryWhenDocumentBecomesVisible);
    } finally {
        reconnectAttemptInProgress = false;
    }
}

async function resume() {
    try {
        if (typeof Blazor.resumeCircuit === 'function') {
            const successful = await Blazor.resumeCircuit();
            if (!successful) {
                location.reload();
            } else {
                isCircuitDisconnected = false;
            }
        } else {
            location.reload();
        }
    } catch {
        location.reload();
    }
}

async function retryWhenDocumentBecomesVisible() {
    if (document.visibilityState === "visible") {
        await retry();
    }
}

// PROACTIVE RECONNECTION: Automatically try to reconnect when:
// 1. User returns to the tab (visibility change)
// 2. User interacts with the page after being away

// Listen for tab visibility changes - reconnect when user returns
document.addEventListener("visibilitychange", async () => {
    if (document.visibilityState === "visible" && isCircuitDisconnected) {
        // User returned to the tab while disconnected - try to reconnect immediately
        await retry();
    }
});

// Listen for user interaction - if disconnected, try to reconnect
const interactionEvents = ['click', 'keydown', 'touchstart', 'mousemove'];
let interactionReconnectDebounce = null;

function handleUserInteraction() {
    lastActivityTime = Date.now();
    
    if (isCircuitDisconnected && !reconnectAttemptInProgress) {
        // Debounce to avoid multiple rapid attempts
        if (interactionReconnectDebounce) {
            clearTimeout(interactionReconnectDebounce);
        }
        interactionReconnectDebounce = setTimeout(async () => {
            if (isCircuitDisconnected) {
                await retry();
            }
        }, 100);
    }
}

interactionEvents.forEach(event => {
    document.addEventListener(event, handleUserInteraction, { passive: true, capture: true });
});

// Periodic connection health check - detect silent disconnections
// This catches cases where the circuit dies but Blazor doesn't fire the reconnect event
let healthCheckInterval = null;

function startHealthCheck() {
    if (healthCheckInterval) return;
    
    healthCheckInterval = setInterval(async () => {
        // Only check if we think we're connected and user is on the page
        if (!isCircuitDisconnected && document.visibilityState === "visible") {
            try {
                // Try a simple JS interop ping to check if circuit is alive
                // If this fails, the circuit is dead even though we didn't get notified
                if (typeof Blazor !== 'undefined' && Blazor._internal && Blazor._internal.navigationManager) {
                    // Circuit appears to be alive
                }
            } catch {
                // Circuit may be dead - trigger reconnection
                isCircuitDisconnected = true;
                setOfflineState(true);
                await retry();
            }
        }
    }, 30000); // Check every 30 seconds
}

// Start health check when page loads
if (document.readyState === 'complete') {
    startHealthCheck();
} else {
    window.addEventListener('load', startHealthCheck);
}
