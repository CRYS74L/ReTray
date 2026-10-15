// WPF CubicEase/QuinticEase evaluated directly, rather than approximated Beziers.
export const nativeEase = {
  cubicInOut: (t) => (t < 0.5 ? 4 * t ** 3 : 1 - (-2 * t + 2) ** 3 / 2),
  cubicOut: (t) => 1 - (1 - t) ** 3,
  quinticOut: (t) => 1 - (1 - t) ** 5,
};
export const pageTiming = Object.freeze({
  exit: 260,
  delay: 120,
  enter: 780,
  opacity: 720,
});
export const deviceTiming = Object.freeze({
  exit: 150,
  delay: 0,
  enter: 450,
  opacity: (450 * 720) / 780,
});

export function nativeFrames(from, to, easing, opacityRatio = 1) {
  const ease = nativeEase[easing];
  return Array.from({ length: 121 }, (_, i) => {
    const t = i / 120,
      position = ease(t),
      opacity = ease(Math.min(1, t / opacityRatio));
    const lerp = (key, p = position) =>
      (from[key] ?? 0) + ((to[key] ?? 0) - (from[key] ?? 0)) * p;
    return {
      offset: t,
      opacity: lerp("opacity", opacity),
      transform: `translate(${lerp("x")}px,${lerp("y")}px) scale(${lerp("scale")})`,
    };
  });
}

export function presentation(el) {
  const style = getComputedStyle(el),
    matrix = new DOMMatrixReadOnly(style.transform);
  return {
    opacity: +style.opacity,
    x: matrix.m41,
    y: matrix.m42,
    scale: matrix.a,
  };
}
