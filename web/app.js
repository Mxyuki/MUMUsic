"use strict";

/* ================================================================== helpers */
const $ = (sel, el = document) => el.querySelector(sel);
const $$ = (sel, el = document) => [...el.querySelectorAll(sel)];
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const fmt = (n) => Number(n || 0).toLocaleString("en-US");
const dur = (s) => (s ? `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, "0")}` : "–:––");
const pad = (n, l = 5) => String(n).padStart(l, "0");
const WORK_LABEL = { anime: "Anime", vn: "VN" };
const workLabel = (kind) => WORK_LABEL[kind] || "Source";
// song links people paste: AMQ / EMQ file links, or a song id ("a123", "e456")
const SONG_LINK = /animemusicquiz\.com\/|selfhoststorage\/|^\w+\.(mp3|webm|ogg|weba)$|^[ae]\d+$/i;

const ICONS = {
  play: '<path d="M7 4.5v15l12-7.5z" fill="currentColor" stroke="none"/>',
  pause: '<path d="M6.5 5h3.8v14H6.5zM13.7 5h3.8v14h-3.8z" fill="currentColor" stroke="none"/>',
  next: '<path d="M5.5 5l9.5 7-9.5 7z" fill="currentColor" stroke="none"/><path d="M18 5v14"/>',
  prev: '<path d="M18.5 5L9 12l9.5 7z" fill="currentColor" stroke="none"/><path d="M6 5v14"/>',
  shuffle: '<path d="M3 7h3.5c5 0 6 10 11 10H21M3 17h3.5c2 0 3.2-1.6 4.2-3.5M13.3 9.5C14.3 8 15.5 7 17.5 7H21M18 4l3 3-3 3M18 14l3 3-3 3"/>',
  repeat: '<path d="M4 12V8h15M16 5l3 3-3 3M20 12v4H5M8 19l-3-3 3-3"/>',
  repeat1: '<path d="M4 12V8h15M16 5l3 3-3 3M20 12v4H5M8 19l-3-3 3-3"/><path d="M11.3 10.6l1.2-.8v4.4" stroke-width="1.4"/>',
  heart: '<path stroke-linejoin="round" d="M12 20s-7.5-4.6-7.5-10.2A4.3 4.3 0 0 1 12 7.3a4.3 4.3 0 0 1 7.5 2.5C19.5 15.4 12 20 12 20z"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  more: '<path d="M5 12h.01M12 12h.01M19 12h.01" stroke-width="3" stroke-linecap="round"/>',
  search: '<circle cx="10.5" cy="10.5" r="6"/><path d="M15 15l5 5"/>',
  radio: '<circle cx="12" cy="12" r="2"/><path d="M7.8 7.8a6 6 0 0 0 0 8.4M16.2 7.8a6 6 0 0 1 0 8.4M5 5a10 10 0 0 0 0 14M19 5a10 10 0 0 1 0 14"/>',
  queue: '<path d="M4 6h12M4 11h12M4 16h7M15 14l5 3-5 3z"/>',
  volume: '<path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z"/><path d="M15.5 9a4 4 0 0 1 0 6M18 6.5a7.5 7.5 0 0 1 0 11"/>',
  mute: '<path d="M4 9.5h3.5L12 5.5v13l-4.5-4H4z"/><path d="M16 9.5l5 5M21 9.5l-5 5"/>',
  sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2.5V5M12 19v2.5M2.5 12H5M19 12h2.5M5.3 5.3l1.8 1.8M16.9 16.9l1.8 1.8M5.3 18.7l1.8-1.8M16.9 7.1l1.8-1.8"/>',
  moon: '<path d="M19.5 14.5A8 8 0 0 1 9.5 4.5a8 8 0 1 0 10 10z"/>',
  close: '<path d="M6 6l12 12M18 6L6 18"/>',
  trash: '<path d="M4 7h16M9 7V4h6v3M6.5 7l1 13h9l1-13"/>',
  download: '<path d="M12 4v11M7 10l5 5 5-5M4 20h16"/>',
  upload: '<path d="M12 16V5M7 10l5-5 5 5M4 20h16"/>',
  home: '<path d="M4 11l8-7 8 7v9h-5.5v-6h-5v6H4z"/>',
  spark: '<path d="M12 3l2 6.5 6.5 2.5-6.5 2.5L12 21l-2-6.5L3.5 12 10 9.5z"/>',
  clock: '<circle cx="12" cy="12" r="8.5"/><path d="M12 7.5V12l3 2"/>',
  tag: '<path d="M3.5 12.5V4H12l8.5 8.5-8 8z"/><circle cx="8" cy="8.5" r="1.3"/>',
  list: '<path d="M4 6h16M4 12h16M4 18h10"/>',
  user: '<circle cx="12" cy="8" r="4"/><path d="M4 21c1-4.5 4.5-6.5 8-6.5s7 2 8 6.5"/>',
  sliders: '<path d="M4 7h10M18 7h2M4 17h4M12 17h8"/><circle cx="16" cy="7" r="2"/><circle cx="10" cy="17" r="2"/>',
  data: '<path d="M4 6c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3zM4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/>',
  link: '<path d="M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1"/>',
  tv: '<path d="M3.5 7h17v12h-17zM8 3l4 4 4-4"/>',
  playnext: '<path d="M4 6h10M4 11h10M4 16h6M16 13v8M12 17h8"/>',
  info: '<circle cx="12" cy="12" r="8.5"/><path d="M12 11v5.5M12 7.6v.4"/>',
  edit: '<path d="M4 20h4L19 9l-4-4L4 16z"/>',
  save: '<path d="M5 4h11l3 3v13H5zM8 4v5h7V4M8 20v-6h8v6"/>',
  level: '<path d="M4 8h3M4 16h3M17 8h3M17 16h3M10.5 5v14M13.5 8v8"/>',
  anilist: '<path d="M4 19L9.5 5h2L17 19M6.6 13.5h7.8M17 5h3v14"/>',
  refresh: '<path d="M19.5 12a7.5 7.5 0 1 1-2.2-5.3M19.5 4v4.5H15"/>',
  vndb: '<path d="M3.5 5l5 14h1.5L15 5M17 5h3.5v14"/>',
  eye: '<path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z"/><circle cx="12" cy="12" r="3"/>',
  eyeoff: '<path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12zM4 4l16 16"/>',
};
const ic = (name) => `<svg viewBox="0 0 24 24" aria-hidden="true">${ICONS[name] || ""}</svg>`;

const store = {
  get(key, fallback) {
    try { const v = localStorage.getItem(key); return v === null ? fallback : JSON.parse(v); } catch { return fallback; }
  },
  set(key, value) {
    try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* private mode etc. */ }
  },
};

