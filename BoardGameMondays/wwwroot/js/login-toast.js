window.bgmLoginToast = {
    getStatus: function () {
        const dismissed = localStorage.getItem('bgm_login_toast_dismissed') === '1';
        const seen = sessionStorage.getItem('bgm_login_toast_seen') === '1';
        return {
            show: !dismissed && !seen,
            seen: seen
        };
    },

    markSeen: function () {
        sessionStorage.setItem('bgm_login_toast_seen', '1');
    },

    dismiss: function () {
        localStorage.setItem('bgm_login_toast_dismissed', '1');
        sessionStorage.setItem('bgm_login_toast_seen', '1');
    }
};
