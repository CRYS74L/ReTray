// WPF UseLayoutRounding uses midpoint-to-even rounding, including fractional DPI.
export function desktopRound(value, scale = globalThis.devicePixelRatio || 1) {
  const pixels = value * scale,
    floor = Math.floor(pixels),
    fraction = pixels - floor;
  const rounded =
    Math.abs(fraction - 0.5) < 1e-8
      ? floor % 2 === 0
        ? floor
        : floor + 1
      : Math.round(pixels);
  return rounded / scale;
}

// Join the parent's actual lower rounded contour to the child's left edge.
export function meshLinkPath(parent, child, radius = 26.6667) {
  const r = Math.min(radius, parent.width / 2, parent.height / 2);
  const inset = Math.min(1, r);
  const x = parent.x + inset;
  const start =
    parent.y +
    parent.height -
    r +
    Math.sqrt(Math.max(0, r * r - (r - Math.min(r, inset)) ** 2)) -
    0.8;
  const end = child.y + child.height / 2;
  const target = child.x + 1;
  const bend = Math.max(0, Math.min(r, target - x, (end - start) / 2));
  const n = (v) => Number(v.toFixed(2));
  return `M${n(x)},${n(start)}V${n(end - bend)}A${n(bend)},${n(bend)} 0 0 0 ${n(x + bend)},${n(end)}H${n(target)}`;
}