async function request(method, url, body) {
  const res = await fetch(url, {
    method,
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  let data = null;
  try { data = await res.json(); } catch { /* empty */ }
  if (!res.ok) throw new Error((data && data.error) || `HTTP ${res.status} (${method} ${url})`);
  return data;
}
const api = {
  get: (u) => request("GET", u),
  post: (u, b) => request("POST", u, b || {}),
  put: (u, b) => request("PUT", u, b || {}),
  patch: (u, b) => request("PATCH", u, b || {}),
  del: (u) => request("DELETE", u),
};

function hash(str) {
  let h = 2166136261;
  for (const c of String(str)) { h ^= c.codePointAt(0); h = Math.imul(h, 16777619); }
  return h >>> 0;
}
function initials(name) {
  const words = String(name || "?").replace(/[^\p{L}\p{N} ]/gu, " ").split(/\s+/).filter(Boolean);
  if (!words.length) return "#";
  return (words.length === 1 ? words[0].slice(0, 2) : words[0][0] + words[1][0]).toUpperCase();
}

/* Generated cover: a little technical "data plate" made from a hash of the seed. */
function art(seed, glyph, { code = "", person = false, cls = "" } = {}) {
  const h = hash(seed);
  const variant = ["v0", "v0", "v0", "v1", "v1", "v2", "v2", "v3"][h % 8];
  const lines = Array.from({ length: 5 }, (_, i) => `<i style="height:${25 + ((h >> (i * 3)) & 7) * 11}%"></i>`).join("");
  const bar = 18 + ((h >> 9) % 60);
  return `<div class="art ${variant} ${person ? "person" : ""} ${cls}">
    <span class="code">${esc(code)}</span><span class="lines">${lines}</span><span class="ring"></span>
    <span class="tri"></span><span class="glyph">${esc(glyph)}</span><span class="bar" style="width:${bar}%"></span></div>`;
}
const songArt = (s, cls = "") => art(s.workId, initials(s.work || s.name), { code: `${s.src === "emq" ? "EMQ" : "AMQ"} ${s.id.slice(1)}`, cls });

/* ================================================================== state */
const state = {
  likes: new Set(),
  playlists: [],
  cache: new Map(),             // song id -> song view
  lists: new Map(),             // list id -> [song ids] for the current page
  listSeq: 0,
  stats: null,
  queue: [], qi: -1, original: null,
  shuffle: store.get("shuffle", false),
  repeat: store.get("repeat", "off"),        // off | all | one
  radio: { on: false, seed: null },
  discoveryMode: store.get("discoveryMode", "close"),   // close | explore
  history: store.get("history", []),         // session-ish play history, used to avoid discovery loops
  volume: store.get("volume", 0.8),
  level: store.get("level", true),            // play every song at the same loudness
  muted: false,
  panelTab: "song",
  panelSong: null,
  ctx: null,                                  // page context, e.g. { playlist: 12 }
  sources: ["amq", "emq"],                    // libraries in use (Settings: AMQ, EMQ or both)
  app: null,                                  // /api/app: version, library, status
};
const audio = $("#audio");
const stage = $("#stage");

/* volume levelling: AMQ and EMQ measured every song's mean volume (dB). The <audio> element is routed through a Web Audio
   gain node that brings each song to LEVEL_DB (about the library median), so quiet songs get lifted and loud ones
   lowered; a limiter catches the peaks of lifted songs. AMQ's CDN allows CORS (the element has crossorigin), which
   Web Audio needs to hear cross-origin media at all; EMQ songs come through the local server. The user's volume is applied in the same gain node. */
const LEVEL_DB = -18, MAX_BOOST_DB = 10;
let actx = null, gainNode = null;
function audioGraph() {
  if (actx) { if (actx.state === "suspended") actx.resume(); return; }
  try {
    actx = new AudioContext();
    gainNode = actx.createGain();
    const limiter = actx.createDynamicsCompressor();
    limiter.threshold.value = -1; limiter.knee.value = 0; limiter.ratio.value = 20;
    limiter.attack.value = 0.002; limiter.release.value = 0.15;
    actx.createMediaElementSource(audio).connect(gainNode).connect(limiter).connect(actx.destination);
    if (window.chrome?.webview) hostOutput(limiter).catch((e) => console.warn("host audio output unavailable", e));
  } catch (e) { console.warn("volume levelling unavailable", e); actx = gainNode = null; }
  applyVolume();
}

/* host audio output: WebView2 plays sound from its own processes, which Discord doesn't capture when the app is
   streamed. In the desktop app the final mix goes to the host as 16-bit stereo PCM over a WebSocket and the host plays
   it (src/MUMUsic.Desktop/Audio/PageAudio.cs); the page stays silent while the socket is open. Silent blocks (paused)
   aren't sent, so the host can close the device. */
const HOST_TAP = `
class HostTap extends AudioWorkletProcessor {
  constructor() {
    super();
    this.on = false;
    this.pcm = new Int16Array(2048);
    this.n = 0;
    this.port.onmessage = (e) => { this.on = e.data; this.n = 0; };
  }
  process(inputs, outputs) {
    const input = inputs[0], output = outputs[0];
    if (!input.length) return true;
    const left = input[0], right = input[1] || input[0];
    if (!this.on) {
      output[0].set(left);
      if (output[1]) output[1].set(right);
      return true;
    }
    let sound = false;
    for (let i = 0; i < left.length; i++) {
      if (left[i] !== 0 || right[i] !== 0) { sound = true; break; }
    }
    if (!sound) return true;
    for (let i = 0; i < left.length; i++) {
      this.pcm[this.n++] = Math.max(-1, Math.min(1, left[i])) * 32767;
      this.pcm[this.n++] = Math.max(-1, Math.min(1, right[i])) * 32767;
    }
    if (this.n === this.pcm.length) {
      this.port.postMessage(this.pcm.buffer, [this.pcm.buffer]);
      this.pcm = new Int16Array(2048);
      this.n = 0;
    }
    return true;
  }
}
registerProcessor("host-tap", HostTap);`;
async function hostOutput(limiter) {
  await actx.audioWorklet.addModule(URL.createObjectURL(new Blob([HOST_TAP], { type: "text/javascript" })));
  const tap = new AudioWorkletNode(actx, "host-tap", { outputChannelCount: [2] });
  limiter.disconnect();
  limiter.connect(tap).connect(actx.destination);
  const connect = () => {
    const ws = new WebSocket(`ws://${location.host}/api/audio?rate=${actx.sampleRate}`);
    ws.binaryType = "arraybuffer";
    ws.onopen = () => tap.port.postMessage(true);
    ws.onclose = () => { tap.port.postMessage(false); setTimeout(connect, 3000); };
    tap.port.onmessage = (e) => { if (ws.readyState === WebSocket.OPEN && ws.bufferedAmount < 1 << 20) ws.send(e.data); };
  };
  connect();
}
// An AudioContext may only start after a user gesture, and while it is suspended the music is silent: build it on the
// first click / key press (before anything can play through it) and resume it on later ones if the browser paused it.
for (const ev of ["pointerdown", "keydown"]) addEventListener(ev, audioGraph, { capture: true });
function songGain(s) {
  if (!state.level || s?.vol == null) return 1;
  return 10 ** (Math.min(MAX_BOOST_DB, LEVEL_DB - s.vol) / 20);
}
// The slider is in decibels (VOLUME_DB from silent-ish to full): loudness is heard logarithmically, so with a linear
// slider all the useful range was squeezed into its first quarter. state.volume stays the gain itself.
const VOLUME_DB = 50;
const volumeToPos = (g) => (g > 0 ? Math.max(0, 1 + (20 * Math.log10(g)) / VOLUME_DB) : 0);
const posToVolume = (p) => (p > 0 ? 10 ** ((VOLUME_DB * (p - 1)) / 20) : 0);
function applyVolume() {
  const user = state.muted ? 0 : state.volume, g = songGain(current());
  if (gainNode) { audio.volume = 1; gainNode.gain.setTargetAtTime(user * g, actx.currentTime, 0.03); }
  else audio.volume = user * Math.min(1, g);     // no Web Audio yet: can only turn loud songs down
}

function remember(songs) {
  for (const s of songs || []) state.cache.set(s.id, { ...state.cache.get(s.id), ...s });
  return songs;
}
function regList(songs) {
  const id = `L${++state.listSeq}`;
  state.lists.set(id, songs.map((s) => s.id));
  return id;
}
const current = () => state.cache.get(state.queue[state.qi]);

/* ================================================================== toasts, menus, modals */
function toast(msg, { error = false, action, onAction, ms = 3200 } = {}) {
  const el = document.createElement("div");
  el.className = `toast${error ? " err" : ""}`;
  el.innerHTML = `<span>${esc(msg)}</span>${action ? `<button>${esc(action)}</button>` : ""}`;
  if (action) el.querySelector("button").onclick = () => { onAction(); el.remove(); };
  $("#toasts").append(el);
  setTimeout(() => el.remove(), ms);
}

const menuEl = $("#menu");
function openMenu(anchor, items) {
  menuEl.innerHTML = "";
  for (const it of items) {
    if (it === "hr") { menuEl.insertAdjacentHTML("beforeend", "<hr>"); continue; }
    if (it.label && !it.run) { menuEl.insertAdjacentHTML("beforeend", `<div class="label">${esc(it.label)}</div>`); continue; }
    if (it.group) {
      const box = document.createElement("div");
      box.className = "scroll";
      for (const sub of it.group) box.append(menuButton(sub));
      menuEl.append(box);
      continue;
    }
    menuEl.append(menuButton(it));
  }
  menuEl.hidden = false;
  const r = anchor.getBoundingClientRect ? anchor.getBoundingClientRect() : { left: anchor.x, right: anchor.x, top: anchor.y, bottom: anchor.y };
  const mw = menuEl.offsetWidth, mh = menuEl.offsetHeight;
  let x = r.right - mw, y = r.bottom + 4;
  if (x < 8) x = Math.min(r.left, innerWidth - mw - 8);
  if (y + mh > innerHeight - 8) y = r.top - mh - 4 >= 8 ? r.top - mh - 4 : Math.max(8, innerHeight - mh - 8);
  menuEl.style.left = `${x}px`;
  menuEl.style.top = `${y}px`;
}
function menuButton(it) {
  const b = document.createElement("button");
  b.innerHTML = `${ic(it.icon || "list")}<span>${esc(it.text)}</span>`;
  b.onclick = () => { closeMenu(); it.run(); };
  return b;
}
function closeMenu() { menuEl.hidden = true; }
document.addEventListener("pointerdown", (e) => { if (!menuEl.hidden && !menuEl.contains(e.target)) closeMenu(); });
addEventListener("resize", closeMenu);
$("#view").addEventListener("scroll", closeMenu, { passive: true });

// Resolves with true / the input's value / read(wrap) on OK, null on cancel, "alt" for the optional third button.
function modal({ title, eyebrow = "", body = "", input, inputType = "text", confirm = "OK", cancel = "Cancel", alt, danger = false, read }) {
  return new Promise((resolve) => {
    const wrap = $("#modal");
    wrap.innerHTML = `<div class="modal" role="dialog" aria-modal="true">
      <header><div class="eyebrow">${esc(eyebrow)}</div><h3>${esc(title)}</h3></header>
      <div class="body">${body}${input !== undefined ? `<input type="${inputType}" value="${esc(input)}" maxlength="120" autocomplete="off">` : ""}</div>
      <footer>${alt ? `<button class="btn ghost" data-alt style="margin-right:auto">${esc(alt)}</button>` : ""}<button class="btn ghost" data-x>${esc(cancel)}</button><button class="btn ${danger ? "dark" : "primary"}" data-ok>${esc(confirm)}</button></footer></div>`;
    wrap.hidden = false;
    const field = $("input", wrap);
    if (field) { field.focus(); field.select(); }
    const done = (v) => { wrap.hidden = true; wrap.innerHTML = ""; resolve(v); };
    const ok = () => done(read ? read(wrap) : field ? field.value.trim() : true);
    $("[data-ok]", wrap).onclick = ok;
    $("[data-x]", wrap).onclick = () => done(null);
    if (alt) $("[data-alt]", wrap).onclick = () => done("alt");
    wrap.onclick = (e) => { if (e.target === wrap) done(null); };
    wrap.onkeydown = (e) => {
      if (e.key === "Escape") done(null);
      if (e.key === "Enter") ok();
    };
  });
}

/* ================================================================== likes & playlists */
async function loadLikes() {
  const d = await api.get("/api/likes");
  state.likes = new Set(d.ids);
  remember(d.songs);
  renderNav();
}
async function toggleLike(id) {
  const liked = state.likes.has(id);
  liked ? state.likes.delete(id) : state.likes.add(id);
  syncLikeButtons(id);
  renderNav();
  try {
    liked ? await api.del(`/api/likes/${id}`) : await api.post(`/api/likes/${id}`);
    if (!liked) toast(`Liked “${state.cache.get(id)?.name || "song"}”`);
  } catch (e) {
    liked ? state.likes.add(id) : state.likes.delete(id);
    syncLikeButtons(id);
    renderNav();
    toast(`Could not save: ${e.message}`, { error: true });
  }
}
function syncLikeButtons(id) {
  for (const b of $$(`[data-like="${id}"]`)) {
    b.classList.toggle("liked", state.likes.has(id));
    b.title = state.likes.has(id) ? "Remove from liked" : "Like";
  }
}

async function loadPlaylists() {
  state.playlists = (await api.get("/api/playlists")).playlists;
  renderNav();
}
async function createPlaylist(ids = []) {
  const name = await modal({ title: "New playlist", eyebrow: "Create", input: "My playlist", confirm: "Create" });
  if (!name) return null;
  const { id } = await api.post("/api/playlists", { name, ids });
  await loadPlaylists();
  toast(ids.length ? `Created “${name}” with ${ids.length} song${ids.length > 1 ? "s" : ""}` : `Created “${name}”`);
  return id;
}
async function addToPlaylist(pid, ids) {
  const { added } = await api.post(`/api/playlists/${pid}/songs`, { ids });
  const p = state.playlists.find((x) => x.id === pid);
  await loadPlaylists();
  toast(added ? `Added ${added} song${added > 1 ? "s" : ""} to “${p?.name}”` : `Already in “${p?.name}”`);
  if (state.ctx?.playlist === pid) render();
}

// Every song of every anime on an AniList list. Importing the same user again adds what is new to the same playlist.
const ANILIST_STATUSES = [["CURRENT", "Watching"], ["COMPLETED", "Completed"], ["REPEATING", "Rewatching"],
                          ["PAUSED", "Paused"], ["PLANNING", "Planning"], ["DROPPED", "Dropped"]];
async function importAniList() {
  const last = store.get("anilist", { user: "", statuses: ["CURRENT", "COMPLETED", "REPEATING"] });
  const form = await modal({
    title: "AniList playlist", eyebrow: "Import", confirm: "Import",
    body: `<p>Makes a playlist with all the songs of the anime on an AniList list (the list must be public).
        Import again later to add the songs of newly added anime.</p>
      <input type="text" data-user value="${esc(last.user)}" placeholder="AniList user name or profile link" maxlength="120">
      <div class="checks">${ANILIST_STATUSES.map(([k, label]) => `<label><input type="checkbox" value="${k}"
        ${last.statuses.includes(k) ? "checked" : ""}><span>${label}</span></label>`).join("")}</div>`,
    read: (w) => ({ user: $("[data-user]", w).value.trim(), statuses: $$("input[type=checkbox]:checked", w).map((c) => c.value) }),
  });
  if (!form?.user) return;
  if (!form.statuses.length) { toast("Pick at least one list", { error: true }); return; }
  store.set("anilist", form);
  toast(`Loading ${form.user.replace(/^.*anilist\.co\/user\//i, "").split("/")[0]}'s list…`);
  try {
    const r = await api.post("/api/anilist", form);
    await loadPlaylists();
    location.hash = `#/playlist/${r.id}`;
    const missing = r.entries - r.found;
    toast(`${r.created ? "Created" : "Updated"} “${r.name}”: ${r.added ? `${fmt(r.added)} new songs` : "no new songs"}`
      + ` from ${fmt(r.found)} anime${missing ? ` (${missing} not in the AMQ library)` : ""}`, { ms: 7000 });
  } catch (e) { toast(e.message, { error: true, ms: 6000 }); }
}

// Same as AniList for visual novels: every song of every VN on a VNDB list (only the labels the user made public).
const VNDB_LABELS = [[1, "Playing"], [2, "Finished"], [3, "Stalled"], [4, "Dropped"], [5, "Wishlist"], [7, "Voted"]];
async function importVndb() {
  const last = store.get("vndb", { user: "", labels: [1, 2], bgm: true });
  const form = await modal({
    title: "VNDB playlist", eyebrow: "Import", confirm: "Import",
    body: `<p>Makes a playlist with all the songs of the visual novels on a VNDB list (only its public labels are seen).
        Import again later to add the songs of newly added VNs.</p>
      <input type="text" data-user value="${esc(last.user)}" placeholder="VNDB user name or profile link" maxlength="120">
      <div class="checks">${VNDB_LABELS.map(([k, label]) => `<label><input type="checkbox" data-label value="${k}"
        ${last.labels.includes(k) ? "checked" : ""}><span>${label}</span></label>`).join("")}</div>
      <div class="checks"><label><input type="checkbox" data-bgm ${last.bgm ? "checked" : ""}><span>Include BGM</span></label></div>`,
    read: (w) => ({ user: $("[data-user]", w).value.trim(), labels: $$("[data-label]:checked", w).map((c) => Number(c.value)),
                    bgm: $("[data-bgm]", w).checked }),
  });
  if (!form?.user) return;
  if (!form.labels.length) { toast("Pick at least one list", { error: true }); return; }
  store.set("vndb", form);
  toast(`Loading ${form.user.replace(/^.*vndb\.org\//i, "").split("/")[0]}'s list…`);
  try {
    const r = await api.post("/api/vndb", form);
    await loadPlaylists();
    location.hash = `#/playlist/${r.id}`;
    const missing = r.entries - r.found;
    toast(`${r.created ? "Created" : "Updated"} “${r.name}”: ${r.added ? `${fmt(r.added)} new songs` : "no new songs"}`
      + ` from ${fmt(r.found)} VNs${missing ? ` (${missing} not in the EMQ library)` : ""}`, { ms: 7000 });
  } catch (e) { toast(e.message, { error: true, ms: 6000 }); }
}

/* ================================================================== navigation */
const NAV = [
  ["home", "Home", "home"],
  ["recommended", "For you", "spark"],
  ["liked", "Liked", "heart"],
  ["recent", "History", "clock"],
  ["tags", "Tags", "tag"],
];
function renderNav() {
  const route = location.hash.slice(2).split(/[/?]/)[0] || "home";
  $("#nav").innerHTML = NAV.map(([r, label, icon], i) => `<a href="#/${r}" class="${route === r ? "active" : ""}">
      <span class="idx">0${i + 1}</span>${ic(icon)}${label}${r === "liked" ? `<span class="count">${state.likes.size}</span>` : ""}</a>`).join("");
  const pid = route === "playlist" ? Number(location.hash.split("/")[2]) : null;
  $("#navPlaylists").innerHTML = state.playlists.length
    ? state.playlists.map((p) => `<a href="#/playlist/${p.id}" data-drop-playlist="${p.id}" class="${pid === p.id ? "active" : ""}">
        <span>${esc(p.name)}</span><small>${p.count}</small></a>`).join("")
    : `<div class="empty">No playlists yet. Use + or a song's menu.</div>`;
}

/* ================================================================== song rows */
function tagChips(tags) {
  return `<div class="tagrow">${(tags || []).map((t, i) => `<a class="tg ${i < 2 ? "g" : ""}" href="#/tag/${encodeURIComponent(t)}">${esc(t)}</a>`).join("")}</div>`;
}
function byLink(s) {
  return s.by ? `<a href="#/artist/${s.by}">${esc(s.artist)}</a>` : esc(s.artist);
}

// which library a song is from, shown when both are in use
const srcBadge = (s) => (state.sources.length > 1 ? `${s.src === "emq" ? "EMQ" : "AMQ"} · ` : "");
function workColumn(songs) {
  const kinds = new Set(songs.map((s) => s.workKind));
  return kinds.size === 1 ? workLabel([...kinds][0]) : "Anime / VN";
}

// Chips over a playlist, the liked songs or an artist's songs: show only openings, endings... and / or only anime or
// VN songs (several chips at once). The choice is in the page link (?kinds=OP,ED&from=vn), so Play / Shuffle /
// Discover use the songs shown.
const songKind = (s) => (s.kind || "").split(":")[1] || "OTHER";
const SONG_FILTERS = [
  { param: "kinds", key: songKind,
    chips: [["OP", "Openings"], ["ED", "Endings"], ["INS", "Inserts"], ["BGM", "BGM / OST"], ["OTHER", "Other"]] },
  { param: "from", key: (s) => (s.workKind === "vn" ? "vn" : "anime"), chips: [["anime", "Anime"], ["vn", "VN"]] },
];
function songFilters(songs, params) {
  let shown = songs, filtered = false;
  const groups = [];
  for (const f of SONG_FILTERS) {
    const counts = {};
    for (const s of songs) counts[f.key(s)] = (counts[f.key(s)] || 0) + 1;
    const present = f.chips.filter(([k]) => counts[k]);
    if (present.length < 2) continue;
    const picked = new Set((params.get(f.param) || "").split(",").filter((k) => counts[k]));
    if (picked.size) { shown = shown.filter((s) => picked.has(f.key(s))); filtered = true; }
    groups.push(`<button class="chip ${picked.size ? "" : "on"}" data-song-filter="${f.param}:">All<small>${fmt(songs.length)}</small></button>`
      + present.map(([k, label]) => `<button class="chip ${picked.has(k) ? "on" : ""}" data-song-filter="${f.param}:${k}">${label}<small>${fmt(counts[k])}</small></button>`).join(""));
  }
  const html = groups.length ? `<div class="chips song-filters">${groups.join('<span class="chip-sep"></span>')}</div>` : "";
  return { songs: shown, html, filtered };
}
function toggleSongFilter(spec) {
  const [param, value] = spec.split(":");
  setHashParam(param, (picked) => {
    if (!value) picked.clear();
    else if (picked.has(value)) picked.delete(value);
    else picked.add(value);
  });
}
// Changes one comma-separated parameter of the page link, keeping the others.
function setHashParam(param, change) {
  const [base, query = ""] = location.hash.split("?");
  const params = new URLSearchParams(query);
  const values = new Set((params.get(param) || "").split(",").filter(Boolean));
  change(values);
  if (values.size) params.set(param, [...values].join(",")); else params.delete(param);
  location.hash = params.size ? `${base}?${params}` : base;
}

function songTable(songs, { numbered = true, match = false, because = false, draggable = false, sortable = true } = {}) {
  remember(songs);
  if (!songs.length) return "";
  const lid = regList(songs);
  const cur = state.queue[state.qi];
  const rows = songs.map((s, i) => `<tr class="row ${s.id === cur ? "current" : ""} ${s.audio ? "" : "unplayable"}" data-row="${s.id}" data-list="${lid}" data-i="${i}" draggable="true">
      <td class="idx-cell"><span class="n">${numbered ? i + 1 : ""}</span><span class="eq"><i></i><i></i><i></i></span>
        <button class="p" data-play="${lid}:${i}" title="Play">${ic("play")}</button></td>
      <td><div class="title-cell"><span class="t" data-song="${s.id}">${esc(s.name)}</span>
        <span class="a">${byLink(s)}${because && s.because ? ` <span class="match">· because you liked ${esc(s.because.name)}</span>` : ""}</span></div></td>
      <td class="work-cell"><a href="#/work/${s.workId}">${esc(s.work)}</a><small>${srcBadge(s)}${esc(s.type || "")}${s.year ? ` · ${s.year}` : ""}${match && s.match ? ` · ${Math.round(s.match * 100)}% match` : ""}</small></td>
      <td>${tagChips(s.tags)}</td>
      <td class="dur-cell">${dur(s.dur)}</td>
      <td class="act-cell"><button class="icon-btn ${state.likes.has(s.id) ? "liked" : ""}" data-like="${s.id}" title="Like">${ic("heart")}</button><button class="icon-btn" data-more="${s.id}" title="More">${ic("more")}</button></td>
    </tr>`).join("");
  const th = (key, label, cls) => `<th class="${cls} ${sortable ? "sortable" : ""}" ${sortable ? `data-sort="${key}"` : ""}>${label}</th>`;
  return `<table class="tracks" data-table="${lid}" ${draggable ? 'data-reorder="1"' : ""}>
    <colgroup><col class="c-idx"><col class="c-title"><col class="c-work"><col class="c-tags"><col class="c-dur"><col class="c-act"></colgroup>
    <thead><tr><th>#</th>${th("name", "Title", "")}${th("work", workColumn(songs), "")}<th>Tags</th>${th("dur", "Time", "c-dur")}<th></th></tr></thead>
    <tbody>${rows}</tbody></table>`;
}

function cards(songs, { why = false } = {}) {
  remember(songs);
  const lid = regList(songs);
  return `<div class="cards">${songs.map((s, i) => `<div class="card" data-card="${s.id}">
      ${songArt(s)}
      <button class="play-fab" data-play="${lid}:${i}" title="Play">${ic("play")}</button>
      <div class="meta"><div class="t" data-song="${s.id}">${esc(s.name)}</div><div class="s">${esc(s.artist)}</div>
      ${why && s.because ? `<div class="why">↳ ${esc(s.because.name)}</div>` : `<div class="why">${esc(s.work)}</div>`}</div></div>`).join("")}</div>`;
}

function section(num, title, body, more = "") {
  return `<section class="section"><div class="section-head"><span class="num">${num}</span><h2>${esc(title)}</h2><span class="rule"></span>${more}</div>${body}</section>`;
}

function markCurrent() {
  const id = state.queue[state.qi];
  for (const r of $$("tr.row")) r.classList.toggle("current", r.dataset.row === id);
  for (const r of $$(".mini[data-qi]")) r.classList.toggle("current", Number(r.dataset.qi) === state.qi);
  document.body.classList.toggle("playing", !audio.paused);
}

/* ================================================================== pages */
const view = $("#view");
const loading = (what = "Loading") => `<div class="loading">${esc(what)}</div>`;
const empty = (big, text, extra = "") => `<div class="empty-state"><div class="big">${esc(big)}</div><p>${text}</p>${extra}</div>`;

function head({ eyebrow, title, sub = "", artHtml = "", actions = "", editable = false }) {
  return `<div class="page-head ${artHtml ? "" : "no-art"}">${artHtml ? `<div class="big-art">${artHtml}</div>` : ""}
    <div><div class="eyebrow">${esc(eyebrow)}</div><h1 class="page-title ${editable ? "editable" : ""}" ${editable ? 'data-rename="1" title="Click to rename"' : ""}>${esc(title)}</h1>
    <div class="page-sub">${sub}</div><div class="actions">${actions}</div></div></div>`;
}
function listActions(lid, { radio = true, save = true } = {}) {
  return `<button class="btn primary" data-playall="${lid}">${ic("play")}Play</button>
    <button class="btn" data-shuffleall="${lid}">${ic("shuffle")}Shuffle</button>
    ${radio ? `<button class="btn" data-radioall="${lid}" title="Start a discovery run seeded from this list">${ic("radio")}Discover</button>` : ""}
    ${save ? `<button class="btn ghost" data-saveall="${lid}" title="Save these songs as a playlist">${ic("save")}Save as playlist</button>` : ""}`;
}

const pages = {
  async home() {
    const d = await api.get("/api/home");
    state.stats = d.stats;
    updateMeta();
    remember(d.recent); remember(d.recommended); remember(d.discover);
    const st = d.stats;
    const pct = st.songs ? (st.analysed / st.songs) * 100 : 0;
    const genre = d.tags.filter((t) => t.group === "genre").slice(0, 16);
    const mood = d.tags.filter((t) => t.group === "mood");
    const startLabel = state.likes.size ? "Discover from your likes" : "Start discovery";
    let html = `<div class="hero">
      <div class="eyebrow">Terminal // ${esc(sourceName())} song archive</div>
      <h1>Find your next<br><em>favourite</em> song</h1>
      <p>Every song in the library has been listened to by the tagger. Like what you enjoy, and discovery mode keeps finding new songs that sound like it — without looping back.</p>
      <div class="actions"><button class="btn primary" data-start-disco>${ic("radio")}${startLabel}</button>
        <a class="btn" href="#/recommended">${ic("spark")}For you</a></div>
      <div class="hero-stats">
        <div><b>${fmt(st.songs)}</b><span>Songs</span></div>
        <div><b>${fmt(st.analysed)}</b><span>Analysed</span><div class="progress-bar"><i style="width:${pct.toFixed(1)}%"></i></div></div>
        <div><b>${fmt(st.liked)}</b><span>Liked</span></div>
        <div><b>${fmt(st.playlists)}</b><span>Playlists</span></div>
      </div></div>`;
    let n = 1;
    if (d.recommended.length) html += section(`0${n++}`, "Recommended for you", cards(d.recommended.slice(0, 12), { why: true }), `<a class="more" href="#/recommended">See all →</a>`);
    if (d.recent.length) html += section(`0${n++}`, "Recently played", cards(d.recent.slice(0, 12)), `<a class="more" href="#/recent">History →</a>`);
    html += section(`0${n++}`, "Random picks", cards(d.discover.slice(0, 12)), `<a class="more" href="#/home" data-reload>Shuffle →</a>`);
    if (d.tags.length) {
      html += section(`0${n++}`, "Browse by sound", `<div class="tag-groups">
        <div><h3>Genre</h3><div class="chips">${genre.map((t) => `<a class="chip tag genre" href="#/tag/${encodeURIComponent(t.tag)}">${esc(t.tag)}<small>${fmt(t.count)}</small></a>`).join("")}</div></div>
        <div><h3>Mood</h3><div class="chips">${mood.map((t) => `<a class="chip tag mood" href="#/tag/${encodeURIComponent(t.tag)}">${esc(t.tag)}<small>${fmt(t.count)}</small></a>`).join("")}</div></div>
      </div>`, `<a class="more" href="#/tags">All tags →</a>`);
    }
    return html;
  },

  async search(params) {
    const q = params.get("q") || "";
    if (!q.trim()) return empty("Search", "Type a song, artist, anime, VN or tag in the search bar. You can also paste an AMQ or EMQ song link.");
    const d = await api.get(`/api/search?q=${encodeURIComponent(q)}`);
    const total = d.songs.length + d.people.length + d.works.length + d.tags.length;
    if (!total) return head({ eyebrow: "Search", title: `“${q}”` }) + empty("No results", "Nothing matched. Try fewer words or a romaji spelling.");
    let html = head({ eyebrow: `Search // ${fmt(d.songs.length)} songs`, title: `“${q}”` });
    let n = 1;
    const people = d.people;
    if (d.tags.length) html += section(`0${n++}`, "Tags", `<div class="chips">${d.tags.map((t) => `<a class="chip tag" href="#/tag/${encodeURIComponent(t)}">${esc(t)}</a>`).join("")}</div>`);
    if (people.length) {
      html += section(`0${n++}`, "Artists & groups", `<div class="people">${people.map((p) => `<a class="person" href="#/artist/${p.id}">
        ${art(p.id, initials(p.name), { person: true, code: p.kind === "group" ? "GRP" : "ART" })}
        <span class="t">${esc(p.name)}</span><span class="s">${p.kind} · ${p.count} song${p.count === 1 ? "" : "s"}</span></a>`).join("")}</div>`);
    }
    if (d.songs.length) html += section(`0${n++}`, "Songs", songTable(d.songs));
    if (d.works.length) html += section(`0${n++}`, "Anime & VNs", `<div class="work-list">${d.works.map((w) => `<a href="#/work/${w.id}"><span>${esc(w.name)}</span><small>${workLabel(w.kind)}${w.year ? ` · ${w.year}` : ""}</small></a>`).join("")}</div>`);
    return html;
  },

  async liked(params) {
    const d = await api.get("/api/likes");
    state.likes = new Set(d.ids);
    const kinds = songFilters(d.songs, params);
    const lid = kinds.songs.length ? regList(kinds.songs) : null;
    const h = head({
      eyebrow: "Collection", title: "Liked songs",
      sub: `<span class="mono">${fmt(d.songs.length)} songs</span>`,
      artHtml: art("liked", "♥", { code: "LIKED", cls: "v3" }),
      actions: lid ? listActions(lid) : "",
    });
    if (!d.songs.length) return h + empty("Nothing liked yet", "Hit the heart on songs you enjoy. Your likes power the “For you” recommendations and discovery mode.");
    return h + `<div class="list-tools"><input class="filter" placeholder="Filter liked songs" data-filter></div>` + kinds.html + songTable(kinds.songs);
  },

  async recommended(params) {
    const seed = params.get("seed");
    if (!state.likes.size) {
      return head({ eyebrow: "For you", title: "Recommended", artHtml: art("rec", "FY", { code: "REC", cls: "v2" }) })
        + empty("Like a few songs first", "Recommendations are built from the sound of the songs you like. Like 3–5 songs you love and come back.", `<button class="btn primary" data-start-disco>${ic("radio")}Start discovery</button>`);
    }
    const d = await api.get(`/api/recommended?n=80${seed ? `&seed=${seed}` : ""}`);
    const lid = regList(d.songs);
    return head({
      eyebrow: "For you", title: "Recommended",
      sub: `<span>Songs that sound like several of your ${fmt(state.likes.size)} liked songs — max 2 per artist and per anime / VN.</span>`,
      artHtml: art("rec", "FY", { code: "REC", cls: "v2" }),
      actions: listActions(lid) + `<a class="btn ghost" href="#/recommended?seed=${Date.now() % 100000}">${ic("refresh")}Refresh</a>`,
    }) + songTable(d.songs, { because: true, match: true });
  },

  async recent() {
    const d = await api.get("/api/recent");
    const h = head({ eyebrow: "History", title: "Recently played", sub: `<span class="mono">${d.songs.length} songs</span>` });
    if (!d.songs.length) return h + empty("Nothing played yet", "Songs you play show up here.");
    return h.replace('<div class="actions"></div>', `<div class="actions">${listActions(regList(d.songs))}</div>`) + songTable(d.songs, { sortable: false });
  },

  async tags() {
    const d = await api.get("/api/tags");
    const groups = {};
    for (const t of d.tags) (groups[t.group] ||= []).push(t);
    const order = ["genre", "mood", "vocals", "instruments", "tempo"];
    return head({ eyebrow: "Browse", title: "Tags", sub: "<span>Tags are given by the audio tagger. Click one to hear the songs that fit it best.</span>" })
      + order.filter((g) => groups[g]).map((g, i) => section(`0${i + 1}`, g, `<div class="chips">${groups[g].map((t) =>
        `<a class="chip tag ${g}" href="#/tag/${encodeURIComponent(t.tag)}">${esc(t.tag)}<small>${fmt(t.count)}</small></a>`).join("")}</div>`)).join("");
  },

  async tag(params, name) {
    const tag = decodeURIComponent(name);
    const d = await api.get(`/api/tag/${encodeURIComponent(tag)}`);
    const lid = regList(d.songs);
    return head({
      eyebrow: "Tag", title: tag, sub: `<span class="mono">Top ${d.songs.length} songs, strongest first</span>`,
      artHtml: art(`tag${tag}`, initials(tag), { code: "TAG" }), actions: d.songs.length ? listActions(lid) : "",
    }) + (d.songs.length ? songTable(d.songs) : empty("Empty", "No song has this tag yet."));
  },

  async playlist(params, id) {
    const d = await api.get(`/api/playlists/${id}`);
    state.ctx = { playlist: Number(id) };
    const kinds = songFilters(d.songs, params);
    const lid = kinds.songs.length ? regList(kinds.songs) : null;
    return head({
      eyebrow: "Playlist", title: d.name, editable: true,
      sub: `<span class="mono">${d.songs.length} songs · ${Math.round(d.songs.reduce((a, s) => a + (s.dur || 0), 0) / 60)} min</span>${d.songs.length > 1 && !kinds.filtered ? '<span class="mono">Drag rows to reorder</span>' : ""}`,
      artHtml: art(`pl${d.id}${d.name}`, initials(d.name), { code: `PL-${pad(d.id, 3)}` }),
      actions: (lid ? listActions(lid, { save: false }) : "")
        + `<button class="btn ghost" data-rename="1">${ic("edit")}Rename</button><button class="btn ghost danger" data-delete-playlist="${d.id}">${ic("trash")}Delete</button>`,
    }) + kinds.html + (d.songs.length ? songTable(kinds.songs, { draggable: !kinds.filtered, sortable: false })
      : empty("Empty playlist", "Add songs from any song's ⋯ menu, or drag a song row onto this playlist in the sidebar."));
  },

  async artist(params, id) { return personPage(id, params); },

  async work(params, id) {
    const d = await api.get(`/api/work/${id}`);
    const lid = d.songs.length ? regList(d.songs) : null;
    const label = workLabel(d.kind);
    const sub = [d.category, d.year, d.score ? `★ ${d.score.toFixed(2)}` : "", d.vndbId].filter(Boolean).map((x) => `<span class="mono">${esc(x)}</span>`).join("")
      + [...(d.developers || []), ...(d.genres || [])].map((g) => `<span>${esc(g)}</span>`).join("");
    return head({
      eyebrow: label, title: d.name, sub, artHtml: art(d.id, initials(d.name), { code: `${d.kind === "vn" ? "VN" : "ANN"} ${d.id.slice(1)}` }),
      actions: lid ? listActions(lid) : "",
    }) + (d.nameJa && d.nameJa !== d.name ? `<p class="mono" style="margin-top:16px">${esc(d.nameJa)}</p>` : "")
      + (d.songs.length ? songTable(d.songs, { sortable: false }) : empty("No songs", `This ${label.toLowerCase()} has no songs in the library.`));
  },

  async options() {
    const st = state.stats || (await api.get("/api/home")).stats;
    const a = state.app = await api.get("/api/app");
    const lib = a.library || {};
    return head({ eyebrow: "System", title: "Options", sub: "<span>Likes, playlists and play history are stored on this computer in <b class=\"mono\">%APPDATA%\\MUMUsic\\user.db</b>, backed up to <b class=\"mono\">user.backup.db</b> every time the app starts.</span>" })
      + `<div class="stat-line" style="margin-top:22px"><span><b>${fmt(state.likes.size)}</b> liked</span><span><b>${fmt(state.playlists.length)}</b> playlists</span><span><b>${fmt(st.analysed)}</b> / ${fmt(st.songs)} analysed</span></div>
      <div class="data-grid">
        <div class="data-card"><div class="eyebrow">01 // Export</div><h3>Export</h3><p>Download all your likes and playlists as a JSON file. Songs are saved with their id, file name, title and artist so the file still works if the library changes.</p>
          <button class="btn primary" data-export>${ic("download")}Export JSON</button></div>
        <div class="data-card"><div class="eyebrow">02 // Import</div><h3>Import</h3><p>Load an export file. It is merged with what you have: likes are added, playlists with the same name get the missing songs, new playlists are created.</p>
          <button class="btn" data-import>${ic("upload")}Import JSON</button><input type="file" accept="application/json,.json" hidden id="importFile"></div>
        <div class="data-card"><div class="eyebrow">03 // Appearance</div><h3>Theme</h3><p>Switch between light and dark. By default it follows your system setting.</p>
          <div class="actions" style="margin-top:0"><button class="btn" data-theme-set="light">${ic("sun")}Light</button><button class="btn" data-theme-set="dark">${ic("moon")}Dark</button><button class="btn ghost" data-theme-set="">System</button></div></div>
        <div class="data-card"><div class="eyebrow">04 // About</div><h3>MUMUsic ${esc(a.version || "")}</h3><p>Library ${esc(lib.version || "?")}${lib.dev ? " (local build)" : ""}${lib.builtAt ? `, built ${esc(lib.builtAt.slice(0, 10))}` : ""}. Songs stream from animemusicquiz.com and erogemusicquiz.com.</p>
          ${a.online && !lib.dev ? `<button class="btn" data-check-updates>${ic("refresh")}Check for updates</button>` : ""}</div>
        ${songKindsCard(a)}
        ${discordCard(a.discord || {})}
        ${blurCard()}
      </div>`;
  },
};

async function personPage(id, params) {
  const d = await api.get(`/api/person/${id}`);
  const kind = d.kind;
  const tab = params.get("tab") || (d.songs.length ? "songs" : d.composed.length ? "composed" : d.arranged.length ? "arranged" : d.lyrics.length ? "lyrics" : "songs");
  const lists = { songs: d.songs, composed: d.composed, arranged: d.arranged, lyrics: d.lyrics };
  const all = lists[tab] || d.songs;
  const filters = songFilters(all, params);
  const songs = filters.songs;
  const lid = songs.length ? regList(songs) : null;
  let sub = `<span class="mono">${kind} · ${d.songs.length} songs</span>`;
  if (d.aliases.length) sub += `<span>Also credited as ${d.aliases.map(esc).join(", ")}</span>`;
  let html = head({
    eyebrow: kind === "group" ? "Group" : "Artist", title: d.name, sub,
    artHtml: art(id, initials(d.name), { person: true, code: `${kind === "group" ? "GRP" : "ART"} ${id.slice(1)}` }),
    actions: lid ? listActions(lid) : "",
  });
  if (d.members?.length) html += `<div class="section"><div class="section-head"><span class="num">//</span><h2>Members</h2><span class="rule"></span></div><div class="chips">${d.members.map((m) => `<a class="chip" href="#/artist/${m.id}">${esc(m.name)}</a>`).join("")}</div></div>`;
  if (d.groups?.length) html += `<div class="section"><div class="section-head"><span class="num">//</span><h2>${kind === "group" ? "Part of" : "Groups & units"}</h2><span class="rule"></span></div><div class="chips">${d.groups.map((g) => `<a class="chip" href="#/artist/${g.id}">${esc(g.name)}<small>${g.count}</small></a>`).join("")}</div></div>`;
  const tabs = [["songs", "Performed", d.songs.length], ["composed", "Composed", d.composed.length], ["arranged", "Arranged", d.arranged.length], ["lyrics", "Lyrics", d.lyrics.length]].filter(([, , n], i) => i === 0 || n);
  if (tabs.length > 1) html += `<div class="tabs">${tabs.map(([k, label, n]) => `<button class="${k === tab ? "active" : ""}" data-tab="${k}">${label}<small>${n}</small></button>`).join("")}</div>`;
  html += all.length ? `<div class="list-tools"><input class="filter" placeholder="Filter songs" data-filter><span class="mono">${tab === "songs" ? "Includes songs with their groups & units" : ""}</span></div>` + filters.html + songTable(songs)
    : empty("No songs", "Nothing here.");
  return html;
}

/* ================================================================== router */
let renderToken = 0;
async function render() {
  const token = ++renderToken;
  closeMenu();
  const raw = location.hash.slice(2) || "home";
  const [path, query = ""] = raw.split("?");
  const [name, ...args] = path.split("/");
  const params = new URLSearchParams(query);
  const page = pages[name] || pages.home;
  state.lists.clear();
  state.ctx = null;
  renderNav();
  if (name !== "search") $("#searchInput").value = "";
  else if (document.activeElement !== searchInput) searchInput.value = params.get("q") || "";
  document.querySelector(".app").classList.remove("nav-open");
  const keepScroll = view.dataset.route === raw;
  if (!keepScroll) view.innerHTML = loading();
  try {
    const html = await page(params, ...args);
    if (token !== renderToken) return;
    const top = view.scrollTop;
    view.innerHTML = html;
    view.dataset.route = raw;
    view.scrollTop = keepScroll ? top : 0;
    markCurrent();
  } catch (e) {
    if (token !== renderToken) return;
    view.innerHTML = empty("Error", esc(e.message));
  }
}
addEventListener("hashchange", render);

function updateMeta() {
  const st = state.stats;
  $("#topMeta").innerHTML = st ? `<b>${fmt(st.analysed)}</b> / ${fmt(st.songs)} analysed` : "";
  $("#brandSub").textContent = `${sourceName()} // ARCHIVE`;
}

/* search box */
const searchInput = $("#searchInput");
let searchTimer;
searchInput.addEventListener("input", () => {
  clearTimeout(searchTimer);
  const q = searchInput.value;
  if (SONG_LINK.test(q.trim())) return;
  searchTimer = setTimeout(() => {
    const target = q.trim() ? `#/search?q=${encodeURIComponent(q.trim())}` : "#/home";
    if (location.hash.startsWith("#/search") && q.trim()) history.replaceState(null, "", target), render();
    else location.hash = target;
  }, 260);
});
searchInput.addEventListener("keydown", async (e) => {
  if (e.key !== "Enter") return;
  const q = searchInput.value.trim();
  if (SONG_LINK.test(q)) {
    try {
      const { id } = await api.get(`/api/resolve?q=${encodeURIComponent(q)}`);
      openSong(id);
      searchInput.value = "";
    } catch { toast("That link isn't in the library", { error: true }); }
  }
});
searchInput.addEventListener("paste", () => setTimeout(() => {
  if (SONG_LINK.test(searchInput.value.trim())) searchInput.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter" }));
}, 0));

/* ================================================================== player */
async function ensureSongs(ids) {
  const missing = ids.filter((id) => !state.cache.has(id));
  if (missing.length) remember((await api.post("/api/songs", { ids: missing })).songs);
}

async function playList(ids, start = 0, { shuffle = state.shuffle, radioSeed = null } = {}) {
  if (!ids.length) return;
  state.queue = ids.slice();
  state.original = null;
  state.radio = { on: !!radioSeed, seed: radioSeed };
  state.shuffle = shuffle;
  if (shuffle) applyShuffle(start); else state.qi = start;
  await playIndex(state.qi);
}
function applyShuffle(startIndex = state.qi) {
  state.original = state.queue.slice();
  const first = state.queue[startIndex];
  const rest = state.queue.filter((_, i) => i !== startIndex);
  for (let i = rest.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i + 1)); [rest[i], rest[j]] = [rest[j], rest[i]]; }
  state.queue = [first, ...rest];
  state.qi = 0;
}

