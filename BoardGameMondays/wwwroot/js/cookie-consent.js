window.cookieConsent = {
    hasConsent: function () {
        return document.cookie.includes('bgm_cookie_consent=');
    },

    saveConsent: function (essential, analytics) {
        const consent = {
            essential: essential,
            analytics: analytics,
            version: '1.0',
            timestamp: new Date().toISOString()
        };
        const encoded = encodeURIComponent(JSON.stringify(consent));
        const expires = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000).toUTCString();
        document.cookie = `bgm_cookie_consent=${encoded}; expires=${expires}; path=/; SameSite=Lax; Secure`;
    },

    getConsent: function () {
        const match = document.cookie.match(/bgm_cookie_consent=([^;]+)/);
        if (match) {
            try {
                return JSON.parse(decodeURIComponent(match[1]));
            } catch {
                return null;
            }
        }
        return null;
    },

    hasAnalyticsConsent: function () {
        const consent = this.getConsent();
        return consent && consent.analytics === true;
    },

    clearConsent: function () {
        document.cookie = 'bgm_cookie_consent=; expires=Thu, 01 Jan 1970 00:00:00 UTC; path=/;';
    },

    showBanner: function () {
        // Clear existing consent to trigger banner re-display
        this.clearConsent();
        // Trigger Blazor to re-check consent state
        location.reload();
    }
};
