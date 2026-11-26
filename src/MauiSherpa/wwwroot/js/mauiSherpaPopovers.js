const popoverBindings = new Map();
const POPOVER_VIEWPORT_MARGIN = 8;

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

/**
 * Pins an open popover panel to its trigger using viewport coordinates.
 * The panel is right-aligned by default. Panels can opt into start alignment
 * and trigger-width matching with data attributes, and are always clamped
 * into the viewport so triggers near an edge stay fully visible.
 * Fixed positioning also escapes the `overflow-x: hidden` on the page content.
 */
export function positionPopover(rootId) {
    const root = document.getElementById(rootId);
    const panel = root?.querySelector("[data-popover-panel]");
    const trigger = root?.querySelector("[data-popover-trigger]");
    if (!panel || !trigger) {
        return;
    }

    panel.style.position = "fixed";
    panel.style.inset = "auto";

    const triggerRect = trigger.getBoundingClientRect();
    const viewportWidth = document.documentElement.clientWidth;
    const viewportHeight = document.documentElement.clientHeight;
    const availableWidth = Math.max(0, viewportWidth - (POPOVER_VIEWPORT_MARGIN * 2));
    const availableHeight = Math.max(0, viewportHeight - (POPOVER_VIEWPORT_MARGIN * 2));
    const matchWidth = panel.dataset.popoverMatchWidth === "true";
    const alignStart = panel.dataset.popoverAlign !== "end";

    panel.style.boxSizing = "border-box";
    panel.style.maxWidth = `${availableWidth}px`;
    panel.style.maxHeight = `${availableHeight}px`;
    panel.style.minWidth = matchWidth
        ? `${Math.min(triggerRect.width, availableWidth)}px`
        : "";
    panel.style.overflowY = panel.scrollHeight > availableHeight ? "auto" : "";

    const panelRect = panel.getBoundingClientRect();

    const maxLeft = viewportWidth - POPOVER_VIEWPORT_MARGIN - panelRect.width;
    const desiredLeft = alignStart
        ? triggerRect.left
        : triggerRect.right - panelRect.width;
    const left = Math.max(
        POPOVER_VIEWPORT_MARGIN,
        Math.min(desiredLeft, maxLeft));

    let top = triggerRect.bottom + POPOVER_VIEWPORT_MARGIN;
    if (top + panelRect.height > viewportHeight - POPOVER_VIEWPORT_MARGIN) {
        const above = triggerRect.top - POPOVER_VIEWPORT_MARGIN - panelRect.height;
        top = above >= POPOVER_VIEWPORT_MARGIN
            ? above
            : Math.max(
                POPOVER_VIEWPORT_MARGIN,
                viewportHeight - POPOVER_VIEWPORT_MARGIN - panelRect.height);
    }

    panel.style.left = `${Math.round(left)}px`;
    panel.style.top = `${Math.round(top)}px`;
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

    const onNavigationKeyDown = event => {
        if (root.dataset.open !== "true") {
            return;
        }

        if (["ArrowDown", "ArrowUp", "Home", "End", "Enter"].includes(event.key)) {
            event.preventDefault();
        }
    };

    const onReposition = () => positionPopover(rootId);

    root.addEventListener("keydown", onNavigationKeyDown);
    document.addEventListener("pointerdown", onPointerDown, true);
    document.addEventListener("keydown", onKeyDown, true);
    document.addEventListener("scroll", onReposition, true);
    window.addEventListener("resize", onReposition);
    popoverBindings.set(rootId, {
        root,
        onPointerDown,
        onKeyDown,
        onNavigationKeyDown,
        onReposition
    });

    positionPopover(rootId);

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

    binding.root.removeEventListener("keydown", binding.onNavigationKeyDown);
    document.removeEventListener("pointerdown", binding.onPointerDown, true);
    document.removeEventListener("keydown", binding.onKeyDown, true);
    document.removeEventListener("scroll", binding.onReposition, true);
    window.removeEventListener("resize", binding.onReposition);
    popoverBindings.delete(rootId);
}

export function focusTrigger(rootId) {
    document.getElementById(rootId)
        ?.querySelector("[data-popover-trigger]")
        ?.focus();
}
