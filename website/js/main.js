// ─── MAUI Sherpa Website — main.js ───

(function () {
    'use strict';

    // ─── Scroll Reveal ───
    function initScrollReveal() {
        const reveals = document.querySelectorAll('.reveal');
        if (!reveals.length) return;

        const observer = new IntersectionObserver(
            (entries) => {
                entries.forEach((entry, i) => {
                    if (entry.isIntersecting) {
                        // Stagger children slightly
                        const delay = entry.target.dataset.revealDelay || 0;
                        setTimeout(() => {
                            entry.target.classList.add('reveal--visible');
                        }, delay);
                        observer.unobserve(entry.target);
                    }
                });
            },
            { threshold: 0.1, rootMargin: '0px 0px -40px 0px' }
        );

        reveals.forEach((el, i) => {
            // Auto-stagger items in grids
            const parent = el.parentElement;
            if (parent && (parent.classList.contains('features-grid') || parent.classList.contains('gallery-grid'))) {
                const siblings = Array.from(parent.querySelectorAll('.reveal'));
                const index = siblings.indexOf(el);
                el.dataset.revealDelay = index * 80;
            }
            observer.observe(el);
        });
    }

    // ─── Hero Stars ───
    function initHeroStars() {
        const container = document.getElementById('hero-stars');
        if (!container) return;

        const count = 60;
        for (let i = 0; i < count; i++) {
            const star = document.createElement('span');
            star.style.left = Math.random() * 100 + '%';
            star.style.top = Math.random() * 100 + '%';
            star.style.animationDelay = Math.random() * 3 + 's';
            star.style.animationDuration = (2 + Math.random() * 3) + 's';
            star.style.width = (1 + Math.random() * 2) + 'px';
            star.style.height = star.style.width;
            container.appendChild(star);
        }
    }

    // ─── Mobile Nav ───
    function initMobileNav() {
        const hamburger = document.getElementById('nav-hamburger');
        const links = document.getElementById('nav-links');
        if (!hamburger || !links) return;
        hamburger.setAttribute('aria-controls', links.id);
        hamburger.setAttribute('aria-expanded', 'false');

        function closeMenu() {
            links.classList.remove('nav__links--open');
            hamburger.setAttribute('aria-expanded', 'false');
            hamburger.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M3 12h18M3 6h18M3 18h18"/></svg>';
        }

        hamburger.addEventListener('click', () => {
            links.classList.toggle('nav__links--open');
            const isOpen = links.classList.contains('nav__links--open');
            hamburger.setAttribute('aria-expanded', String(isOpen));
            hamburger.innerHTML = isOpen
                ? '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 6L6 18M6 6l12 12"/></svg>'
                : '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 12h18M3 6h18M3 18h18"/></svg>';
        });

        // Close on link click
        links.querySelectorAll('a').forEach(a => {
            a.addEventListener('click', closeMenu);
        });
        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && links.classList.contains('nav__links--open')) {
                closeMenu();
                hamburger.focus();
            }
        });
    }

    // ─── Lightbox ───
    function initLightbox() {
        const lightbox = document.getElementById('lightbox');
        if (!lightbox) return;

        const lightboxImg = lightbox.querySelector('img');
        const closeBtn = lightbox.querySelector('.lightbox__close');
        let opener;
        let previousOverflow;

        lightbox.setAttribute('role', 'dialog');
        lightbox.setAttribute('aria-modal', 'true');
        lightbox.setAttribute('aria-label', 'Enlarged app screenshot');
        lightbox.hidden = true;

        document.querySelectorAll('[data-lightbox]').forEach(item => {
            function openLightbox() {
                const img = item.querySelector('img');
                if (!img || !img.getAttribute('src') || item.hidden) return;
                opener = item;
                previousOverflow = document.body.style.overflow;
                lightboxImg.src = img.src;
                lightboxImg.alt = img.alt;
                lightbox.hidden = false;
                lightbox.classList.add('lightbox--open');
                document.body.style.overflow = 'hidden';
                closeBtn.focus();
            }
            if (item.tagName !== 'BUTTON') {
                item.tabIndex = 0;
                item.setAttribute('role', 'button');
                item.setAttribute('aria-label', `Enlarge ${item.querySelector('img')?.alt || 'screenshot'}`);
                item.addEventListener('keydown', event => {
                    if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault();
                        openLightbox();
                    }
                });
            }
            item.addEventListener('click', openLightbox);
        });

        function closeLightbox() {
            if (lightbox.hidden) return;
            lightbox.hidden = true;
            lightbox.classList.remove('lightbox--open');
            document.body.style.overflow = previousOverflow;
            opener?.focus();
        }
        closeBtn.addEventListener('click', closeLightbox);
        lightbox.addEventListener('click', (e) => {
            if (e.target === lightbox) closeLightbox();
        });
        document.addEventListener('keydown', (e) => {
            if (lightbox.hidden) return;
            if (e.key === 'Escape') closeLightbox();
            if (e.key === 'Tab') {
                e.preventDefault();
                closeBtn.focus();
            }
        });
    }

    // Platform download anchors must reveal their panel before scrolling to it.
    function initTabs() {
        document.querySelectorAll('.tab-group').forEach(group => {
            const tabs = Array.from(group.querySelectorAll('.tab-group__tab'));
            const container = group.parentElement;
            const panels = container.querySelectorAll('.tab-panel');
            group.setAttribute('role', 'tablist');
            group.setAttribute('aria-label', 'Installation platform');

            function activate(tab, focus = false) {
                tabs.forEach(item => {
                    const selected = item === tab;
                    item.classList.toggle('tab-group__tab--active', selected);
                    item.setAttribute('aria-selected', String(selected));
                    item.tabIndex = selected ? 0 : -1;
                });
                panels.forEach(panel => panel.classList.toggle('tab-panel--active', panel.dataset.panel === tab.dataset.tab));
                if (focus) tab.focus();
            }
            tabs.forEach((tab, index) => {
                const panel = Array.from(panels).find(panel => panel.dataset.panel === tab.dataset.tab);
                tab.id ||= `install-tab-${tab.dataset.tab}`;
                tab.setAttribute('role', 'tab');
                if (panel) {
                    panel.id ||= `install-panel-${tab.dataset.tab}`;
                    panel.setAttribute('role', 'tabpanel');
                    panel.setAttribute('aria-labelledby', tab.id);
                    tab.setAttribute('aria-controls', panel.id);
                }
                tab.addEventListener('click', () => activate(tab));
                tab.addEventListener('keydown', event => {
                    let next;
                    if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
                    if (event.key === 'ArrowLeft') next = (index + tabs.length - 1) % tabs.length;
                    if (event.key === 'Home') next = 0;
                    if (event.key === 'End') next = tabs.length - 1;
                    if (next !== undefined) {
                        event.preventDefault();
                        activate(tabs[next], true);
                    }
                });
            });
            function activateHash() {
                const tab = tabs.find(tab => `#${tab.dataset.tab}` === location.hash);
                if (tab) activate(tab);
                return tab;
            }
            activate(tabs.find(tab => tab.classList.contains('tab-group__tab--active')) || tabs[0]);
            const linkedTab = activateHash();
            if (linkedTab) {
                const panel = Array.from(panels).find(panel => panel.dataset.panel === linkedTab.dataset.tab);
                panel?.scrollIntoView({ block: 'start' });
            }
            window.addEventListener('hashchange', () => {
                const tab = activateHash();
                if (tab) document.getElementById(tab.getAttribute('aria-controls'))?.scrollIntoView({ block: 'start' });
            });
        });
    }

    // ─── Code Copy Buttons ───
    function initCodeCopy() {
        document.querySelectorAll('.code-block__copy').forEach(btn => {
            btn.addEventListener('click', () => {
                const code = btn.closest('.code-block').querySelector('code');
                if (!code) return;
                navigator.clipboard.writeText(code.textContent).then(() => {
                    const orig = btn.textContent;
                    btn.textContent = 'Copied!';
                    setTimeout(() => { btn.textContent = orig; }, 1500);
                });
            });
        });
    }

    // ─── Init ───
    document.addEventListener('DOMContentLoaded', () => {
        initScrollReveal();
        initHeroStars();
        initMobileNav();
        initLightbox();
        initTabs();
        initCodeCopy();
    });
})();
