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

        hamburger.addEventListener('click', () => {
            links.classList.toggle('nav__links--open');
            const isOpen = links.classList.contains('nav__links--open');
            hamburger.innerHTML = isOpen
                ? '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 6L6 18M6 6l12 12"/></svg>'
                : '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 12h18M3 6h18M3 18h18"/></svg>';
        });

        // Close on link click
        links.querySelectorAll('a').forEach(a => {
            a.addEventListener('click', () => {
                links.classList.remove('nav__links--open');
                hamburger.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 12h18M3 6h18M3 18h18"/></svg>';
            });
        });
    }

    // ─── Lightbox ───
    function initLightbox() {
        const lightbox = document.getElementById('lightbox');
        if (!lightbox) return;

        const lightboxImg = lightbox.querySelector('img');
        const closeBtn = lightbox.querySelector('.lightbox__close');

        document.querySelectorAll('[data-lightbox]').forEach(item => {
            item.addEventListener('click', () => {
                const img = item.querySelector('.window-chrome__body img');
                if (!img) return;
                lightboxImg.src = img.src;
                lightboxImg.alt = img.alt;
                lightbox.classList.add('lightbox--open');
                document.body.style.overflow = 'hidden';
            });
        });

        function closeLightbox() {
            lightbox.classList.remove('lightbox--open');
            document.body.style.overflow = '';
        }

        closeBtn.addEventListener('click', closeLightbox);
        lightbox.addEventListener('click', (e) => {
            if (e.target === lightbox) closeLightbox();
        });
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') closeLightbox();
        });
    }

    // ─── Smooth Scroll for Anchor Links ───
    function initSmoothScroll() {
        document.querySelectorAll('a[href^="#"]').forEach(anchor => {
            anchor.addEventListener('click', function (e) {
                const href = this.getAttribute('href');
                if (href === '#') return;
                const target = document.querySelector(href);
                if (!target) return;
                e.preventDefault();
                target.scrollIntoView({ behavior: 'smooth', block: 'start' });
            });
        });
    }

    // ─── Tab Groups ───
    function initTabs() {
        document.querySelectorAll('.tab-group').forEach(group => {
            const tabs = group.querySelectorAll('.tab-group__tab');
            const container = group.parentElement;
            const panels = container.querySelectorAll('.tab-panel');

            tabs.forEach(tab => {
                tab.addEventListener('click', () => {
                    const target = tab.dataset.tab;

                    tabs.forEach(t => t.classList.remove('tab-group__tab--active'));
                    panels.forEach(p => p.classList.remove('tab-panel--active'));

                    tab.classList.add('tab-group__tab--active');
                    const panel = container.querySelector(`[data-panel="${target}"]`);
                    if (panel) panel.classList.add('tab-panel--active');
                });
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
        initSmoothScroll();
        initTabs();
        initCodeCopy();
    });
})();