export class DesktopPrecision {
  constructor(beforeScroll) {
    this.beforeScroll = beforeScroll;
    this.items = new Map();
    this.canvas = document.createElement("canvas");
    this.context = this.canvas.getContext("2d");
    for (const viewport of document.querySelectorAll(
      ".scroll-pane,.device-list",
    )) {
      const list = viewport.classList.contains("device-list");
      const owner = list
        ? viewport.parentElement
        : document.getElementById("detail-surface");
      const rail = document.createElement("div");
      rail.className = "desktop-scrollbar";
      rail.tabIndex = 0;
      rail.setAttribute("role", "scrollbar");
      rail.setAttribute("aria-orientation", "vertical");
      rail.setAttribute(
        "aria-label",
        list
          ? "设备列表滚动条"
          : `${viewport.getAttribute("aria-label") || "详情"}滚动条`,
      );
      rail.setAttribute("aria-controls", viewport.id);
      rail.innerHTML =
        '<div class="desktop-scroll-track"><div class="desktop-scroll-thumb"></div></div>';
      const top = document.createElement("div"),
        bottom = document.createElement("div");
      top.className = "viewport-fade viewport-fade-top";
      bottom.className = "viewport-fade viewport-fade-bottom";
      top.setAttribute("aria-hidden", "true");
      bottom.setAttribute("aria-hidden", "true");
      owner.append(top, bottom, rail);
      const item = {
        viewport,
        owner,
        list,
        rail,
        top,
        bottom,
        track: rail.firstElementChild,
        thumb: rail.firstElementChild.firstElementChild,
      };
      this.items.set(viewport, item);
      rail.addEventListener("pointerdown", (event) =>
        this.pointerDown(item, event),
      );
      rail.addEventListener("pointermove", (event) =>
        this.pointerMove(item, event),
      );
      rail.addEventListener("pointerup", (event) =>
        this.pointerUp(item, event),
      );
      rail.addEventListener("pointercancel", (event) =>
        this.pointerUp(item, event),
      );
      rail.addEventListener("keydown", (event) => {
        if (
          ![
            "ArrowUp",
            "ArrowDown",
            "PageUp",
            "PageDown",
            "Home",
            "End",
          ].includes(event.key)
        )
          return;
        event.preventDefault();
        beforeScroll(viewport);
        const step =
          event.key === "ArrowUp"
            ? -16
            : event.key === "ArrowDown"
              ? 16
              : event.key === "PageUp"
                ? -viewport.clientHeight
                : viewport.clientHeight;
        viewport.scrollTop =
          event.key === "Home"
            ? 0
            : event.key === "End"
              ? viewport.scrollHeight
              : viewport.scrollTop + step;
      });
    }
    document.fonts.ready.then(() => this.alignLabels());
  }
  update(viewport) {
    const item = this.items.get(viewport);
    if (!item) return;
    if (!viewport.getClientRects().length || viewport.hidden) {
      item.rail.hidden = true;
      item.top.hidden = true;
      item.bottom.hidden = true;
      return;
    }
    // Layout coordinates must not inherit an animated page's scale/translation.
    // Both viewport kinds are positioned directly inside their overlay owner.
    const rect = {
      top: viewport.offsetTop,
      left: viewport.offsetLeft,
      width: viewport.offsetWidth,
      height: viewport.offsetHeight,
    };
    rect.bottom = rect.top + rect.height;
    const dpr = devicePixelRatio || 1,
      slot = item.list ? desktopRound(10, dpr) : desktopRound(17, dpr);
    const overflow = viewport.scrollHeight > viewport.clientHeight + 1;
    viewport.style.setProperty("--scroll-reserve", (item.list ? 14 : 0) + "px");
    item.rail.hidden = !overflow;
    const topMargin = item.list ? 0 : 14,
      bottomMargin = item.list ? 0 : 14;
    item.rail.style.width = slot + "px";
    item.rail.style.right = (item.list ? -1 : 1) + "px";
    item.rail.style.top = rect.top + desktopRound(topMargin, dpr) + "px";
    item.rail.style.height =
      desktopRound(Math.max(0, rect.height - topMargin - bottomMargin), dpr) +
      "px";
    const max = Math.max(0, viewport.scrollHeight - viewport.clientHeight);
    item.rail.style.setProperty("--track-inset", desktopRound(2, dpr) + "px");
    const trackHeight = Math.max(
      0,
      item.rail.clientHeight - 2 * desktopRound(2, dpr),
    );
    const thumbHeight = Math.min(
      trackHeight,
      Math.max(
        desktopRound(28, dpr),
        (trackHeight * viewport.clientHeight) /
          Math.max(1, viewport.scrollHeight),
      ),
    );
    item.thumb.style.height = desktopRound(thumbHeight, dpr) + "px";
    item.thumb.style.top =
      desktopRound(
        max ? ((trackHeight - thumbHeight) * viewport.scrollTop) / max : 0,
        dpr,
      ) + "px";
    item.rail.setAttribute("aria-valuemin", "0");
    item.rail.setAttribute("aria-valuemax", String(Math.round(max)));
    item.rail.setAttribute(
      "aria-valuenow",
      String(Math.round(viewport.scrollTop)),
    );
    const remaining = Math.max(0, max - viewport.scrollTop);
    const topHeight = Math.min(36, viewport.scrollTop),
      bottomHeight = item.list ? 0 : Math.min(36, remaining);
    for (const fade of [item.top, item.bottom]) {
      fade.style.left = rect.left + "px";
      fade.style.width = Math.max(0, rect.width - (item.list ? 14 : 0)) + "px";
    }
    item.top.style.top = rect.top + "px";
    item.top.style.height = topHeight + "px";
    item.top.hidden = topHeight < 0.5;
    item.bottom.style.top = rect.bottom - bottomHeight + "px";
    item.bottom.style.height = bottomHeight + "px";
    item.bottom.hidden = bottomHeight < 0.5;
    item.thumb.style.setProperty("--thumb-thin", desktopRound(3, dpr) + "px");
    item.thumb.style.setProperty("--thumb-wide", desktopRound(5, dpr) + "px");
    item.thumb.style.setProperty("--thumb-inset", desktopRound(1, dpr) + "px");
    item.thumb.style.setProperty(
      "--thumb-right-thin",
      desktopRound(2, dpr) + "px",
    );
    item.thumb.style.setProperty(
      "--thumb-right-wide",
      desktopRound(1, dpr) + "px",
    );
  }
  pointerDown(item, event) {
    if (event.button !== 0) return;
    event.preventDefault();
    this.beforeScroll(item.viewport);
    const rect = item.thumb.getBoundingClientRect();
    if (event.clientY >= rect.top && event.clientY <= rect.bottom) {
      item.drag = { y: event.clientY, offset: item.viewport.scrollTop };
      item.rail.setPointerCapture(event.pointerId);
      item.thumb.classList.add("dragging");
    } else
      item.viewport.scrollTop +=
        (event.clientY < rect.top ? -1 : 1) * item.viewport.clientHeight;
  }
  pointerMove(item, event) {
    if (!item.drag) return;
    const travel = item.track.clientHeight - item.thumb.clientHeight,
      max = item.viewport.scrollHeight - item.viewport.clientHeight;
    item.viewport.scrollTop =
      item.drag.offset +
      ((event.clientY - item.drag.y) * max) / Math.max(1, travel);
  }
  pointerUp(item, event) {
    item.drag = null;
    item.thumb.classList.remove("dragging");
    if (item.rail.hasPointerCapture(event.pointerId))
      item.rail.releasePointerCapture(event.pointerId);
  }
  alignLabels() {
    const ctx = this.context;
    for (const field of document.querySelectorAll(".input-field")) {
      const label = field.querySelector(":scope >span"),
        target = field.querySelector("input");
      if (!label || !target || !field.getClientRects().length) continue;
      const labelStyle = getComputedStyle(label),
        targetStyle = getComputedStyle(target);
      ctx.font = labelStyle.font;
      const labelInk = -ctx.measureText(label.textContent)
        .actualBoundingBoxLeft;
      ctx.font = targetStyle.font;
      const value = target.value || target.placeholder,
        targetInk = -ctx.measureText(value).actualBoundingBoxLeft;
      const box = target.getBoundingClientRect(),
        caption = label.getBoundingClientRect();
      const padding =
        parseFloat(targetStyle.paddingLeft) +
        parseFloat(targetStyle.borderLeftWidth) +
        (target.value ? 0 : 2);
      const offset = target.id === "router-password" ? -2 : 0;
      const current = parseFloat(labelStyle.marginLeft) || 0;
      const delta =
        box.left + padding + targetInk + offset - (caption.left + labelInk);
      if (Math.abs(delta) > 0.025) {
        label.style.marginLeft = current + delta + "px";
        label.style.transform = "none";
      }
    }
  }
}
