// Share helpers for Blazor (clipboard + popup)
window.ficipediaShare = {
    copyToClipboard: function (text) {
        return navigator.clipboard && navigator.clipboard.writeText(text);
    },
    openPopup: function (url, width, height) {
        var w = width || 600;
        var h = height || 400;
        var left = (screen.width - w) / 2;
        var top = (screen.height - h) / 2;
        window.open(url, 'share', 'width=' + w + ',height=' + h + ',left=' + left + ',top=' + top + ',scrollbars=yes');
    }
};
