(() => {
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const root = document.documentElement;
  const header = document.querySelector('[data-header]');
  const motionSections = [...document.querySelectorAll('[data-motion-section]')];
  const headerLinks = [...document.querySelectorAll('.site-header nav a[href^="#"]')];
  const chapterLinks = [...document.querySelectorAll('[data-chapter-link]')];
  const wayfindingLinks = [...chapterLinks, ...headerLinks];
  const clamp = (minimum, value, maximum) => Math.min(maximum, Math.max(minimum, value));
  const documentTop = (element) => {
    let top = 0;
    let node = element;
    while (node) {
      top += node.offsetTop || 0;
      node = node.offsetParent;
    }
    return top;
  };

  let motionMetrics = [];
  let smoothScroll = window.scrollY;
  let targetScroll = window.scrollY;
  let previousTarget = window.scrollY;
  let animationFrame = 0;

  const applyScrollPresentation = (element, progress) => {
    const inverse = 1 - progress;

    if (element.id === 'top') {
      const copy = element.querySelector('.hero-copy');
      const stage = element.querySelector('.hero-stage');
      const copyOpacity = clamp(0.55, 1 - progress * 0.58, 1);
      if (copy) {
        copy.style.opacity = String(copyOpacity);
        copy.style.transform = reducedMotion ? 'none' : `translate3d(0, ${(-progress * 28).toFixed(2)}px, 0)`;
      }
      if (stage) {
        stage.style.transform = reducedMotion
          ? 'none'
          : `translate3d(0, ${(inverse * 20).toFixed(2)}px, 0) scale(${(0.97 + progress * 0.03).toFixed(4)})`;
      }
      return;
    }

    if (element.classList.contains('layers-section')) {
      const figure = element.querySelector('.layer-figure .shot-button');
      if (figure) {
        figure.style.clipPath = `inset(0 0 ${(inverse * 6).toFixed(2)}% 0 round 4px)`;
        figure.style.transform = reducedMotion ? 'none' : `translate3d(${(inverse * 20).toFixed(2)}px, 0, 0)`;
      }
      return;
    }

    if (element.classList.contains('action-ledger')) {
      element.querySelectorAll('.action-index > article').forEach((item, index) => {
        item.style.opacity = '1';
        item.style.transform = reducedMotion ? 'none' : `translateY(${(inverse * (18 + index * 3)).toFixed(2)}px)`;
      });
      return;
    }

    if (element.classList.contains('deck-section')) {
      const editor = element.querySelector('.editor-figure');
      if (editor) editor.style.transform = reducedMotion ? 'none' : `translateX(${(inverse * 30).toFixed(2)}px)`;
      return;
    }

    if (element.classList.contains('deck-runway')) {
      const strip = element.querySelector('.deck-strip .media-spot');
      if (strip) {
        if (reducedMotion) {
          strip.style.transform = 'none';
          strip.style.filter = `brightness(${(0.92 + progress * 0.08).toFixed(3)})`;
        } else if (window.innerWidth <= 700) {
          strip.style.transform = `translate3d(${(-progress * 67).toFixed(2)}%, 0, 0)`;
        } else {
          strip.style.transform = `translate3d(${((0.5 - progress) * 4).toFixed(2)}vw, 0, 0) scale(${(0.98 + progress * 0.02).toFixed(4)})`;
        }
      }
      return;
    }

    if (element.classList.contains('workflow-section')) {
      const macro = element.querySelector('.workflow-macro');
      const gesture = element.querySelector('.workflow-gesture');
      if (macro) macro.style.transform = 'none';
      if (gesture) {
        gesture.style.transform = reducedMotion ? 'none' : `translateY(${(inverse * 30).toFixed(2)}px)`;
        gesture.style.opacity = '1';
      }
      return;
    }

    if (element.classList.contains('principles')) {
      element.querySelectorAll('.principle-list > div').forEach((item, index) => {
        item.style.opacity = '1';
        item.style.transform = reducedMotion ? 'none' : `translateX(${(inverse * (20 + index * 3)).toFixed(2)}px)`;
      });
      return;
    }

    if (element.classList.contains('download-section')) return;
  };

  const measureMotion = () => {
    motionMetrics = motionSections.map((element) => ({
      element,
      top: documentTop(element),
      height: element.offsetHeight
    }));
  };

  const updateWayfinding = () => {
    const probe = targetScroll + window.innerHeight * 0.42;
    let activeId = 'top';

    wayfindingLinks.forEach((link) => {
      const id = link.getAttribute('href')?.slice(1);
      const section = id ? document.getElementById(id) : null;
      if (section && documentTop(section) <= probe) activeId = id;
    });

    chapterLinks.forEach((link) => {
      const active = link.getAttribute('href') === '#' + activeId;
      link.classList.toggle('is-active', active);
      if (active) link.setAttribute('aria-current', 'location');
      else link.removeAttribute('aria-current');
    });

    headerLinks.forEach((link) => {
      link.classList.toggle('is-active', link.getAttribute('href') === '#' + activeId);
    });
  };

  const renderMotion = () => {
    targetScroll = window.scrollY;
    const distance = root.scrollHeight - window.innerHeight;
    const pageProgress = distance > 0 ? targetScroll / distance : 0;
    const velocity = clamp(-1, (targetScroll - previousTarget) / 90, 1);
    previousTarget = targetScroll;

    smoothScroll = reducedMotion
      ? targetScroll
      : smoothScroll + (targetScroll - smoothScroll) * 0.16;

    root.style.setProperty('--scroll-progress', (pageProgress * 100).toFixed(3) + '%');
    root.style.setProperty('--scroll-velocity', velocity.toFixed(3));
    header?.classList.toggle('is-scrolled', targetScroll > 16);
    updateWayfinding();

    const viewportHeight = window.innerHeight;
    motionMetrics.forEach(({ element, top, height }) => {
      let progress;
      if (element.id === 'top') {
        progress = clamp(0, smoothScroll / Math.max(1, height - viewportHeight * 0.28), 1);
      } else {
        const start = top - viewportHeight * 0.78;
        const end = top + height - viewportHeight * 0.3;
        progress = clamp(0, (smoothScroll - start) / Math.max(1, end - start), 1);
      }
      element.style.setProperty('--motion-progress', progress.toFixed(4));
      applyScrollPresentation(element, progress);
    });

    if (!reducedMotion && Math.abs(targetScroll - smoothScroll) > 0.12) {
      animationFrame = window.requestAnimationFrame(renderMotion);
    } else {
      smoothScroll = targetScroll;
      animationFrame = 0;
    }
  };

  const requestRender = () => {
    if (!animationFrame) animationFrame = window.requestAnimationFrame(renderMotion);
  };

  const refreshLocalizedMotion = () => {
    measureMotion();
    requestRender();
  };

  refreshLocalizedMotion();
  renderMotion();
  document.addEventListener('relyr:languagechange', refreshLocalizedMotion);
  window.addEventListener('scroll', requestRender, { passive: true });
  window.addEventListener('resize', () => {
    measureMotion();
    requestRender();
  }, { passive: true });
  window.addEventListener('load', () => {
    measureMotion();
    requestRender();
  }, { once: true });

  const revealItems = document.querySelectorAll('.reveal');
  if (reducedMotion || !('IntersectionObserver' in window)) {
    revealItems.forEach((item) => item.classList.add('is-visible'));
  } else {
    const revealObserver = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return;
        entry.target.classList.add('is-visible');
        revealObserver.unobserve(entry.target);
      });
    }, { threshold: 0.08, rootMargin: '0px 0px -8% 0px' });
    revealItems.forEach((item) => revealObserver.observe(item));
  }

  const productVideoFrames = [...document.querySelectorAll('[data-video-frame]')];

  const syncVideoFrame = (frame) => {
    const video = frame.querySelector('[data-product-video]');
    const progress = frame.querySelector('.video-progress span');
    if (!video) return;

    const playing = !video.paused && !video.ended;
    const percentage = Number.isFinite(video.duration) && video.duration > 0
      ? `${Math.min(100, Math.max(0, video.currentTime / video.duration * 100)).toFixed(2)}%`
      : '0%';

    frame.classList.toggle('is-playing', playing);
    if (progress) progress.style.width = percentage;
  };

  const playProductVideo = async (frame) => {
    const video = frame.querySelector('[data-product-video]');
    if (!video) return;
    try {
      await video.play();
    } catch {
      // Muted inline playback can still be blocked by a browser policy.
    }
    syncVideoFrame(frame);
  };

  const pauseProductVideo = (frame) => {
    const video = frame.querySelector('[data-product-video]');
    video?.pause();
    syncVideoFrame(frame);
  };

  productVideoFrames.forEach((frame) => {
    const video = frame.querySelector('[data-product-video]');
    if (!video) return;

    ['loadedmetadata', 'timeupdate', 'play', 'pause', 'ended'].forEach((eventName) => {
      video.addEventListener(eventName, () => syncVideoFrame(frame));
    });

    pauseProductVideo(frame);
  });

  if ('IntersectionObserver' in window) {
    const videoObserver = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        const frame = entry.target;
        const inView = entry.isIntersecting && entry.intersectionRatio >= 0.28;
        frame.dataset.inView = String(inView);
        if (!inView) pauseProductVideo(frame);
        else if (document.visibilityState === 'visible') playProductVideo(frame);
      });
    }, { threshold: [0, 0.28, 0.6] });
    productVideoFrames.forEach((frame) => videoObserver.observe(frame));
  }

  document.addEventListener('visibilitychange', () => {
    productVideoFrames.forEach((frame) => {
      if (document.visibilityState !== 'visible') {
        pauseProductVideo(frame);
      } else if (frame.dataset.inView === 'true') {
        playProductVideo(frame);
      }
    });
  });

  document.addEventListener('relyr:languagechange', () => {
    productVideoFrames.forEach(syncVideoFrame);
  });

  const dialog = document.querySelector('[data-shot-dialog]');
  const dialogImage = dialog?.querySelector('[data-shot-image]');
  const dialogCaption = dialog?.querySelector('[data-shot-caption]');
  const closeButton = dialog?.querySelector('[data-shot-close]');

  if (dialog && dialogImage && dialogCaption) {
    document.querySelectorAll('[data-shot]').forEach((button) => {
      button.addEventListener('click', () => {
        const preview = button.querySelector('img');
        dialogImage.src = button.dataset.shot || preview?.src || '';
        dialogImage.alt = preview?.alt || button.dataset.caption || '';
        dialogCaption.textContent = button.dataset.caption || preview?.alt || 'RELYR product screen';
        productVideoFrames.forEach(pauseProductVideo);
        dialog.showModal();
      });
    });

    closeButton?.addEventListener('click', () => dialog.close());
    dialog.addEventListener('click', (event) => {
      if (event.target === dialog) dialog.close();
    });
    dialog.addEventListener('close', () => {
      dialogImage.removeAttribute('src');
      dialogImage.alt = '';
      productVideoFrames.forEach((frame) => {
        if (frame.dataset.inView === 'true') playProductVideo(frame);
      });
    });
  }

  const updateLatestRelease = async () => {
    try {
      const response = await fetch('https://api.github.com/repos/zitan-source/RELYR/releases/latest', {
        headers: { Accept: 'application/vnd.github+json' }
      });
      if (!response.ok) return;

      const release = await response.json();
      const tag = typeof release.tag_name === 'string' ? release.tag_name : '';
      const version = tag.replace(/^v/i, '');
      const assets = Array.isArray(release.assets) ? release.assets : [];
      const setup = assets.find((asset) => /^RELYR-Setup-[\d.]+\.exe$/i.test(asset.name));
      const checksum = assets.find((asset) => /^RELYR-Setup-[\d.]+\.exe\.sha256$/i.test(asset.name));

      if (tag) {
        document.querySelectorAll('[data-release-version]').forEach((node) => {
          node.textContent = tag.startsWith('v') ? tag : 'v' + tag;
        });
      }
      if (setup?.browser_download_url) {
        document.querySelectorAll('[data-download-link]').forEach((link) => {
          link.href = setup.browser_download_url;
          if (version) link.setAttribute('download', 'RELYR-Setup-' + version + '.exe');
        });
      }
      if (checksum?.browser_download_url) {
        document.querySelectorAll('[data-checksum-link]').forEach((link) => {
          link.href = checksum.browser_download_url;
        });
      }
    } catch {
      // The stable HTML fallback remains usable when GitHub is unavailable.
    }
  };

  updateLatestRelease();
})();
