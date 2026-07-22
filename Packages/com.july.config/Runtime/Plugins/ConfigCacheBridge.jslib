mergeInto(LibraryManager.library, {
    JulyGetConfigCache: function() {
        var cache = '';
        if (typeof window !== 'undefined' && window.__JULY_CONFIG_CACHE)
            cache = window.__JULY_CONFIG_CACHE;
        else if (typeof GameGlobal !== 'undefined' && GameGlobal.__JULY_CONFIG_CACHE)
            cache = GameGlobal.__JULY_CONFIG_CACHE;
        var len = lengthBytesUTF8(cache) + 1;
        var buf = _malloc(len);
        stringToUTF8(cache, buf, len);
        return buf;
    }
});
