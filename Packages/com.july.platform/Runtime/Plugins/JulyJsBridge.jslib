// 微信好友接口与 C# 消息桥接，由 Platform 包随项目提供。
mergeInto(LibraryManager.library, {
    JulyBridge_Init: function(goNamePtr) {
        var goName = UTF8ToString(goNamePtr);
        var g = (typeof GameGlobal !== 'undefined') ? GameGlobal
              : (typeof window !== 'undefined') ? window : {};
        g.__JULY_BRIDGE_GO = goName;

        g.__JULY_BRIDGE_SEND = function(callbackId, status, data) {
            var payload = JSON.stringify({
                id: callbackId,
                status: status,
                data: (typeof data === 'string') ? data : JSON.stringify(data || null)
            });
            GameGlobal.Module.SendMessage(g.__JULY_BRIDGE_GO || 'JulyJsBridge', 'OnMessage', payload);
        };
    },

    JulyBridge_GetRelationFriendList: function(callbackId) {
        var g = (typeof GameGlobal !== 'undefined') ? GameGlobal
              : (typeof window !== 'undefined') ? window : {};
        var send = g.__JULY_BRIDGE_SEND;

        var apiExists =
            typeof wx !== 'undefined' &&
            typeof wx.getRelationFriendList === 'function';

        if (!apiExists) {
            send(callbackId, 'fail', { errMsg: 'getRelationFriendList:fail api not supported' });
            return;
        }

        wx.getRelationFriendList({
            success: function(res) {
                send(callbackId, 'success', res);
            },
            fail: function(res) {
                send(callbackId, 'fail', res);
            }
        });
    }
});