let failStreak = 0;
async function playIndex(i) {
  if (i < 0 || i >= state.queue.length) return;
  state.qi = i;
  const id = state.queue[i];
  await ensureSongs([id]);
  const s = state.cache.get(id);
  if (!s?.audio) {
    toast(`“${s?.name || id}” has no audio file, skipping`, { error: true });
    return next(true);
  }
  audio.src = s.audio;
  applyVolume();
  try { await audio.play(); failStreak = 0; } catch (e) { if (e.name !== "AbortError") console.warn(e); }
  state.history = [...state.history.filter((h) => h !== id), id].slice(-800);
  store.set("history", state.history);
  api.post("/api/played", { id }).catch(() => {});
  renderPlayer();
  markCurrent();
  saveSession();
  setMediaSession(s);
  if (state.panelTab === "queue" && !$("#panel").hidden) renderQueue();
  if (state.radio.on && state.qi >= state.queue.length - 1) prefetchRadio();
}

let radioBusy = null;
function prefetchRadio() {
  if (radioBusy) return radioBusy;
  const cur = state.queue[state.qi];
  radioBusy = api.post("/api/radio/next", {
    seed: state.radio.seed ?? cur, current: state.queue[state.queue.length - 1] ?? cur,
    history: [...new Set([...state.history, ...state.queue])].slice(-600),
    mode: state.discoveryMode,
    skipped: state.radio.skipped || [],
    liked: state.queue.filter((id) => state.likes.has(id)),
  }).then((s) => {
    remember([s]);
    state.queue.push(s.id);
    state.lastRadioPick = s.id;
    if (state.original) state.original.push(s.id);
    renderPlayer();
    if (state.panelTab === "queue" && !$("#panel").hidden) renderQueue();
    saveSession();
    return s;
  }).catch((e) => { toast(`Discovery: ${e.message}`, { error: true }); return null; })
    .finally(() => { radioBusy = null; });
  return radioBusy;
}

