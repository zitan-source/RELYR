(() => {
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const header = document.querySelector('.site-header');
  const root = document.documentElement;

  const updateScroll = () => {
    const distance = document.documentElement.scrollHeight - window.innerHeight;
    const progress = distance > 0 ? (window.scrollY / distance) * 100 : 0;
    root.style.setProperty('--scroll-progress', `${Math.min(100, Math.max(0, progress))}%`);
    header?.classList.toggle('scrolled', window.scrollY > 20);
  };
  updateScroll();
  window.addEventListener('scroll', updateScroll, { passive: true });

  const items = document.querySelectorAll('.reveal');
  if (reducedMotion || !('IntersectionObserver' in window)) {
    items.forEach((item) => item.classList.add('visible'));
  } else {
    const observer = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;
        entry.target.classList.add('visible');
        observer.unobserve(entry.target);
      });
    }, { threshold: 0.12, rootMargin: '0px 0px -36px' });
    items.forEach((item) => observer.observe(item));
  }

  const tilt = document.querySelector('[data-tilt]');
  if (tilt && !reducedMotion && window.matchMedia('(pointer: fine)').matches) {
    tilt.addEventListener('pointermove', (event) => {
      const bounds = tilt.getBoundingClientRect();
      const x = (event.clientX - bounds.left) / bounds.width - 0.5;
      const y = (event.clientY - bounds.top) / bounds.height - 0.5;
      tilt.style.setProperty('--tilt-x', `${x * 3 - 2}deg`);
      tilt.style.setProperty('--tilt-y', `${y * -2 + 1}deg`);
    });
    tilt.addEventListener('pointerleave', () => {
      tilt.style.removeProperty('--tilt-x');
      tilt.style.removeProperty('--tilt-y');
    });
  }
})();
