import { deviceIconNames, deviceIconPaths } from "./device-icons.js";
export const $ = (id) => document.getElementById(id);
export const esc = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const paths = {
  sidebar:
    '<path fill="currentColor" stroke="none" fill-rule="evenodd" d="M5.5 2.9H14.5C16.52 2.9 18.16 4.52 18.16 6.54V13.46C18.16 15.48 16.52 17.12 14.5 17.12H5.5C3.48 17.12 1.84 15.48 1.84 13.46V6.54C1.84 4.52 3.48 2.9 5.5 2.9ZM5.5 3.98C4.08 3.98 2.92 5.13 2.92 6.54V13.46C2.92 14.88 4.08 16.04 5.5 16.04H6.96V3.98ZM8.04 3.98V16.04H14.5C15.92 16.04 17.08 14.88 17.08 13.46V6.54C17.08 5.13 15.92 3.98 14.5 3.98Z"/>',
  back: '<path d="M15 5L8 12L15 19" vector-effect="non-scaling-stroke"/>',
  "chevron-down": '<path d="M5 9L12 16L19 9"/>',
  down: '<path transform="rotate(180 12 12)" d="M12 19.5V4.5M4.7 11.8L12 4.5L19.3 11.8"/>',
  up: '<path d="M12 19.5V4.5M4.7 11.8L12 4.5L19.3 11.8"/>',
  "total-down":
    '<circle cx="14" cy="14" r="10.2"/><path d="M14 8.6V19.4M8.8 14.2L14 19.4L19.2 14.2"/>',
  "total-up":
    '<circle cx="14" cy="14" r="10.2"/><path d="M14 19.4V8.6M8.8 13.8L14 8.6L19.2 13.8"/>',
};
const symbols = { settings: "\uE713", trash: "\uE74D", restart: "\uE777" };
export function icon(name) {
  if (symbols[name])
    return `<span class="symbol-icon" aria-hidden="true">${symbols[name]}</span>`;
  if (name === "router") return deviceIcon("路由器");
  const box =
    name === "sidebar"
      ? "0 0 20 20"
      : name === "back"
        ? "7 4 9 16"
        : name.startsWith("total-")
          ? "0 0 28 28"
          : "0 0 24 24";
  return `<svg viewBox="${box}" fill="none" stroke="currentColor" stroke-width="${/^(total-|down$|up$)/.test(name) ? 1.6 : 1.7}" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths[name] || paths["chevron-down"]}</svg>`;
}
export function deviceIcon(type) {
  if (type === "空气检测仪") type = "监测设备";
  return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${deviceIconPaths[deviceIconNames[type] || "cpu"]}</svg>`;
}
export function hydrateIcons(root = document) {
  root
    .querySelectorAll("[data-icon]")
    .forEach((el) => (el.innerHTML = icon(el.dataset.icon)));
}
export function text(id, value) {
  const el = $(id),
    s = String(value ?? "—");
  if (el && el.textContent !== s) el.textContent = s;
}
export function deviceNameParts(value) {
  const segments = [
    ...new Intl.Segmenter(undefined, { granularity: "grapheme" }).segment(
      String(value ?? ""),
    ),
  ].map((s) => s.segment);
  const kind = (s) =>
    /^\p{Script=Han}/u.test(s)
      ? 1
      : /^[A-Za-z\u00c0-\u024f]/u.test(s) && /^\p{L}/u.test(s)
        ? 2
        : /^\p{Nd}/u.test(s)
          ? 3
          : /^[\p{P}\p{S}]/u.test(s)
            ? 4
            : 0;
  const boundary = (a, b) =>
    (a === 1 && [2, 3, 4].includes(b)) ||
    (b === 1 && [2, 3, 4].includes(a)) ||
    (a === 2 && b === 4) ||
    (a === 4 && b === 2);
  const result = [];
  let content = "";
  for (let i = 0; i < segments.length; i++) {
    content += segments[i];
    let next = i + 1;
    while (next < segments.length && /^\s+$/u.test(segments[next])) next++;
    if (
      next < segments.length &&
      boundary(kind(segments[i]), kind(segments[next]))
    ) {
      result.push({ text: content, gap: false }, { text: " ", gap: true });
      content = "";
      i = next - 1;
    }
  }
  if (content) result.push({ text: content, gap: false });
  return result;
}
export function deviceNameMarkup(value) {
  return deviceNameParts(value)
    .map((p) => (p.gap ? '<span class="name-gap"> </span>' : esc(p.text)))
    .join("");
}
export function renderDeviceName(el, value) {
  if (el.dataset.name === String(value ?? "")) return;
  el.dataset.name = String(value ?? "");
  el.setAttribute("aria-label", el.dataset.name);
  el.innerHTML = deviceNameMarkup(value);
}
// Portrait pages omit status; the remaining complete fields always share a row.
export function metadataLayout(widths, availableWidth, portrait = false) {
  const order = portrait ? [1, 2, 3] : [0, 1, 2, 3];
  const visibleWidths = order.map(index => widths[index]);
  const total = visibleWidths.reduce((sum, width) => sum + width, 0);
  const spaces = visibleWidths.length - 1;
  const naturalGap = Math.min(24, Math.max(8, (availableWidth - total) / spaces));
  const scale = Math.min(1, availableWidth / (total + spaces * naturalGap));
  const fitted = visibleWidths.map(width => width * scale);
  const gap = Math.max(0, (availableWidth - fitted.reduce((sum, width) => sum + width, 0)) / spaces);
  return { order, widths: fitted, gap, scale };
}
// Same binary units and three significant digits as DeviceItemViewModel.FormatRateParts.
export function bytes(n, speed = false) {
  if (n == null || !Number.isFinite(Number(n))) return { value: "—", unit: "" };
  const v = Math.max(0, Number(n)),
    units = ["B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB"];
  const exponent = Math.max(
    speed ? 1 : 0,
    Math.min(units.length - 1, v < 1 ? 0 : Math.floor(Math.log2(v) / 10)),
  );
  const scaled = v / 1024 ** exponent;
  let value;
  if (scaled === 0) value = speed ? "0" : "0.00";
  else {
    const places =
      scaled >= 1000
        ? 0
        : Math.min(
            speed ? 2 : 3,
            Math.max(0, 3 - (Math.floor(Math.log10(scaled)) + 1)),
          );
    const rounded =
      Math.round((scaled + Number.EPSILON) * 10 ** places) / 10 ** places;
    value = speed && rounded === 0 ? "0" : rounded.toFixed(places);
  }
  return { value, unit: units[exponent] + (speed ? "/s" : "") };
}
export const rate = (n) => {
  const f = bytes(n, true);
  return f.unit ? `${f.value}\u2009${f.unit}` : f.value;
};
export const total = (n) => {
  const f = bytes(n);
  return f.unit ? `${f.value}\u2009${f.unit}` : f.value;
};
function parts(value) {
  const raw = String(value ?? "");
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})/.exec(raw);
  if (match)
    return {
      year: +match[1],
      month: match[2],
      day: match[3],
      hour: match[4],
      minute: match[5],
      second: match[6],
    };
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return null;
  const pad = (n) => String(n).padStart(2, "0");
  return {
    year: d.getFullYear(),
    month: pad(d.getMonth() + 1),
    day: pad(d.getDate()),
    hour: pad(d.getHours()),
    minute: pad(d.getMinutes()),
    second: pad(d.getSeconds()),
  };
}
export function date(value) {
  const p = parts(value);
  return !p || p.year < 2000
    ? "暂无记录"
    : `${p.month}\u2009月\u2009${p.day}\u2009日`;
}
export function clock(value, seconds = true) {
  const p = parts(value);
  return p ? `${p.hour}:${p.minute}${seconds ? ":" + p.second : ""}` : "—";
}
export function relativeDate(value, now) {
  const p = parts(value),
    n = parts(now);
  if (!p || p.year < 2000 || !n) return "暂无记录";
  const days =
    (Date.UTC(n.year, +n.month - 1, +n.day) -
      Date.UTC(p.year, +p.month - 1, +p.day)) /
    86400000;
  return days === 0
    ? "今天"
    : days === 1
      ? "昨天"
      : days === 2
        ? "前天"
        : date(value);
}
export function relativeTime(value, now = Date.now()) {
  if (!value) return "时间未知";
  const elapsed = Math.max(0, new Date(now) - new Date(value));
  if (elapsed < 60000) return "刚刚";
  if (elapsed < 3600000) return `${Math.floor(elapsed / 60000)}\u2009分钟前`;
  if (elapsed < 86400000) return `${Math.floor(elapsed / 3600000)}\u2009小时前`;
  return `${Math.floor(elapsed / 86400000)}\u2009天前`;
}
export function duration(start, end = Date.now()) {
  if (!start) return "暂无时长";
  const m = Math.max(0, Math.floor((new Date(end) - new Date(start)) / 60000));
  if (m < 60) return `${m}\u2009分钟`;
  if (m < 1440)
    return `${Math.floor(m / 60)}\u2009小时\u2009${m % 60}\u2009分钟`;
  return `${Math.floor(m / 1440)}\u2009天\u2009${Math.floor((m % 1440) / 60)}\u2009小时`;
}
export function dateMarkup(value) {
  return esc(value)
    .split("\u2009")
    .join('<span class="date-gap"> </span>')
    .replace(
      / ~ /g,
      '<span class="range-gap"> </span>~<span class="range-gap"> </span>',
    );
}
export function fields(root, rows) {
  const shape = JSON.stringify(rows.map((row) => row[0]));
  if (root.dataset.shape !== shape) {
    root.dataset.shape = shape;
    root.innerHTML = rows
      .map(([label]) => `<div><dt>${esc(label)}</dt><dd></dd></div>`)
      .join("");
  }
  rows.forEach(([, value], index) => {
    const el = root.children[index].querySelector("dd"),
      s = String(value || "—");
    if (el.textContent !== s) el.textContent = s;
  });
}
export function sessionsMarkup(items, now, compact = false) {
  return items
    .map(
      (s) =>
        `<div class="session-row" data-online-at="${esc(s.onlineAt)}"><span class="session-track" aria-hidden="true"><i></i><i></i></span><div class="session-times"><span class="session-label">${compact ? "离线" : "离线时间"}</span><span class="session-time"><span>${s.offlineAt ? dateMarkup(relativeDate(s.offlineAt, now)) : "当前在线"}</span>${s.offlineAt ? `<span>${clock(s.offlineAt, false)}</span>` : ""}</span><span class="session-label">${compact ? "上线" : "上线时间"}</span><span class="session-time"><span>${dateMarkup(relativeDate(s.onlineAt, now))}</span><span>${clock(s.onlineAt, false)}</span></span></div><span class="session-duration">${esc(duration(s.onlineAt, s.offlineAt || now))}</span></div>`,
    )
    .join("");
}
let toastTimer;
export function toast(message) {
  text("toast", message);
  $("toast").hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => ($("toast").hidden = true), 2300);
}
export async function copy(value) {
  try {
    if (navigator.clipboard && isSecureContext)
      await navigator.clipboard.writeText(value);
    else {
      const el = document.createElement("textarea");
      el.value = value;
      el.className = "clipboard-helper";
      document.body.append(el);
      el.select();
      const copied = document.execCommand("copy");
      el.remove();
      if (!copied) throw new Error("copy");
    }
    toast("已复制");
  } catch {
    toast("请长按文字复制");
  }
}