async function next(auto = false) {
  // a discovery song skipped in its first 30 s teaches the run what to avoid
  if (!auto && state.radio.on && audio.currentTime < 30 && current()) {
    state.radio.skipped = [...(state.radio.skipped || []), current().id].slice(-12);
    if (state.queue.at(-1) === state.lastRadioPick && state.queue.length - 1 > state.qi + 1 && !radioBusy) {
      // the song after next was picked before this skip: re-pick it with the new information
      state.queue.pop();
    }
  }
  if (auto && state.repeat === "one" && current()?.audio) { audio.currentTime = 0; audio.play(); return; }
  if (state.qi < state.queue.length - 1) return playIndex(state.qi + 1);
  if (state.radio.on) {
    const s = await prefetchRadio();
    if (s && state.qi < state.queue.length - 1) return playIndex(state.qi + 1);
    return;
  }
  if (state.repeat === "all" && state.queue.length) return playIndex(0);
  audio.pause();
  renderPlayer();
}
function prev() {
  if (audio.currentTime > 3 || state.qi <= 0) { audio.currentTime = 0; return; }
  playIndex(state.qi - 1);
}
function togglePlay() {
  if (!state.queue.length) { startDiscovery(); return; }
  if (!audio.src) { playIndex(state.qi); return; }
  audio.paused ? audio.play() : audio.pause();
}
function toggleShuffle() {
  state.shuffle = !state.shuffle;
  if (state.queue.length) {
    if (state.shuffle) applyShuffle();
    else if (state.original) {
      const cur = state.queue[state.qi];
      state.queue = state.original;
      state.qi = Math.max(0, state.queue.indexOf(cur));
      state.original = null;
    }
  }
  store.set("shuffle", state.shuffle);
  renderPlayer();
  if (state.panelTab === "queue" && !$("#panel").hidden) renderQueue();
  saveSession();
}
function cycleRepeat() {
  state.repeat = { off: "all", all: "one", one: "off" }[state.repeat];
  store.set("repeat", state.repeat);
  renderPlayer();
}
function toggleRadio() {
  const cur = state.queue[state.qi];
  if (!state.radio.on && !cur) { startDiscovery(); return; }
  state.radio = state.radio.on ? { on: false, seed: null } : { on: true, seed: cur };
  toast(state.radio.on ? "Discovery on — similar songs will keep playing after the queue" : "Discovery off");
  if (state.radio.on) {
    // drop the rest of the queue so discovery starts right after the current song
    state.queue = state.queue.slice(0, state.qi + 1);
    state.original = null;
    prefetchRadio();
  }
  renderPlayer();
  saveSession();
}
async function startRadio(id) {
  await ensureSongs([id]);
  await playList([id], 0, { shuffle: false, radioSeed: id });
  toast(`Discovery started from “${state.cache.get(id)?.name}”`);
}
async function startDiscovery() {
  let pool = [...state.likes];
  if (!pool.length) {
    const d = await api.get("/api/home");
    pool = d.discover.map((s) => s.id);
    remember(d.discover);
  }
  startRadio(pool[Math.floor(Math.random() * pool.length)]);
}
function playNext(id) {
  if (!state.queue.length) return playList([id]);
  state.queue.splice(state.qi + 1, 0, id);
  if (state.original) state.original.push(id);
  toast("Playing next");
  renderPlayer(); saveSession();
  if (state.panelTab === "queue" && !$("#panel").hidden) renderQueue();
}
function addToQueue(ids) {
  if (!state.queue.length) return playList(ids);
  const insertAt = state.radio.on ? state.qi + 1 : state.queue.length;
  state.queue.splice(insertAt, 0, ...ids);
  if (state.original) state.original.push(...ids);
  toast(`Added ${ids.length > 1 ? `${ids.length} songs` : "1 song"} to the queue`);
  renderPlayer(); saveSession();
  if (state.panelTab === "queue" && !$("#panel").hidden) renderQueue();
}

function saveSession() {
  store.set("session", {
    queue: state.queue.slice(-500), qi: Math.min(state.qi, 499), time: audio.currentTime || 0,
    radio: state.radio, original: state.original ? state.original.slice(-500) : null,
  });
}
async function restoreSession() {
  const s = store.get("session", null);
  if (!s || !s.queue?.length) return;
  s.queue = s.queue.filter((id) => typeof id === "string");
  try { await ensureSongs(s.queue); } catch { return; }
  state.queue = s.queue.filter((id) => state.cache.has(id));
  if (!state.queue.length) return;
  state.qi = Math.min(s.qi, state.queue.length - 1);
  state.radio = s.radio || { on: false, seed: null };
  state.original = s.original;
  const song = current();
  if (song?.audio) {
    audio.src = song.audio;
    applyVolume();
    audio.addEventListener("loadedmetadata", () => { if (s.time) audio.currentTime = s.time; }, { once: true });
    setMediaSession(song);
  }
  renderPlayer();
}

