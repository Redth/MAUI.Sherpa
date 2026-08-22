const popoverBindings = new Map();

export function ensureStylesheet(href) {
    const absoluteHref = new URL(href, document.baseURI).href;
    const existing = Array.from(document.querySelectorAll("link[rel='stylesheet']"))
        .some(link => link.href === absoluteHref);

    if (existing) {
        return;
    }

    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = href;
    document.head.appendChild(link);
}

export function ensureStylesheets(...hrefs) {
    hrefs.flat().forEach(ensureStylesheet);
}

export function bindPopover(rootId, dotNetReference) {
    unbindPopover(rootId);

    const root = document.getElementById(rootId);
    if (!root) {
        return;
    }

    const close = restoreFocus => {
        if (root.dataset.open === "true") {
            void dotNetReference
                .invokeMethodAsync("CloseFromJavaScript", restoreFocus)
                .catch(() => {});
        }
    };

    const onPointerDown = event => {
        if (!root.contains(event.target)) {
            close(false);
        }
    };

    const onKeyDown = event => {
        if (event.key !== "Escape" || root.dataset.open !== "true") {
            return;
        }

        event.preventDefault();
        event.stopPropagation();
        close(true);
    };

    document.addEventListener("pointerdown", onPointerDown, true);
    document.addEventListener("keydown", onKeyDown, true);
    popoverBindings.set(rootId, { onPointerDown, onKeyDown });

    queueMicrotask(() => {
        const initialFocus = root.querySelector("[data-popover-initial-focus]");
        const closeButton = root.querySelector("[aria-label^='Close']");
        (initialFocus ?? closeButton)?.focus();
    });
}

export function unbindPopover(rootId) {
    const binding = popoverBindings.get(rootId);
    if (!binding) {
        return;
    }

    document.removeEventListener("pointerdown", binding.onPointerDown, true);
    document.removeEventListener("keydown", binding.onKeyDown, true);
    popoverBindings.delete(rootId);
}

export function focusTrigger(rootId) {
    document.getElementById(rootId)
        ?.querySelector("[data-popover-trigger]")
        ?.focus();
}
