import { rate, clock, esc } from "./ui.js";

export function trafficScale(samples) {
  // Native CalculateScale ignores placeholders and samples with no directions.
  // The missing direction contributes only to axis bounds, never to a plotted point.
  const values = samples
    .filter((s) => !s.placeholder && (s.upload != null || s.download != null))
    .flatMap((s) => [s.upload ?? 0, s.download ?? 0]);
  const peak = values.length ? Math.max(...values) : 0;
  if (!peak) return { min: 0, max: 1024 };
  const minimum = Math.min(...values),
    span = peak - minimum;
  return span <= 0
    ? { min: 0, max: peak * 1.2 }
    : { min: Math.max(0, minimum - span * 0.1), max: peak + span * 0.1 };
}

function normalizeRange(range) {
  return [1, 5, 10].includes(range) ? range : 10;
}

function timestampAtOffset(time, source) {
  const offset =
    typeof source === "string" && source.match(/(Z|([+-])(\d{2}):(\d{2}))$/);
  if (!offset) return time;
  const minutes =
    offset[1] === "Z"
      ? 0
      : (Number(offset[3]) * 60 + Number(offset[4])) *
        (offset[2] === "-" ? -1 : 1);
  return (
    new Date(time + minutes * 60000).toISOString().slice(0, 19) + offset[1]
  );
}

// TrafficHistoryBuffer normalizes to whole seconds; GetVisibleSamples supplies
// range * 60 + 1 fixed slots ending at the latest received sample, not wall time.
export function trafficSlots(samples, range = 10) {
  const byTime = new Map();
  for (const sample of samples || []) {
    const timestamp = new Date(sample.at).getTime();
    if (!Number.isFinite(timestamp) || sample.placeholder) continue;
    const time = Math.floor(timestamp / 1000) * 1000;
    byTime.set(time, {
      ...sample,
      at: timestampAtOffset(time, sample.at),
      time,
      upload: Number.isFinite(sample.upload)
        ? Math.max(0, sample.upload)
        : null,
      download: Number.isFinite(sample.download)
        ? Math.max(0, sample.download)
        : null,
      placeholder: false,
    });
  }
  if (!byTime.size) return [];
  const end = Math.max(...byTime.keys()),
    count = normalizeRange(range) * 60 + 1;
  const endSource = byTime.get(end).at;
  const start = end - (count - 1) * 1000;
  return Array.from({ length: count }, (_, i) => {
    const time = start + i * 1000;
    return (
      byTime.get(time) || {
        at: timestampAtOffset(time, endSource),
        time,
        upload: null,
        download: null,
        placeholder: true,
      }
    );
  });
}

export function trafficHoverIndex(samples, x, left, right) {
  if (!samples.length || x < left || x > right) return null;
  const index = Math.round(
    Math.max(0, Math.min(1, (x - left) / Math.max(1, right - left))) *
      (samples.length - 1),
  );
  return samples[index].placeholder ? null : index;
}

export function trafficTimeAxis(samples, range, left, right) {
  if (!samples.length) return [];
  range = normalizeRange(range);
  const distance = range === 1 ? 35 : range === 5 ? 38 : 40;
  const targetLabels = Math.min(range === 10 ? 8 : 6, samples.length);
  const nominalStep = Math.max(
    1,
    Math.floor(samples.length / Math.max(1, targetLabels - 1)),
  );
  const pixelStep = Math.max(
    1,
    Math.ceil((samples.length * distance) / Math.max(1, right - left)),
  );
  const step = Math.max(nominalStep, pixelStep);
  const point = (index) => ({
    index,
    time: samples[index].time,
    at: samples[index].at ?? samples[index].time,
    x: left + (index / Math.max(1, samples.length - 1)) * (right - left),
  });
  const points = [point(0)];
  for (let i = step; i < samples.length - step; i += step)
    points.push(point(i));
  if (samples.length > 1 && right - points.at(-1).x >= distance)
    points.push(point(samples.length - 1));
  return points;
}

export function trafficSeries(samples, direction, x, y, bottom) {
  // Native GetRenderableSampleSegments skips unknown slots and joins the real
  // controls on both sides, without adding zero-valued intermediate controls.
  const points = samples
    .filter((p) => !p.placeholder && p[direction] != null)
    .map((p) => [+x(p.time).toFixed(2), +y(p[direction]).toFixed(2)]);
  const path = trafficCurve(points);
  const area = path
    ? path + `L${points.at(-1)[0]},${bottom}L${points[0][0]},${bottom}Z`
    : "";
  return { path, area, points };
}

export function trafficTooltipPosition(
  pointX,
  boxWidth,
  left,
  right,
  top,
  bottom,
  boxHeight,
) {
  let x = pointX + 12;
  if (x + boxWidth > right) x = pointX - boxWidth - 12;
  x = Math.max(left, Math.min(x, Math.max(left, right - boxWidth)));
  let y = top + 8;
  if (y + boxHeight > bottom) y = Math.max(top + 2, bottom - boxHeight - 2);
  return { x, y };
}