function setMediaSession(s) {
  if (!("mediaSession" in navigator) || !s) return;
  navigator.mediaSession.metadata = new MediaMetadata({ title: s.name, artist: s.artist, album: s.work, artwork: [{ src: "/icon.png", sizes: "128x128", type: "image/png" }] });
  document.title = `${s.name} — ${s.artist}`;
}
if ("mediaSession" in navigator) {
  navigator.mediaSession.setActionHandler("play", () => audio.play());
  navigator.mediaSession.setActionHandler("pause", () => audio.pause());
  navigator.mediaSession.setActionHandler("nexttrack", () => next());
  navigator.mediaSession.setActionHandler("previoustrack", () => prev());
}

/* player bar */
function renderPlayer() {
  const s = current();
  const p = $("#player");
  const playing = !audio.paused;
  const vol = state.muted ? 0 : state.volume, volPos = volumeToPos(vol);
  p.innerHTML = `
    <div class="np">${s ? `<button class="np-cover" data-pc="stage" title="${stage.hidden ? "Open the big view with the clip" : "Back to the library (Esc)"}">${songArt(s, stage.hidden ? "" : "on")}</button><div class="np-text"><div class="t" data-song="${s.id}">${esc(s.name)}</div>
      <div class="s">${byLink(s)} · <a href="#/work/${s.workId}">${esc(s.work)}</a></div></div>
      <button class="icon-btn ${state.likes.has(s.id) ? "liked" : ""}" data-like="${s.id}" title="Like">${ic("heart")}</button>`
      : `<div class="np-idle">// Idle — pick a song or start discovery</div>`}</div>
    <div class="controls">
      <div class="buttons">
        <button class="icon-btn ${state.shuffle ? "on" : ""}" data-pc="shuffle" title="Shuffle">${ic("shuffle")}</button>
        <button class="icon-btn" data-pc="prev" title="Previous">${ic("prev")}</button>
        <button class="play-btn" data-pc="play" title="${playing ? "Pause" : "Play"} (Space)">${ic(playing ? "pause" : "play")}</button>
        <button class="icon-btn" data-pc="next" title="Next">${ic("next")}</button>
        <button class="icon-btn ${state.repeat !== "off" ? "on" : ""}" data-pc="repeat" title="Repeat: ${state.repeat}">${ic(state.repeat === "one" ? "repeat1" : "repeat")}</button>
      </div>
      <div class="seek"><span id="tCur">${dur(audio.currentTime)}</span>
        <div class="range" id="seekBar"><span class="track"></span><span class="buf" id="seekBuf"></span><span class="fill" id="seekFill"></span><span class="knob" id="seekKnob"></span></div>
        <span id="tDur">${dur(audio.duration || s?.dur)}</span></div>
    </div>
    <div class="right-ctl">
      <button class="disco ${state.radio.on ? "on" : ""}" data-pc="radio" title="Discovery: keep playing similar songs, never repeating"><i></i><span>Discovery</span></button>
      <button class="icon-btn" data-pc="queue" title="Queue">${ic("queue")}</button>
      <button class="icon-btn ${state.level ? "on" : ""}" data-pc="level" title="Volume levelling: ${state.level ? "on, every song plays at the same loudness" : "off"}">${ic("level")}</button>
      <button class="icon-btn vol-ico" data-pc="mute" title="Mute">${ic(vol ? "volume" : "mute")}</button>
      <div class="range volume" id="volBar"><span class="track"></span><span class="fill" style="width:${volPos * 100}%"></span><span class="knob" style="left:${volPos * 100}%"></span></div>
    </div>`;
  updateProgress();
  renderStage();
}
function updateProgress() {
  const d = audio.duration || current()?.dur || 0;
  const pct = d ? (audio.currentTime / d) * 100 : 0;
  const fill = $("#seekFill"), knob = $("#seekKnob");
  if (!fill) return;
  fill.style.width = `${pct}%`;
  knob.style.left = `${pct}%`;
  $("#tCur").textContent = dur(audio.currentTime);
  $("#tDur").textContent = dur(d);
  if (audio.buffered.length && d) $("#seekBuf").style.width = `${(audio.buffered.end(audio.buffered.length - 1) / d) * 100}%`;
}
let lastSave = 0;
audio.addEventListener("timeupdate", () => {
  updateProgress();
  if (Date.now() - lastSave > 5000) { lastSave = Date.now(); saveSession(); }
});
audio.addEventListener("play", () => { renderPlayer(); markCurrent(); });
audio.addEventListener("pause", () => { renderPlayer(); markCurrent(); });
audio.addEventListener("ended", () => next(true));
audio.addEventListener("loadedmetadata", updateProgress);
audio.addEventListener("error", () => {
  if (!audio.src || !current()) return;
  failStreak++;
  toast(`Couldn't load “${current()?.name}”${failStreak < 4 ? ", skipping" : ""}`, { error: true });
  if (failStreak < 4) setTimeout(() => next(true), 900);
});
applyVolume();

// Discord status: the host turns this into "Listening to MUMUsic" with a progress bar and a "Listen on MUMUsic"
// button for friends (Integrations/DiscordPresence.cs)
function reportPresence() {
  const s = current();
  const song = s && audio.src ? { id: s.id, name: s.name, artist: s.artist, work: s.work, workKind: s.workKind, type: s.type,
    position: audio.currentTime, duration: audio.duration || s.dur, playing: !audio.paused } : null;
  api.post("/api/presence", { song }).catch(() => {});
}
for (const ev of ["play", "pause", "seeked", "loadedmetadata"]) audio.addEventListener(ev, reportPresence);

function dragRange(el, onValue) {
  const set = (e) => {
    const r = el.getBoundingClientRect();
    onValue(Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)));
  };
  el.classList.add("drag");
  return set;
}
$("#player").addEventListener("pointerdown", (e) => {
  const bar = e.target.closest("#seekBar, #volBar");
  if (!bar) return;
  e.preventDefault();
  const isSeek = bar.id === "seekBar";
  const apply = (v) => {
    if (isSeek) {
      const d = audio.duration || 0;
      if (d) audio.currentTime = v * d;
      updateProgress();
    } else setVolumePos(v);
  };
  const set = dragRange(bar, apply);
  set(e);
  const move = (ev) => set(ev);
  const up = () => { removeEventListener("pointermove", move); removeEventListener("pointerup", up); bar.classList.remove("drag"); if (!isSeek) renderPlayer(); };
  addEventListener("pointermove", move);
  addEventListener("pointerup", up);
});
function setVolumePos(pos) {
  state.volume = posToVolume(pos); state.muted = false; applyVolume(); store.set("volume", state.volume);
  const bar = $("#volBar");
  if (bar) { $(".fill", bar).style.width = `${pos * 100}%`; $(".knob", bar).style.left = `${pos * 100}%`; }
  const icon = $("#player .vol-ico");
  if (icon) icon.innerHTML = ic(pos ? "volume" : "mute");
}
// Scrolling over the volume icon or slider: 5% of the slider per wheel notch
$("#player").addEventListener("wheel", (e) => {
  if (!e.target.closest("#volBar, .vol-ico") || !e.deltaY) return;
  e.preventDefault();
  const pos = state.muted ? 0 : volumeToPos(state.volume);
  setVolumePos(Math.min(1, Math.max(0, Math.round((pos - Math.sign(e.deltaY) * 0.05) * 100) / 100)));
}, { passive: false });
$("#player").addEventListener("click", (e) => {
  const b = e.target.closest("[data-pc]");
  if (!b) return;
  const a = b.dataset.pc;
  if (a === "play") togglePlay();
  else if (a === "next") next();
  else if (a === "prev") prev();
  else if (a === "shuffle") toggleShuffle();
  else if (a === "repeat") cycleRepeat();
  else if (a === "radio") toggleRadio();
  else if (a === "queue") togglePanel("queue");
  else if (a === "stage") toggleStage();
  else if (a === "mute") { state.muted = !state.muted; applyVolume(); renderPlayer(); }
  else if (a === "level") {
    state.level = !state.level; store.set("level", state.level); applyVolume(); renderPlayer();
    toast(state.level ? "Volume levelling on: every song plays at the same loudness" : "Volume levelling off");
  }
});

