export function shouldCompleteReturn(distance, velocity, width) {
  return (
    distance > 8 &&
    velocity >= -0.15 &&
    (distance >= width * 0.35 ||
      distance + Math.max(0, velocity) * 180 >= width * 0.5)
  );
}

export function returnSpringFrames(from, to, velocity, width) {
  const omega = 28,
    duration = 0.4,
    displacement = from - to;
  return Array.from({ length: 61 }, (_, index) => {
    const time = (duration * index) / 60;
    const value =
      index === 60
        ? to
        : to +
          (displacement + (velocity * 1000 + omega * displacement) * time) *
            Math.exp(-omega * time);
    return {
      offset: index / 60,
      transform: `translate3d(${Math.max(0, Math.min(width, value))}px,0,0)`,
    };
  });
}