// Match TrafficChart.AppendClashVergeQuadraticCurve: each sample controls
// a quadratic curve ending midway between it and the following sample.
export function trafficCurve(points) {
  if (points.length < 2) return "";
  let result = `M${points[0].join(",")}`;
  for (let i = 1; i < points.length; i++) {
    const current = points[i],
      next = points[Math.min(i + 1, points.length - 1)];
    result += `Q${current.join(",")} ${(current[0] + next[0]) / 2},${(current[1] + next[1]) / 2}`;
  }
  return result;
}

// Stable native snapshots are redrawn directly on sampling; no slide animation.
export class TrafficChart {
  constructor(element) {
    this.el = element;
    this.samples = [];
    this.cursor = null;
    this.range = 10;
    element.innerHTML = `<svg viewBox="0 0 640 232" preserveAspectRatio="none" aria-hidden="true"><defs>${["download", "upload"].map((direction) => `<linearGradient id="${direction}-fill" x1="0" x2="0" y1="0" y2="1"><stop offset="0%" stop-opacity=".15"/><stop offset="100%" stop-opacity="0"/></linearGradient>`).join("")}</defs><g class="chart-grid"></g><path class="chart-area download" fill="url(#download-fill)"/><path class="chart-line download"/><path class="chart-area upload" fill="url(#upload-fill)"/><path class="chart-line upload"/><g class="chart-cursor" visibility="hidden"><path class="cursor-rule"/><circle class="download-point" r="4"/><circle class="upload-point" r="4"/></g></svg><div class="chart-empty">等待新的流量采样</div><output class="chart-tooltip" hidden></output>`;
    element.addEventListener("pointerdown", (e) => {
      if (e.pointerType === "mouse" && e.button !== 0) return;
      this.select(e);
      if (e.pointerType !== "mouse" && this.cursor != null) {
        this.touch = {
          id: e.pointerId,
          x: e.clientX,
          y: e.clientY,
          moved: false,
        };
        element.setPointerCapture(e.pointerId);
      }
    });
    element.addEventListener("pointermove", (e) => {
      if (
        this.touch?.id === e.pointerId &&
        Math.hypot(e.clientX - this.touch.x, e.clientY - this.touch.y) > 8
      )
        this.touch.moved = true;
      if (e.pointerType === "mouse" || e.buttons) this.select(e);
    });
    element.addEventListener("pointerup", (e) => {
      const moved = this.touch?.id === e.pointerId && this.touch.moved;
      this.releasePointer(e.pointerId);
      if (moved) this.clear();
      // A stationary touch retains its inspected sample until scroll/navigation.
    });
    element.addEventListener("lostpointercapture", (e) => {
      if (this.touch?.id === e.pointerId) this.clear();
    });
    element.addEventListener("pointerleave", (e) => {
      if (e.pointerType === "mouse") this.clear();
    });
    element.addEventListener("pointercancel", () => this.clear());
    element.addEventListener("blur", () => this.clear());
    element.addEventListener("keydown", (e) => {
      if (e.key === "Escape") {
        this.clear();
        return;
      }
      if (!["ArrowLeft", "ArrowRight"].includes(e.key)) return;
      e.preventDefault();
      const real = this.samples.filter((p) => !p.placeholder);
      const current =
        this.cursor == null
          ? real.length - 1
          : real.findIndex((p) => p.time === this.cursor);
      const i = Math.max(
        0,
        Math.min(real.length - 1, current + (e.key === "ArrowLeft" ? -1 : 1)),
      );
      if (real[i]) {
        this.cursor = real[i].time;
        this.drawCursor();
      }
    });
    this.resizeObserver = new ResizeObserver(() => {
      if (this.cached && this.el.clientWidth > 0) this.update(...this.cached);
    });
    this.resizeObserver.observe(element);
  }
  update(samples, now, range = 10, interval = 1) {
    this.cached = [samples, now, range, interval];
    this.width = this.el.clientWidth || 600;
    this.height = this.el.clientHeight || 176;
    this.left = 60;
    this.right = Math.max(this.left + 1, this.width - 24);
    this.top = 23;
    this.bottom = Math.max(this.top + 1, this.height - 32);
    this.el
      .querySelector("svg")
      .setAttribute("viewBox", `0 0 ${this.width} ${this.height}`);
    range = normalizeRange(range);
    if (range !== this.range) this.clear();
    this.range = range;
    this.samples = trafficSlots(samples, range);
    this.end = this.samples.at(-1)?.time || 0;
    this.start = this.samples[0]?.time || 0;
    const scale = trafficScale(this.samples);
    this.max = scale.max;
    this.min = scale.min;
    this.x = (t) =>
      this.left +
      ((t - this.start) / Math.max(1, this.end - this.start)) *
        (this.right - this.left);
    this.y = (v) =>
      this.bottom -
      Math.max(
        0,
        Math.min(
          1,
          (Math.max(this.min, v) - this.min) / Math.max(1, this.max - this.min),
        ),
      ) *
        (this.bottom - this.top);
    let grid = "";
    for (let i = 0; i <= 4; i++) {
      const y = this.top + (i * (this.bottom - this.top)) / 4;
      grid += `<path d="M${this.left} ${y}H${this.right}"/>`;
    }
    for (let i = 0; i <= 6; i++) {
      const x = this.left + (i * (this.right - this.left)) / 6;
      grid += `<path d="M${x} ${this.top}V${this.bottom}"/>`;
    }
    for (let i = 0; i < 3; i++) {
      const y = this.top + (i * (this.bottom - this.top)) / 2;
      grid += `<text x="${this.left - 7}" y="${y + 4}" text-anchor="end">${esc(rate(this.max - ((this.max - this.min) * i) / 2).replace("/s", ""))}</text>`;
    }
    const labels = trafficTimeAxis(this.samples, range, this.left, this.right);
    for (let i = 0; i < labels.length; i++) {
      const point = labels[i];
      const label =
        range === 1 ? clock(point.at).slice(3) : clock(point.at, false);
      grid += `<text class="time-label" x="${point.x}" y="${this.bottom + 18}" text-anchor="${i === 0 ? "start" : i === labels.length - 1 ? "end" : "middle"}">${label}</text>`;
    }
    this.el.querySelector(".chart-grid").innerHTML = grid;
    for (const direction of ["download", "upload"]) {
      const { path, area } = trafficSeries(
        this.samples,
        direction,
        this.x,
        this.y,
        this.bottom,
      );
      this.el.querySelector(".chart-line." + direction).setAttribute("d", path);
      this.el.querySelector(".chart-area." + direction).setAttribute("d", area);
    }
    this.el.querySelector(".chart-empty").hidden = this.samples.some(
      (p) => p.upload != null || p.download != null,
    );
    this.drawCursor();
  }
  select(e) {
    if (!this.samples.length) return;
    const rect = this.el.getBoundingClientRect();
    // Pointer coordinates are rendered pixels; the plot uses layout pixels.
    const localX =
      ((e.clientX - rect.left) * this.width) / Math.max(1, rect.width);
    const index = trafficHoverIndex(
      this.samples,
      localX,
      this.left,
      this.right,
    );
    if (index == null || this.width < 100 || this.height < 80) {
      this.clear();
      return;
    }
    this.cursor = this.samples[index].time;
    this.drawCursor();
  }
  clear() {
    this.releasePointer(this.touch?.id);
    this.cursor = null;
    this.drawCursor();
  }
  releasePointer(id) {
    if (id == null) return;
    if (this.touch?.id === id) this.touch = null;
    if (this.el.hasPointerCapture(id)) this.el.releasePointerCapture(id);
  }
  drawCursor() {
    const group = this.el.querySelector(".chart-cursor"),
      tip = this.el.querySelector("output");
    group
      .querySelectorAll("circle")
      .forEach((circle) => circle.setAttribute("visibility", "hidden"));
    const point = this.samples.find((p) => p.time === this.cursor);
    if (!point || point.placeholder) {
      group.setAttribute("visibility", "hidden");
      tip.hidden = true;
      return;
    }
    group.setAttribute("visibility", "visible");
    const x = this.x(point.time);
    group
      .querySelector("path")
      .setAttribute("d", `M${x},${this.top}V${this.bottom}`);
    for (const direction of ["download", "upload"]) {
      const circle = group.querySelector("." + direction + "-point");
      circle.setAttribute("r", "3.5");
      circle.setAttribute("stroke", "var(--tool-tip)");
      circle.setAttribute("stroke-width", "3");
      circle.setAttribute("paint-order", "stroke fill");
      circle.setAttribute(
        "visibility",
        point[direction] == null ? "hidden" : "visible",
      );
      circle.setAttribute("cx", x);
      circle.setAttribute("cy", this.y(point[direction] || 0));
    }
    tip.hidden = false;
    const markup = `<b>${clock(point.at)}</b><span class="up">↑ ${rate(point.upload)}</span><span class="down">↓ ${rate(point.download)}</span>`;
    if (tip.innerHTML !== markup) tip.innerHTML = markup;
    tip.style.maxWidth = Math.max(1, this.right - this.left) + "px";
    tip.style.overflow = "hidden";
    const position = trafficTooltipPosition(
      x,
      tip.offsetWidth,
      this.left,
      this.right,
      this.top,
      this.bottom,
      tip.offsetHeight,
    );
    tip.style.left = position.x + "px";
    tip.style.top = position.y + "px";
  }
}