/* ================================================================== now-playing stage (big view + clip) */
// The clip plays muted and follows the <audio> element, so discovery, queue and controls keep working as usual.
// One <video> element is reused for every song and emptied on close: creating a new one per song leaks media
// players until garbage collection, and once Chrome hits its player limit it pauses / refuses to load the music too.
let stageSong = null, stageToken = 0, lastHardSync = 0;
// Nothing is blurred unless the user asks: per clip ({songId: true | false}, kept when the song comes back), per anime /
// VN picture for songs without a clip ({workId: ...}), or for every 18+ work (option). A choice beats the option.
// The older "blurredSongs" / "artBlur" choices are dropped: pictures used to start blurred from VNDB's image rating, so
// what they held looked random.
for (const old of ["blurredSongs", "artBlur"]) { try { localStorage.removeItem(old); } catch { /* storage blocked */ } }
const clipBlur = store.get("clipBlur", {});
const artBlur = store.get("workBlur", {});
let autoBlurAdult = store.get("autoBlurAdult", false);
const workAdult = new Map();          // workId -> 18+ (from /api/work/<id>/art)
const clipBlurred = (s) => clipBlur[s.id] ?? (autoBlurAdult && workAdult.get(s.workId) !== false);
const artBlurred = (s) => artBlur[s.workId] ?? (autoBlurAdult && workAdult.get(s.workId) === true);
// The anime / VN picture and whether the work is 18+; the host caches both after the first time.
async function workArt(workId) {
  const art = await api.get(`/api/work/${workId}/art`);
  workAdult.set(workId, !!art.adult);
  return art;
}
function setAutoBlur(on) {
  autoBlurAdult = on;
  store.set("autoBlurAdult", on);
  stageSong = null;
  renderStage();
  render();
}
const stageVideo = document.createElement("video");
stageVideo.id = "stageVideo";
stageVideo.muted = stageVideo.defaultMuted = true;
stageVideo.playsInline = true;
stageVideo.preload = "auto";
stageVideo.addEventListener("loadedmetadata", () => syncStageVideo(true));
stageVideo.addEventListener("playing", () => stageVideo.parentElement?.classList.add("live"));
stageVideo.addEventListener("error", () => {
  if (!stageVideo.getAttribute("src")) return;
  const screen = stageVideo.parentElement;
  screen?.classList.remove("has-video", "live");
  releaseStageVideo();
  if (!screen || !current()) return;
  screen.classList.remove("blurred");
  $("[data-stage-blur]", stage)?.remove();
  showStageCover(current(), stageToken);      // the clip is gone: the picture instead
});
function releaseStageVideo() {
  stageVideo.remove();
  if (!stageVideo.getAttribute("src")) return;
  stageVideo.pause();
  stageVideo.removeAttribute("src");
  stageVideo.load();            // aborts the download and frees the decoder
}
function toggleStage() { stage.hidden ? openStage() : closeStage(); }
function openStage() {
  if (!current()) return;
  stage.hidden = false;
  stageSong = null;
  renderStage();
  $("#player .np .art")?.classList.add("on");
}
function closeStage() {
  if (stage.hidden) return;
  stage.hidden = true;
  stageSong = null;
  releaseStageVideo();
  stage.innerHTML = "";
  $("#player .np .art")?.classList.remove("on");
}
function renderStage() {
  if (stage.hidden) return;
  const s = current();
  if (!s) return closeStage();
  if (stageSong === s.id) { syncStageVideo(); renderStageNext(); return; }
  stageSong = s.id;
  const token = ++stageToken;
  const mode = state.radio.on ? `Discovery · ${state.discoveryMode === "explore" ? "explore" : "close vibe"}` : "Now playing";
  stage.innerHTML = `
    <div class="stage-top">
      <div class="eyebrow">${esc(mode)}</div>
      <button class="stage-close" data-stage-close title="Back (Esc)">${ic("close")}<span>Close</span><kbd>Esc</kbd></button>
    </div>
    <div class="stage-body">
      <div class="stage-screen ${s.video ? "has-video" : ""} ${s.video && clipBlurred(s) ? "blurred" : ""}">
        ${songArt(s, "stage-art")}
      </div>
      <div class="stage-info">
        <div class="eyebrow">${esc(s.type || "Song")}${s.year ? ` // ${s.year}` : ""} // ${workLabel(s.workKind)}</div>
        <h1 class="stage-title">${esc(s.name)}</h1>
        <div class="stage-artist">${byLink(s)}</div>
        <a class="stage-work" href="#/work/${s.workId}">${esc(s.work)}</a>
        ${s.tags?.length ? tagChips(s.tags) : ""}
        <dl class="kv stage-kv" id="stageKv"></dl>
        <div class="stage-actions">
          <button class="icon-btn ${state.likes.has(s.id) ? "liked" : ""}" data-like="${s.id}" title="Like">${ic("heart")}</button>
          ${state.radio.on ? "" : `<button class="btn" data-radio="${s.id}" title="Keep playing songs that sound like this one">${ic("radio")}Discover</button>`}
          <button class="btn ghost" data-stage-details="${s.id}">${ic("info")}Details</button>
          ${s.video ? `<button class="btn ghost" data-stage-blur title="Blur this clip, also the next times it plays (B)">${blurLabel(s)}</button>` : ""}
        </div>
        <div class="stage-next" id="stageNext"></div>
      </div>
    </div>`;
  if (s.video) {
    const url = s.video;
    if (stageVideo.getAttribute("src") !== url) stageVideo.setAttribute("src", url);   // swaps the clip, same player
    $(".stage-screen", stage).appendChild(stageVideo);
    if (stageVideo.readyState >= 3) syncStageVideo(true);
    // With the 18+ option, a clip of a work not checked yet starts blurred until the host has the answer.
    if (autoBlurAdult && clipBlur[s.id] == null && !workAdult.has(s.workId)) {
      workArt(s.workId).catch(() => workAdult.set(s.workId, false)).then(() => {
        if (token !== stageToken) return;
        $(".stage-screen", stage)?.classList.toggle("blurred", clipBlurred(s));
        const btn = $("[data-stage-blur]", stage);
        if (btn) btn.innerHTML = blurLabel(s);
      });
    }
  } else {
    releaseStageVideo();
    showStageCover(s, token);
  }
  renderStageNext();
  api.get(`/api/song/${s.id}`).then((d) => {
    if (token !== stageToken) return;
    const rows = [];
    if (d.features) rows.push(["Tempo", `${d.features.bpm} BPM · ${esc(d.features.key)}`]);
    for (const c of d.credits) rows.push([c.role.replace(" by", ""), `<a href="#/artist/${c.id}">${esc(c.name)}</a>`]);
    $("#stageKv").innerHTML = rows.map(([k, v]) => `<dt>${esc(k)}</dt><dd>${v}</dd>`).join("");
  }).catch(() => {});
}
const blurLabel = (s) => clipBlurred(s) ? `${ic("eye")}Show clip` : `${ic("eyeoff")}Blur clip`;
const artBlurLabel = (blurred) => blurred ? `${ic("eye")}Show picture` : `${ic("eyeoff")}Blur picture`;
// The anime / VN picture behind a song without a clip, once the host has found it (cached after the first time).
async function showStageCover(s, token) {
  let art;
  try { art = await workArt(s.workId); } catch { return; }
  const screen = $(".stage-screen", stage);
  if (!art.url || token !== stageToken || !screen || screen.classList.contains("has-video")) return;
  const blurred = artBlurred(s);
  const url = esc(art.url);
  screen.insertAdjacentHTML("beforeend", `<div class="stage-cover"><img class="backdrop" src="${url}" alt=""><img class="picture" src="${url}" alt=""></div>`);
  screen.classList.add("has-cover");
  screen.classList.toggle("blurred", blurred);
  const img = $(".stage-cover .picture", screen);
  img.addEventListener("load", () => screen.classList.add("cover-live"), { once: true });
  img.addEventListener("error", () => screen.classList.remove("has-cover", "cover-live", "blurred"), { once: true });
  $(".stage-actions", stage)?.insertAdjacentHTML("beforeend",
    `<button class="btn ghost" data-stage-blur title="Blur this picture, for every song of this ${workLabel(s.workKind).toLowerCase()} (B)">${artBlurLabel(blurred)}</button>`);
}
function toggleBlur() {
  const s = current();
  const screen = $(".stage-screen.has-video, .stage-screen.has-cover", stage);
  if (stage.hidden || !s || !screen) return;
  const btn = $("[data-stage-blur]", stage);
  if (screen.classList.contains("has-video")) {
    clipBlur[s.id] = !clipBlurred(s);
    store.set("clipBlur", clipBlur);
    screen.classList.toggle("blurred", clipBlur[s.id]);
    if (btn) btn.innerHTML = blurLabel(s);
    return;
  }
  artBlur[s.workId] = !artBlurred(s);
  store.set("workBlur", artBlur);
  screen.classList.toggle("blurred", artBlur[s.workId]);
  if (btn) btn.innerHTML = artBlurLabel(artBlur[s.workId]);
}
function renderStageNext() {
  const el = $("#stageNext");
  if (!el) return;
  const n = state.cache.get(state.queue[state.qi + 1]);
  el.innerHTML = n ? `<span class="mono">Up next</span><button data-stage-skip>${esc(n.name)} <small>${esc(n.artist)}</small></button>`
    : state.radio.on ? `<span class="mono">Up next</span><span class="muted">finding a close match…</span>` : "";
}
function syncStageVideo(hard = false) {
  const v = stageVideo;
  if (!v.isConnected || stage.hidden || v.readyState < 1 || (v.seeking && !hard)) return;
  // Some EMQ clips are shorter than their mp3: loop the whole clip until the song ends. Without this, every resync
  // seeked past the end and the clip replayed its first two seconds over and over.
  const len = v.duration, t = audio.currentTime;
  const loops = Number.isFinite(len) && len > 0 && audio.duration > len + 1;
  v.loop = loops;
  if (!loops && Number.isFinite(len) && t >= len) return;       // a fraction of a second longer: hold the last frame
  const target = loops ? t % len : t;
  let d = target - v.currentTime;
  if (loops && Math.abs(d) > len / 2) d -= Math.sign(d) * len;   // around the loop point the clip is on time
  // a clip that buffers slower than the music would otherwise be re-seeked on every tick (new range request each time)
  if (hard || (Math.abs(d) > 0.6 && Date.now() - lastHardSync > 3000)) {
    v.currentTime = target; v.playbackRate = 1; lastHardSync = Date.now();
  }
  else v.playbackRate = Math.abs(d) > 0.06 ? 1 + Math.max(-0.1, Math.min(0.1, d)) : 1;   // drift back smoothly
  if (audio.paused) { if (!v.paused) v.pause(); }
  else if (v.paused) v.play().catch(() => {});
}
audio.addEventListener("timeupdate", () => syncStageVideo());
audio.addEventListener("seeked", () => syncStageVideo(true));
stage.addEventListener("click", (e) => {
  const t = e.target;
  if (t.closest("[data-stage-close]")) { closeStage(); return; }
  if (t.closest("[data-stage-skip]")) { next(); return; }
  if (t.closest("[data-stage-blur]")) { toggleBlur(); return; }
  const d = t.closest("[data-stage-details]");
  if (d) { closeStage(); openSong(d.dataset.stageDetails); return; }
  if (t.closest("a[href^='#/']")) closeStage();       // going to an artist / anime / VN / tag page
});

/* ================================================================== side panel: song details & queue */
const panel = $("#panel");
function togglePanel(tab) {
  if (!panel.hidden && state.panelTab === tab) { panel.hidden = true; return; }
  state.panelTab = tab;
  panel.hidden = false;
  tab === "queue" ? renderQueue() : renderSongPanel(state.panelSong ?? state.queue[state.qi]);
}
function openSong(id) {
  state.panelSong = id;
  state.panelTab = "song";
  panel.hidden = false;
  renderSongPanel(id);
}
function panelHead() {
  return `<div class="panel-head"><div class="panel-tabs">
      <button class="${state.panelTab === "song" ? "active" : ""}" data-ptab="song">Song</button>
      <button class="${state.panelTab === "queue" ? "active" : ""}" data-ptab="queue">Queue</button></div>
    <button class="icon-btn sm" data-close-panel title="Close">${ic("close")}</button></div>`;
}
function miniRow(s, { qi, match, remove } = {}) {
  return `<div class="mini ${qi === state.qi ? "current" : ""}" ${qi !== undefined ? `data-qi="${qi}"` : `data-mini="${s.id}"`}>
    ${songArt(s)}<div style="min-width:0"><div class="t">${esc(s.name)}</div><div class="s">${esc(s.artist)} · ${esc(s.work)}</div></div>
    ${match ? `<span class="match">${Math.round(s.match * 100)}%</span>` : ""}
    ${remove ? `<button class="icon-btn" data-qremove="${qi}" title="Remove">${ic("close")}</button>` : ""}
</div>`;
}

let panelToken = 0;
async function renderSongPanel(id) {
  const token = ++panelToken;
  if (!id) {
    panel.innerHTML = panelHead() + `<div class="panel-body">${empty("No song", "Click a song title to see its tags and similar songs.")}</div>`;
    return;
  }
  panel.innerHTML = panelHead() + `<div class="panel-body">${loading()}</div>`;
  let d;
  try { d = await api.get(`/api/song/${id}`); } catch (e) { panel.querySelector(".panel-body").innerHTML = empty("Error", esc(e.message)); return; }
  if (token !== panelToken) return;
  remember([d]);
  if (d.liked) state.likes.add(id);
  const groups = {};
  for (const t of d.fullTags) (groups[t.group] ||= []).push(t);
  const tagHtml = Object.keys(groups).length
    ? `<div class="tagbars">${["genre", "mood", "vocals", "instruments", "tempo"].filter((g) => groups[g]).map((g) => `<div><h4>${g}</h4>${groups[g].map((t) =>
      `<div class="tagbar"><a href="#/tag/${encodeURIComponent(t.tag)}">${esc(t.tag)}</a><span>${Math.round(t.score * 100)}%</span><div class="meter"><i style="width:${t.score * 100}%"></i></div></div>`).join("")}</div>`).join("")}</div>`
    : `<div class="notice">Not analysed yet: this song has no tags or similar songs in this version of the library.</div>`;
  const credits = d.credits.map((c) => `<dt>${esc(c.role.replace(" by", ""))}</dt><dd class="credits"><a href="#/artist/${c.id}">${esc(c.name)}</a></dd>`).join("");
  panel.innerHTML = panelHead() + `<div class="panel-body">
    ${songArt(d, "detail-art")}
    <div class="eyebrow">${esc(d.type || "Song")}${d.year ? ` // ${d.year}` : ""} // ${workLabel(d.workKind)}</div>
    <div class="detail-title">${esc(d.name)}</div>
    <div class="detail-artist">${byLink(d)}</div>
    <div class="detail-actions">
      <button class="btn primary" data-play-one="${d.id}">${ic("play")}Play</button>
      <button class="btn" data-radio="${d.id}" title="Play this, then keep discovering similar songs">${ic("radio")}Discover</button>
      <button class="icon-btn ${state.likes.has(d.id) ? "liked" : ""}" data-like="${d.id}" title="Like">${ic("heart")}</button>
      <button class="icon-btn" data-more="${d.id}" title="More">${ic("more")}</button>
    </div>
    <dl class="kv"><dt>${workLabel(d.workKind)}</dt><dd><a href="#/work/${d.workId}">${esc(d.work)}</a></dd>${credits}
      <dt>Length</dt><dd class="mono" style="color:var(--ink)">${dur(d.dur)}</dd>
      ${d.features ? `<dt>Tempo</dt><dd class="mono" style="color:var(--ink)">${d.features.bpm} BPM · ${esc(d.features.key)}</dd>` : ""}
      ${d.link ? `<dt>Link</dt><dd><a class="mono" style="color:var(--ink-2);word-break:break-all" href="${esc(d.link)}" data-external>${esc(d.file)}</a></dd>` : ""}</dl>
    ${tagHtml}
    ${d.features ? `<div class="tagbars"><div><h4>sound</h4>${[["loudness", "loud"], ["brightness", "bright"], ["punch", "punchy"]].map(([k, label]) =>
      `<div class="tagbar"><div>${label}</div><span>${Math.round(d.features[k] * 100)}%</span><div class="meter"><i style="width:${d.features[k] * 100}%"></i></div></div>`).join("")}</div></div>` : ""}
    <div class="queue-label" style="margin-top:26px"><span>Similar songs</span><span></span></div>
    <div class="mini-list" id="similarList">${loading("Finding similar songs")}</div>
  </div>`;
  try {
    const sim = await api.get(`/api/song/${id}/similar?n=25`);
    if (token !== panelToken) return;
    remember(sim.songs);
    const lid = regList(sim.songs);
    $("#similarList").innerHTML = sim.songs.length
      ? sim.songs.map((s, i) => miniRow(s, { match: true }).replace('class="mini', `data-play="${lid}:${i}" class="mini`)).join("")
      : `<div class="notice">No similar songs available for this one.</div>`;
    $(".queue-label span:last-child", panel).textContent = sim.songs.length ? "click to play" : "";
  } catch (e) {
    if (token === panelToken) $("#similarList").innerHTML = `<div class="notice">${esc(e.message)}</div>`;
  }
}

function renderQueue() {
  const upcoming = state.queue.slice(state.qi + 1, state.qi + 151);
  const cur = current();
  panel.innerHTML = panelHead() + `<div class="panel-body">
    ${state.radio.on ? `<div class="notice" style="margin-bottom:18px"><b>Discovery is on.</b> When the queue runs out, a similar song you haven't heard this session is picked automatically.</div>` : ""}
    <div class="queue-label"><span>Discovery style</span></div>
    <div class="seg">
      <button class="${state.discoveryMode === "close" ? "active" : ""}" data-dmode="close"><b>Close vibe</b><small>Every pick is as close as possible to the first song</small></button>
      <button class="${state.discoveryMode === "explore" ? "active" : ""}" data-dmode="explore"><b>Explore</b><small>Drifts song to song, wanders further</small></button>
    </div>
    <div class="queue-label"><span>Now playing</span></div>
    ${cur ? miniRow(cur, { qi: state.qi }) : `<div class="notice">Nothing playing.</div>`}
    <div class="queue-label"><span>Next up · ${state.queue.length - state.qi - 1}</span>${upcoming.length ? `<button class="mono" data-qclear>Clear</button>` : ""}</div>
    <div class="mini-list">${upcoming.map((id, k) => { const s = state.cache.get(id); return s ? miniRow(s, { qi: state.qi + 1 + k, remove: true }) : ""; }).join("")
      || `<div class="notice">${state.radio.on ? "Finding the next song…" : "The queue is empty. Turn on Discovery to keep going with similar songs."}</div>`}</div>
  </div>`;
}

panel.addEventListener("click", (e) => {
  const t = e.target;
  if (t.closest("[data-close-panel]")) { panel.hidden = true; return; }
  const tab = t.closest("[data-ptab]");
  if (tab) { state.panelTab = tab.dataset.ptab; tab.dataset.ptab === "queue" ? renderQueue() : renderSongPanel(state.panelSong ?? state.queue[state.qi]); return; }
  const rm = t.closest("[data-qremove]");
  if (rm) {
    const qi = Number(rm.dataset.qremove);
    const [id] = state.queue.splice(qi, 1);
    if (state.original) state.original.splice(state.original.indexOf(id), 1);
    renderQueue(); renderPlayer(); saveSession();
    return;
  }
  const dm = t.closest("[data-dmode]");
  if (dm) {
    state.discoveryMode = dm.dataset.dmode;
    store.set("discoveryMode", state.discoveryMode);
    // re-pick an already prefetched discovery song with the new style
    if (state.radio.on && !radioBusy && state.queue.length - 1 > state.qi && state.queue.at(-1) === state.lastRadioPick) {
      state.queue.pop();
      if (state.original) state.original = state.original.filter((id) => id !== state.lastRadioPick);
      prefetchRadio();
    }
    renderQueue();
    return;
  }
  if (t.closest("[data-qclear]")) { state.queue = state.queue.slice(0, state.qi + 1); state.original = null; renderQueue(); saveSession(); return; }
  const q = t.closest("[data-qi]");
  if (q && !t.closest("button")) { playIndex(Number(q.dataset.qi)); }
});

/* ================================================================== song menu */
function songMenu(anchor, id) {
  const s = state.cache.get(id);
  const liked = state.likes.has(id);
  const items = [
    { icon: "play", text: "Play now", run: () => playList([id]) },
    { icon: "playnext", text: "Play next", run: () => playNext(id) },
    { icon: "queue", text: "Add to queue", run: () => addToQueue([id]) },
    { icon: "radio", text: "Start discovery from here", run: () => startRadio(id) },
    { icon: "info", text: "Tags & similar songs", run: () => openSong(id) },
    { icon: "heart", text: liked ? "Remove from liked" : "Like", run: () => toggleLike(id) },
    "hr",
    { label: "Add to playlist" },
    { group: [
      { icon: "plus", text: "New playlist…", run: async () => { const pid = await createPlaylist([id]); if (pid) location.hash = `#/playlist/${pid}`; } },
      ...state.playlists.map((p) => ({ icon: "list", text: p.name, run: () => addToPlaylist(p.id, [id]) })),
    ] },
    "hr",
  ];
  if (state.ctx?.playlist) items.push({ icon: "trash", text: "Remove from this playlist", run: async () => {
    await api.del(`/api/playlists/${state.ctx.playlist}/songs/${id}`);
    await loadPlaylists(); render();
    toast("Removed from playlist");
  } });
  if (s?.by) items.push({ icon: "user", text: `Go to ${s.artist}`.slice(0, 60), run: () => (location.hash = `#/artist/${s.by}`) });
  if (s) items.push({ icon: "tv", text: `Go to ${workLabel(s.workKind) === "VN" ? "VN" : "anime"}`, run: () => (location.hash = `#/work/${s.workId}`) });
  if (s?.link) items.push({ icon: "link", text: "Copy song link", run: () => navigator.clipboard.writeText(s.link).then(() => toast("Link copied")) });
  openMenu(anchor, items);
}

