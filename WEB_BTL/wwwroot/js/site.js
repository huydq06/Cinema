(() => {
    const isInternalGet = (url) =>
        url.origin === window.location.origin &&
        url.protocol.startsWith("http") &&
        !url.hash;

    const replaceMain = (html, url, addHistory) => {
        const documentFromResponse = new DOMParser().parseFromString(html, "text/html");
        const currentMain = document.querySelector("main");
        const responseMain = documentFromResponse.querySelector("main");

        if (!currentMain || !responseMain) {
            window.location.href = url.href;
            return;
        }

        currentMain.replaceWith(responseMain);
        document.title = documentFromResponse.title || document.title;

        if (addHistory) {
            window.history.pushState({}, "", url.href);
        }

    };

    const loadPage = async (url, addHistory = true) => {
        document.body.classList.add("is-page-loading");

        try {
            const response = await fetch(url.href, {
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });

            if (!response.ok) {
                throw new Error(`Request failed: ${response.status}`);
            }

            replaceMain(await response.text(), url, addHistory);
        } catch {
            window.location.href = url.href;
        } finally {
            document.body.classList.remove("is-page-loading");
        }
    };

    document.addEventListener("click", (event) => {
        if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
            return;
        }

        const link = event.target.closest("a[href]");
        if (!link || link.target === "_blank" || link.hasAttribute("download") || link.dataset.fullLoad !== undefined) {
            return;
        }

        const url = new URL(link.href, window.location.href);
        if (!isInternalGet(url)) {
            return;
        }

        event.preventDefault();
        loadPage(url);
    });

    document.addEventListener("submit", (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || (form.method || "get").toLowerCase() !== "get" || form.dataset.fullLoad !== undefined) {
            return;
        }

        const url = new URL(form.action || window.location.href, window.location.href);
        const formData = new FormData(form);
        url.search = new URLSearchParams(formData).toString();

        if (!isInternalGet(url)) {
            return;
        }

        event.preventDefault();
        loadPage(url);
    });

    window.addEventListener("popstate", () => {
        loadPage(new URL(window.location.href), false);
    });
})();
