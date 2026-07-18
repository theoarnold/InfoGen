// Share helpers for Blazor (clipboard + popup + share menu)
window.ficipediaShare = {
    copyToClipboard: function (text) {
        return navigator.clipboard && navigator.clipboard.writeText(text);
    },
    // Older/locked-down desktop browsers (or non-HTTPS/unfocused-tab edge cases) can lack or
    // reject navigator.clipboard entirely - this covers those via the legacy execCommand approach.
    legacyCopyToClipboard: function (text) {
        var textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.setAttribute('readonly', '');
        textarea.style.position = 'fixed';
        textarea.style.left = '-9999px';
        textarea.style.top = '0';
        document.body.appendChild(textarea);
        textarea.focus();
        textarea.select();
        var success = false;
        try {
            success = document.execCommand('copy');
        } catch (e) {
            success = false;
        }
        document.body.removeChild(textarea);
        return success;
    },
    openPopup: function (url, width, height) {
        var w = width || 600;
        var h = height || 400;
        var left = (screen.width - w) / 2;
        var top = (screen.height - h) / 2;
        window.open(url, 'share', 'width=' + w + ',height=' + h + ',left=' + left + ',top=' + top + ',scrollbars=yes');
    },
    copyCurrentLink: function (button) {
        var original = button.textContent;
        var reset = function () {
            setTimeout(function () {
                button.textContent = original;
                window.ficipediaShare.closeShareMenu(button);
            }, 1200);
        };
        var showResult = function (success) {
            button.textContent = success ? 'Copied!' : 'Copy failed';
            reset();
        };
        var url = window.location.href;
        var result;
        try {
            result = window.ficipediaShare.copyToClipboard(url);
        } catch (e) {
            result = null;
        }
        if (result && typeof result.then === 'function') {
            result.then(function () {
                showResult(true);
            }, function () {
                // Modern API failed/was denied - fall back to the legacy method rather than giving up.
                showResult(window.ficipediaShare.legacyCopyToClipboard(url));
            });
        } else {
            // No Clipboard API available at all - go straight to the legacy fallback.
            showResult(window.ficipediaShare.legacyCopyToClipboard(url));
        }
    },
    shareToX: function (button) {
        var url = window.location.href;
        var title = document.title.replace(/ - Ficipedia$/, '');
        var shareUrl = 'https://twitter.com/intent/tweet?text=' + encodeURIComponent(title) + '&url=' + encodeURIComponent(url);
        window.ficipediaShare.openPopup(shareUrl, 600, 400);
        if (button) window.ficipediaShare.closeShareMenu(button);
    },
    closeShareMenu: function (elementInsideMenu) {
        var wrapper = elementInsideMenu.closest('.share-menu-wrapper');
        if (!wrapper) return;
        var menu = wrapper.querySelector('.share-menu');
        var toggleButton = wrapper.querySelector('.btn-share');
        if (menu) menu.hidden = true;
        if (toggleButton) toggleButton.setAttribute('aria-expanded', 'false');
    },
    toggleShareMenu: function (button) {
        var wrapper = button.closest('.share-menu-wrapper');
        var menu = wrapper.querySelector('.share-menu');
        var isOpen = !menu.hidden;

        // Close any other open share menus on the page first.
        document.querySelectorAll('.share-menu').forEach(function (m) { m.hidden = true; });

        if (isOpen) {
            button.setAttribute('aria-expanded', 'false');
            return;
        }

        menu.hidden = false;
        button.setAttribute('aria-expanded', 'true');

        var closeOnOutsideClick = function (e) {
            if (!wrapper.contains(e.target)) {
                menu.hidden = true;
                button.setAttribute('aria-expanded', 'false');
                document.removeEventListener('click', closeOnOutsideClick);
            }
        };
        // Deferred so the current click (which is what opened the menu) doesn't immediately close it.
        setTimeout(function () {
            document.addEventListener('click', closeOnOutsideClick);
        }, 0);
    }
};