/* ================================================================== global click handling */
// Middle click on an anime / VN name opens its AniList / VNDB page instead of a new app window.
const WORK_LINK = 'a[href^="#/work/"]';
document.addEventListener("mousedown", (e) => { if (e.button === 1 && e.target.closest(WORK_LINK)) e.preventDefault(); });
document.addEventListener("auxclick", async (e) => {
  const a = e.button === 1 && e.target.closest(WORK_LINK);
  if (!a) return;
  e.preventDefault();
  try { openExternal((await api.get(`/api/work/${a.getAttribute("href").split("/")[2]}/site`)).url); }
  catch (err) { toast(err.message, { error: true }); }
});

document.addEventListener("click", async (e) => {
  const t = e.target;
  const el = (sel) => t.closest(sel);
  let x;
  if ((x = el("[data-external]"))) { e.preventDefault(); openExternal(x.href); return; }
  if ((x = el("[data-like]"))) { e.preventDefault(); toggleLike(x.dataset.like); return; }
  if ((x = el("[data-more]"))) { e.preventDefault(); songMenu(x, x.dataset.more); return; }
  if ((x = el("[data-play]"))) {
    e.preventDefault();
    const [lid, i] = x.dataset.play.split(":");
    const ids = state.lists.get(lid);
    if (ids) playList(ids, Number(i), { shuffle: false });
    return;
  }
  if ((x = el("[data-song]"))) { e.preventDefault(); openSong(x.dataset.song); return; }
  if ((x = el("[data-card]")) && !el("a")) { openSong(x.dataset.card); return; }
  if ((x = el("[data-play-one]"))) { playList([x.dataset.playOne]); return; }
  if ((x = el("[data-radio]"))) { startRadio(x.dataset.radio); return; }
  if ((x = el("[data-playall]"))) { const ids = state.lists.get(x.dataset.playall); if (ids) playList(ids, 0, { shuffle: false }); return; }
  if ((x = el("[data-shuffleall]"))) {
    const ids = state.lists.get(x.dataset.shuffleall);
    if (ids) playList(ids, Math.floor(Math.random() * ids.length), { shuffle: true });
    return;
  }
  if ((x = el("[data-radioall]"))) {
    const ids = state.lists.get(x.dataset.radioall) || [];
    const analysed = ids.filter((id) => state.cache.get(id)?.analysed);
    const pool = analysed.length ? analysed : ids;
    if (pool.length) startRadio(pool[Math.floor(Math.random() * Math.min(pool.length, 15))]);
    return;
  }
  if ((x = el("[data-saveall]"))) { const ids = state.lists.get(x.dataset.saveall); if (ids) { const pid = await createPlaylist(ids); if (pid) location.hash = `#/playlist/${pid}`; } return; }
  if (el("[data-start-disco]")) { startDiscovery(); return; }
  if (el("[data-reload]")) { e.preventDefault(); render(); return; }
  if ((x = el("[data-song-filter]"))) { toggleSongFilter(x.dataset.songFilter); return; }
  if ((x = el("[data-tab]"))) {
    const tab = x.dataset.tab;
    setHashParam("tab", (values) => { values.clear(); values.add(tab); });
    return;
  }
  if (el("[data-rename]") && state.ctx?.playlist) {
    const pid = state.ctx.playlist;
    const cur = state.playlists.find((p) => p.id === pid);
    const name = await modal({ title: "Rename playlist", eyebrow: "Playlist", input: cur?.name || "", confirm: "Save" });
    if (name) { await api.patch(`/api/playlists/${pid}`, { name }); await loadPlaylists(); render(); }
    return;
  }
  if ((x = el("[data-delete-playlist]"))) {
    const pid = Number(x.dataset.deletePlaylist);
    const p = state.playlists.find((q) => q.id === pid);
    const ok = await modal({ title: "Delete playlist?", eyebrow: "Playlist", body: `<p>“${esc(p?.name)}” and its ${p?.count ?? 0} songs will be removed. The songs stay in your library.</p>`, confirm: "Delete", danger: true });
    if (ok) { await api.del(`/api/playlists/${pid}`); await loadPlaylists(); location.hash = "#/home"; toast("Playlist deleted"); }
    return;
  }
  if (el("[data-check-updates]")) { checkUpdates(true); return; }
  if (el("[data-discord]")) { saveDiscord({ on: !state.app.discord.on }); return; }
  if (el("[data-auto-blur]")) { setAutoBlur(!autoBlurAdult); return; }
  if (el("[data-export]")) { exportData(); return; }
  if (el("[data-import]")) { $("#importFile").click(); return; }
  if ((x = el("[data-theme-set]"))) { setTheme(x.dataset.themeSet || null); return; }
  if ((x = el("[data-sort]"))) { sortTable(x); return; }
});
document.addEventListener("dblclick", (e) => {
  const row = e.target.closest("tr.row");
  if (!row || e.target.closest("button, a")) return;
  const ids = state.lists.get(row.dataset.list);
  if (ids) playList(ids, Number(row.dataset.i), { shuffle: false });
});
document.addEventListener("contextmenu", (e) => {
  const row = e.target.closest("tr.row, .card, .mini[data-play]");
  if (!row) return;
  e.preventDefault();
  const id = row.dataset.row || row.dataset.card || state.lists.get(row.dataset.play.split(":")[0])?.[Number(row.dataset.play.split(":")[1])];
  if (id) songMenu({ x: e.clientX, y: e.clientY }, id);
});
document.addEventListener("change", async (e) => {
  if (e.target.id !== "importFile" || !e.target.files[0]) return;
  try {
    const data = JSON.parse(await e.target.files[0].text());
    const r = await api.post("/api/import", data);
    await Promise.all([loadLikes(), loadPlaylists()]);
    toast(`Imported ${r.likes} likes, ${r.playlists} new playlists, ${r.songs} playlist songs${r.missing ? ` (${r.missing} not found)` : ""}`, { ms: 6000 });
    render();
  } catch (err) {
    toast(`Import failed: ${err.message}`, { error: true, ms: 6000 });
  }
  e.target.value = "";
});
document.addEventListener("input", (e) => {
  if (!e.target.matches("[data-filter]")) return;
  const q = e.target.value.toLowerCase();
  for (const row of $$("tr.row")) {
    const s = state.cache.get(row.dataset.row);
    const hay = `${s?.name} ${s?.artist} ${s?.work} ${(s?.tags || []).join(" ")}`.toLowerCase();
    row.hidden = q && !hay.includes(q);
  }
});

function sortTable(th) {
  const table = th.closest("table");
  const key = th.dataset.sort;
  const dir = th.dataset.dir === "asc" ? "desc" : "asc";
  $$("th", table).forEach((h) => { h.classList.remove("sorted"); delete h.dataset.dir; });
  th.classList.add("sorted");
  th.dataset.dir = dir;
  const lid = table.dataset.table;
  const ids = state.lists.get(lid).slice();
  const val = (id) => { const s = state.cache.get(id); return key === "dur" ? s.dur || 0 : String(s[key] || "").toLowerCase(); };
  ids.sort((a, b) => (val(a) < val(b) ? -1 : val(a) > val(b) ? 1 : 0) * (dir === "asc" ? 1 : -1));
  state.lists.set(lid, ids);
  const tbody = $("tbody", table);
  const rows = new Map($$("tr.row", tbody).map((r) => [r.dataset.row, r]));
  ids.forEach((id, i) => { const r = rows.get(id); r.dataset.i = i; $(".n", r).textContent = i + 1; $("[data-play]", r).dataset.play = `${lid}:${i}`; tbody.append(r); });
  th.textContent = th.textContent.replace(/ [↑↓]$/, "") + (dir === "asc" ? " ↑" : " ↓");
}

/* drag & drop: rows -> sidebar playlists, and reordering inside a playlist */
let dragId = null;
document.addEventListener("dragstart", (e) => {
  const row = e.target.closest?.("tr.row");
  if (!row) return;
  dragId = row.dataset.row;
  row.classList.add("dragging");
  e.dataTransfer.effectAllowed = "copyMove";
  e.dataTransfer.setData("text/plain", String(dragId));
});
document.addEventListener("dragend", () => {
  $$(".dragging, .drop-above, .drop").forEach((x) => x.classList.remove("dragging", "drop-above", "drop"));
  dragId = null;
});
document.addEventListener("dragover", (e) => {
  if (dragId === null) return;
  const pl = e.target.closest("[data-drop-playlist]");
  const row = e.target.closest("table[data-reorder] tr.row");
  if (!pl && !row) return;
  e.preventDefault();
  $$(".drop-above, .drop").forEach((x) => x.classList.remove("drop-above", "drop"));
  (pl || row).classList.add(pl ? "drop" : "drop-above");
});
document.addEventListener("drop", async (e) => {
  if (dragId === null) return;
  const pl = e.target.closest("[data-drop-playlist]");
  const row = e.target.closest("table[data-reorder] tr.row");
  if (!pl && !row) return;
  e.preventDefault();
  const id = dragId;
  if (pl) { addToPlaylist(Number(pl.dataset.dropPlaylist), [id]); return; }
  const tbody = row.parentElement;
  const moving = $(`tr.row[data-row="${id}"]`, tbody);
  if (!moving || moving === row) return;
  tbody.insertBefore(moving, row);
  const ids = $$("tr.row", tbody).map((r) => r.dataset.row);
  $$("tr.row", tbody).forEach((r, i) => { $(".n", r).textContent = i + 1; r.dataset.i = i; });
  state.lists.set(row.dataset.list, ids);
  $$("[data-play]", tbody).forEach((b, i) => (b.dataset.play = `${row.dataset.list}:${i}`));
  try { await api.put(`/api/playlists/${state.ctx.playlist}/order`, { ids }); } catch (err) { toast(`Could not save order: ${err.message}`, { error: true }); }
});

/* ================================================================== Discord status */
// Kinds of song the app picks by itself (discovery, recommendations, similar songs). Search and pages show them all.
const SONG_KINDS = [["amq", "Anime", [["OP", "Openings"], ["ED", "Endings"], ["INS", "Insert songs"]]],
                    ["emq", "Visual novels", [["OP", "Openings"], ["ED", "Endings"], ["INS", "Insert songs"],
                                              ["BGM", "BGM / soundtrack"], ["OTHER", "Untyped"]]]];
function songKindsCard(a) {
  const skip = new Set(a.skipKinds || []), counts = a.kindCounts || {};
  return `<div class="data-card"><div class="eyebrow">05 // Songs</div><h3>What plays</h3>
    <p>Kinds of song discovery, recommendations and similar songs pick from. Search, anime / VN pages and playlists still show everything.</p>
    ${SONG_KINDS.filter(([src]) => state.sources.includes(src)).map(([src, label, kinds]) => `<div class="kind-group"><span class="mono">${label}</span>
      <div class="checks">${kinds.map(([k, name]) => `<label><input type="checkbox" data-kind="${src}:${k}" ${skip.has(`${src}:${k}`) ? "" : "checked"}>
        <span>${name}<small>${fmt(counts[`${src}:${k}`] || 0)}</small></span></label>`).join("")}</div></div>`).join("")}</div>`;
}
document.addEventListener("change", async (e) => {
  const box = e.target.closest("[data-kind]");
  if (!box) return;
  const skip = new Set(state.app.skipKinds || []);
  if (box.checked) skip.delete(box.dataset.kind); else skip.add(box.dataset.kind);
  try {
    state.app.skipKinds = (await api.put("/api/song-kinds", { skip: [...skip] })).skip;
    api.get("/api/home").then((d) => { state.stats = d.stats; updateMeta(); }).catch(() => {});
  } catch (err) {
    box.checked = !box.checked;
    toast(err.message, { error: true });
  }
});
function discordCard(d) {
  const status = !d.on ? "Off" : d.status === "connected" ? "Showing on your profile"
    : d.status === "idle" ? "Shows up when a song plays" : d.status;
  return `<div class="data-card"><div class="eyebrow">06 // Discord</div><h3>Discord status</h3>
    <p>Shows what you listen to on your Discord profile: title, artist, anime or VN, and how far into the song you are.</p>
    <div class="actions" style="margin-top:0"><button class="btn ${d.on ? "on" : ""}" data-discord>${d.on ? "On" : "Off"}</button></div>
    <p class="mono" style="margin-top:12px;text-transform:none;letter-spacing:0">${esc(status || "")}</p></div>`;
}
async function saveDiscord(change) {
  try {
    state.app.discord = await api.put("/api/discord", change);
    if (state.app.discord.on) reportPresence();
  } catch (err) {
    toast(err.message, { error: true, ms: 7000 });
    return;
  }
  render();
}
function blurCard() {
  return `<div class="data-card"><div class="eyebrow">07 // Clips</div><h3>Adult works</h3>
    <p>Blur the clip and picture of songs from 18+ visual novels (VNDB) and adult anime (AniList). B or the button in the big view still blurs or shows any of them.</p>
    <div class="actions" style="margin-top:0"><button class="btn ${autoBlurAdult ? "on" : ""}" data-auto-blur>${autoBlurAdult ? "On" : "Off"}</button></div></div>`;
}

/* ================================================================== export, theme, keys */
async function exportData() {
  const data = await api.get("/api/export");
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob);
  a.download = `mumusic-export-${new Date().toISOString().slice(0, 10)}.json`;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 2000);
  toast(`Exported ${data.likes.length} likes and ${data.playlists.length} playlists`);
}

