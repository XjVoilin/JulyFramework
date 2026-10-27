mergeInto(LibraryManager.library, {
    July_TencentDn_IsReady: function () {
        return !!(GameGlobal.dnSDK && GameGlobal.dnSDK.getInitResult().inited);
    },

    July_TencentDn_Track: function (eventName, parameters) {
        if (!GameGlobal.dnSDK) {
            console.error('[TencentDn] SDK is unavailable; check exported initialization.');
            return 100;
        }
        return GameGlobal.dnSDK.track(
            UTF8ToString(eventName), JSON.parse(UTF8ToString(parameters))).code;
    },

    July_TencentDn_SetOpenId: function (openId) {
        if (!GameGlobal.dnSDK) {
            console.error('[TencentDn] SDK is unavailable; OpenID was not bound.');
            return 100;
        }
        return GameGlobal.dnSDK.setOpenId(UTF8ToString(openId)).code;
    }
});
