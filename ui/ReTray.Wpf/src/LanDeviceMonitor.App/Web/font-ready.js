// Runs after the stylesheet and before the body can paint. Keep the desktop
// font, but never expose the system-font -> bundled-font swap on cold phones.
(() => {
  const root = document.documentElement;
  const fonts = document.fonts;
  if (!fonts?.load) {
    root.dataset.fontState = "fallback";
    return;
  }

  root.dataset.fontState = "loading";
  const finish = (state) => {
    if (root.dataset.fontState !== "loading") return;
    clearTimeout(deadline);
    root.dataset.fontState = state;
  };
  // A stalled request must not leave the UI blank or swap in a late font.
  const deadline = setTimeout(() => finish("fallback"), 5000);

  try {
    const sample = "ReTray 0123456789 Ag";
    Promise.all([
      fonts.load('400 14px "App Text"', sample),
      fonts.load('600 18px "App Text"', sample),
      fonts.load('400 18px "App Icons"', "\uE713"),
    ])
      .then((matches) => {
        if (matches.some((faces) => !faces.length)) {
          finish("fallback");
          return;
        }
        return fonts.ready.then(() => {
          // app.js also measures name spacing on fonts.ready. Let that layout
          // frame finish before revealing the final glyphs and their positions.
          requestAnimationFrame(() =>
            requestAnimationFrame(() => finish("ready")),
          );
        });
      })
      .catch(() => finish("fallback"));
  } catch {
    finish("fallback");
  }
})();