function effectiveTheme() {
  const t = document.documentElement.dataset.theme;
  return t || (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light");
}
function setTheme(t) {
  if (t) document.documentElement.dataset.theme = t; else delete document.documentElement.dataset.theme;
  try { t ? localStorage.setItem("theme", t) : localStorage.removeItem("theme"); } catch { /* ignore */ }
  renderThemeBtn();
}
function renderThemeBtn() {
  const dark = effectiveTheme() === "dark";
  $("#themeBtn").innerHTML = `${ic(dark ? "sun" : "moon")}${dark ? "Light" : "Dark"}`;
}
$("#themeBtn").onclick = () => setTheme(effectiveTheme() === "dark" ? "light" : "dark");
matchMedia("(prefers-color-scheme: dark)").addEventListener("change", renderThemeBtn);

document.addEventListener("keydown", (e) => {
  const typing = e.target.matches("input, textarea") || e.target.isContentEditable;
  if (e.key === "Escape") {
    const menuOpen = !menuEl.hidden;
    closeMenu();
    if (!$("#modal").hidden) return;
    if (typing) e.target.blur();
    else if (!menuOpen && !stage.hidden) { closeStage(); return; }
  }
  if (typing || !$("#modal").hidden) return;
  if (e.key === "/") { e.preventDefault(); searchInput.focus(); searchInput.select(); }
  else if (e.key === " ") { e.preventDefault(); togglePlay(); }
  else if (e.key === "ArrowRight" && e.shiftKey) next();
  else if (e.key === "ArrowLeft" && e.shiftKey) prev();
  else if (e.key === "ArrowRight") audio.currentTime = Math.min((audio.duration || 0), audio.currentTime + 5);
  else if (e.key === "ArrowLeft") audio.currentTime = Math.max(0, audio.currentTime - 5);
  else if (e.key.toLowerCase() === "b" && !e.ctrlKey && !e.altKey) toggleBlur();
});

$("#newPlaylistBtn").innerHTML = ic("plus");
$("#aniListBtn").innerHTML = ic("anilist");
$("#aniListBtn").onclick = importAniList;
$("#vndbBtn").innerHTML = ic("vndb");
$("#vndbBtn").onclick = importVndb;
$("#newPlaylistBtn").onclick = async () => { const pid = await createPlaylist(); if (pid) location.hash = `#/playlist/${pid}`; };
$("#optionsBtn").innerHTML = `${ic("sliders")}Options`;
$("#searchIcon").innerHTML = ic("search");
$("#navToggle").innerHTML = ic("list");
$("#navToggle").onclick = () => document.querySelector(".app").classList.toggle("nav-open");


/* ================================================================== window: title bar, resize edges, tray */
// The desktop host (src/MUMUsic.Desktop/Windowing/HostBridge.cs) over WebView2 messages: requests {id, method, params}
// get {id, result | error}; the host also sends events {event, payload}. In a plain browser there is no host and the
// window buttons are hidden.
const shell = (() => {
  const webview = window.chrome?.webview;
  if (!webview) return null;
  const waiting = new Map();
  let lastId = 0;
  webview.addEventListener("message", (e) => {
    const m = e.data;
    if (m.event === "windowState") onWindowState(m.payload);
    else if (m.event === "tray") trayCommand(m.payload);
    else if (m.event === "open" && started) takeOpenLink();
    const w = waiting.get(m.id);
    if (!w) return;
    waiting.delete(m.id);
    if (m.error) w.reject(new Error(m.error)); else w.resolve(m.result);
  });
  const call = (method, ...params) => new Promise((resolve, reject) => {
    const id = ++lastId;
    waiting.set(id, { resolve, reject });
    webview.postMessage({ id, method, params });
  });
  return Object.fromEntries(["minimize", "toggle_maximize", "is_maximized", "close", "quit", "start_move", "start_resize",
    "pick_library_file", "open_url", "take_open"].map((name) => [name, (...params) => call(name, ...params)]));
})();
const bridge = () => shell;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

function onWindowState(maximized) {
  document.body.classList.toggle("maximized", !!maximized);
  $("#maxIcon").innerHTML = maximized ? '<path d="M2.5 4.5h5v5h-5zM4.5 4.5v-2h5v5h-2"/>' : '<path d="M2.5 2.5h7v7h-7z"/>';
  $('[data-win="maximize"]').title = maximized ? "Restore" : "Maximize";
}
if (shell) {
  document.body.classList.add("desktop");
  shell.is_maximized().then(onWindowState);
  audioGraph();       // the host allows sound without a click, and a song opened from a link plays through the host too
}

$("#titlebar").addEventListener("click", (e) => {
  const b = e.target.closest("[data-win]");
  if (!b || !bridge()) return;
  if (b.dataset.win === "minimize") bridge().minimize();
  else if (b.dataset.win === "maximize") bridge().toggle_maximize().then(onWindowState);
  else bridge().close();
});
// Dragging is handed to Windows (snap, multi-monitor); that swallows the browser's double click, so it is
// detected here from two quick presses.
let lastBarPress = 0;
$("#titlebar").addEventListener("mousedown", (e) => {
  if (e.button !== 0 || e.target.closest("button") || !bridge()) return;
  if (Date.now() - lastBarPress < 400) { lastBarPress = 0; bridge().toggle_maximize().then(onWindowState); return; }
  lastBarPress = Date.now();
  bridge().start_move();
});
$("#resizeEdges").addEventListener("mousedown", (e) => {
  const edge = e.target.dataset.edge;
  if (edge && e.button === 0 && bridge()) { e.preventDefault(); bridge().start_resize(edge); }
});
function trayCommand(cmd) {
  if (cmd === "play") togglePlay();
  else if (cmd === "next") next();
  else if (cmd === "prev") prev();
}
function openExternal(url) {
  if (bridge()) bridge().open_url(url);
  else window.open(url, "_blank", "noopener");
}

/* ================================================================== libraries in use: AMQ, EMQ or both */
const SOURCE_CHOICES = [["amq", "AMQ", ["amq"]], ["emq", "EMQ", ["emq"]], ["both", "Both", ["amq", "emq"]]];
const SOURCE_TITLE = { amq: "Anime songs (animemusicquiz.com)", emq: "Eroge & VN songs (erogemusicquiz.com)", both: "Both libraries" };
const sourceName = () => (state.sources.length > 1 ? "AMQ + EMQ" : state.sources[0].toUpperCase());

function renderSources() {
  const cur = state.sources.length > 1 ? "both" : state.sources[0];
  $("#sourceSwitch").innerHTML = SOURCE_CHOICES.map(([k, label]) =>
    `<button role="radio" aria-checked="${k === cur}" class="${k === cur ? "active" : ""}" data-source="${k}" title="${SOURCE_TITLE[k]}">${label}</button>`).join("");
  $("#aniListBtn").hidden = !state.sources.includes("amq");
  $("#vndbBtn").hidden = !state.sources.includes("emq");
  $("#brandSub").textContent = `${sourceName()} // ARCHIVE`;
}
$("#sourceSwitch").addEventListener("click", async (e) => {
  const b = e.target.closest("[data-source]");
  if (!b || b.classList.contains("active")) return;
  const sources = SOURCE_CHOICES.find(([k]) => k === b.dataset.source)[2];
  const box = $("#sourceSwitch");
  box.classList.add("busy");
  try {
    await api.put("/api/sources", { sources });
    state.sources = sources;
    renderSources();
    await render();
    api.get("/api/home").then((d) => { state.stats = d.stats; updateMeta(); }).catch(() => {});
    toast(`Library: ${sourceName()}`);
  } catch (err) {
    toast(err.message, { error: true });
  } finally {
    box.classList.remove("busy");
  }
});

/* ================================================================== start: loading, first start (access key), updates */
const splash = $("#splash");
const splashCard = (inner) => `<div class="splash-card"><div class="splash-logo"><img src="/icon.png" alt=""></div>${inner}</div>`;

function jobHtml(job) {
  const pct = Math.round((job.progress || 0) * 100);
  const step = { downloading: "Downloading", decrypting: "Decrypting", installing: "Installing", done: "Done" }[job.step] || "Starting";
  const busy = job.step === "installing" || job.step === "idle";
  return `<div class="job"><div class="mono">${step}${busy ? "…" : ` · ${pct}%`}</div>
    <div class="job-bar ${busy ? "indeterminate" : ""}"><i style="width:${pct}%"></i></div></div>`;
}
// Polls the background job (download / decrypt / install) until it is done; throws its error.
async function waitJob(paint) {
  for (;;) {
    const job = await api.get("/api/job");
    paint(job);
    if (job.step === "error") throw new Error(job.error);
    if (job.step === "done") return job;
    await sleep(400);
  }
}

function renderSetup(error = "") {
  const online = state.app?.online;
  splash.innerHTML = splashCard(`
    <div class="eyebrow">MUMUsic // First start</div>
    <h1>Welcome to<br><em>MUMUsic</em></h1>
    <p>The song library is encrypted. Enter the access key you were given${online
      ? ": the library downloads once (about 150 MB), then updates itself when a new version is out."
      : ", then open the library file (.mumu)."}</p>
    <input type="password" id="setupKey" placeholder="Access key" autocomplete="off" spellcheck="false">
    <div class="error" id="setupError">${esc(error)}</div>
    <div class="actions">
      ${online ? `<button class="btn primary" data-setup="online">${ic("download")}Download the library</button>` : ""}
      <button class="btn ${online ? "ghost" : "primary"}" data-setup="file">${ic("upload")}Open a library file</button>
    </div>`);
  $("#setupKey").focus();
}
splash.addEventListener("click", async (e) => {
  const b = e.target.closest("[data-setup]");
  if (!b) return;
  const key = $("#setupKey").value.trim();
  if (!key) { $("#setupError").textContent = "Enter the access key"; return; }
  let file = null;
  if (b.dataset.setup === "file") {
    file = await bridge()?.pick_library_file();
    if (!file) return;
  }
  $$("[data-setup]", splash).forEach((x) => (x.disabled = true));
  try {
    await api.post("/api/setup", { key, file });
    await waitJob((job) => {
      splash.innerHTML = splashCard(`<div class="eyebrow">MUMUsic // Library</div><h1>Getting<br><em>ready</em></h1>${jobHtml(job)}`);
    });
    await startApp();
  } catch (err) {
    renderSetup(err.message);
  }
});
splash.addEventListener("keydown", (e) => {
  if (e.key === "Enter" && e.target.id === "setupKey") $("[data-setup]", splash)?.click();
});

async function runJob(url, title, note, body) {
  await api.post(url, body || {});
  const wrap = $("#modal");
  wrap.hidden = false;
  try {
    await waitJob((job) => {
      wrap.innerHTML = `<div class="modal" role="dialog" aria-modal="true"><header><div class="eyebrow">Update</div><h3>${esc(title)}</h3></header>
        <div class="body">${note ? `<p>${esc(note)}</p>` : ""}${jobHtml(job)}</div></div>`;
    });
  } finally {
    wrap.hidden = true;
    wrap.innerHTML = "";
  }
}

async function offerAppUpdate(info) {
  const answer = await modal({
    eyebrow: "Update", title: `MUMUsic ${info.version}`, confirm: info.canInstall ? "Update now" : "Open the download page",
    cancel: "Later", alt: "Skip this version",
    body: `<p>A new version of the app is out (you have ${esc(info.current)}).</p>${info.notes ? `<div class="update-notes">${esc(info.notes)}</div>` : ""}`,
  });
  if (answer === "alt") { await api.post("/api/updates/skip", { version: info.version }); return; }
  if (!answer) return;
  if (!info.canInstall) { openExternal(info.page); return; }
  try {
    await runJob("/api/updates/app", "Updating MUMUsic", "The app restarts on its own once the update is installed.");
  } catch (err) { toast(`Update failed: ${err.message}`, { error: true, ms: 8000 }); }
}

async function offerLibraryUpdate(info) {
  if (info.needsApp) { toast("A new song library is out: it needs the latest version of the app", { ms: 7000 }); return; }
  const counts = info.songs ? ` — ${fmt(info.songs.amq || 0)} AMQ and ${fmt(info.songs.emq || 0)} EMQ songs` : "";
  const ok = await modal({
    eyebrow: "Update", title: "New song library", confirm: "Update now", cancel: "Later",
    body: `<p>Library <b class="mono">${esc(info.version)}</b>${counts}. Download: ${Math.round((info.size || 0) / 1e6)} MB.
      Your likes and playlists are kept.</p>`,
  });
  if (!ok) return;
  let url = "/api/updates/library", body = null;
  for (;;) {
    try {
      await runJob(url, "Updating the library", "", body);
      state.app = await api.get("/api/app");
      await render();
      toast("Library updated");
      return;
    } catch (err) {
      if (!/access key/i.test(err.message)) { toast(`Update failed: ${err.message}`, { error: true, ms: 8000 }); return; }
      const key = await modal({ eyebrow: "Update", title: "Access key", input: "", inputType: "password", confirm: "Continue",
        body: `<p>${esc(err.message)}.</p>` });
      if (!key) return;
      url = "/api/updates/key";
      body = { key };
    }
  }
}

async function checkUpdates(manual = false) {
  if (!state.app?.online) return;
  let u;
  try { u = await api.get(`/api/updates${manual ? "?refresh=1" : ""}`); } catch (err) {
    if (manual) toast(`Could not check for updates: ${err.message}`, { error: true });
    return;
  }
  if (u.app) await offerAppUpdate(u.app);
  if (u.library) await offerLibraryUpdate(u.library);
  if (manual && !u.app && !u.library) toast("Everything is up to date");
}

let started = false;

// A friend's "Listen on MUMUsic" button (mumusic://play/<song>): the host keeps the song until the page is ready.
async function takeOpenLink() {
  const id = await shell?.take_open().catch(() => null);
  if (!id) return;
  try { await ensureSongs([id]); } catch { /* checked below */ }
  const s = state.cache.get(id);
  if (!s) { toast("That song isn't in your library: update the library and try again", { error: true, ms: 7000 }); return; }
  await playList([id], 0, { shuffle: false });
  toast(`Playing “${s.name}” from a friend's Discord`);
}
async function startApp() {
  state.app = await api.get("/api/app");
  state.sources = state.app.sources;
  renderSources();
  splash.hidden = true;
  $("#app").hidden = false;
  renderPlayer();
  try {
    await Promise.all([loadLikes(), loadPlaylists()]);
  } catch (e) {
    toast(`Can't reach the library: ${e.message}`, { error: true, ms: 8000 });
  }
  await render();
  if (!started) restoreSession();
  started = true;
  takeOpenLink();
  api.get("/api/home").then((d) => { state.stats = d.stats; updateMeta(); }).catch(() => {});
  checkUpdates();
}

(async function boot() {
  renderThemeBtn();
  splash.innerHTML = splashCard(`<div class="eyebrow">MUMUsic // Starting</div><h1>Loading<br><em>songs</em></h1>${loading("Loading the library")}`);
  for (;;) {
    try { state.app = await api.get("/api/app"); } catch { await sleep(500); continue; }
    if (state.app.status === "ready") return startApp();
    if (state.app.status === "setup") return renderSetup();
    if (state.app.status === "error") {
      splash.innerHTML = splashCard(`<div class="eyebrow">MUMUsic // Error</div><h1>Could not<br><em>load</em></h1><p>${esc(state.app.error)}</p>`);
      return;
    }
    await sleep(300);
  }
})();
