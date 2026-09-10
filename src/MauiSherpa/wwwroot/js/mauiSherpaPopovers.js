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
 * The panel is right-aligned with the trigger when there is room and clamped
 * into the viewport otherwise, so triggers near an edge stay fully visible.
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
    const panelRect = panel.getBoundingClientRect();
    const viewportWidth = document.documentElement.clientWidth;
    const viewportHeight = document.documentElement.clientHeight;

    const maxLeft = viewportWidth - POPOVER_VIEWPORT_MARGIN - panelRect.width;
    const left = Math.max(
        POPOVER_VIEWPORT_MARGIN,
        Math.min(triggerRect.right - panelRect.width, maxLeft));

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

    const onReposition = () => positionPopover(rootId);

    document.addEventListener("pointerdown", onPointerDown, true);
    document.addEventListener("keydown", onKeyDown, true);
    document.addEventListener("scroll", onReposition, true);
    window.addEventListener("resize", onReposition);
    popoverBindings.set(rootId, { onPointerDown, onKeyDown, onReposition });

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
