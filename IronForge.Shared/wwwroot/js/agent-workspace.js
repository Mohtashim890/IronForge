window.ironForgeAgent = {
    scrollToBottom: function (element) {
        if (!element) {
            return;
        }

        element.scrollTo({
            top: element.scrollHeight,
            behavior: "smooth"
        });
    }
};